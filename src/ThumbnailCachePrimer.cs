using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThumbnailPrimer;

/// <summary>Progress snapshot reported after each batch or skipped file.</summary>
internal sealed record PrimeProgress(
    int Done,
    int Total,
    int ErrorCount,
    string LastFile,
    string? SkippedFile = null);

/// <summary>
/// Enumerates image files in a folder and pre-warms the Windows Explorer
/// thumbnail cache by calling IThumbnailCache::GetThumbnail for each one.
/// All COM work runs on a dedicated STA background thread.
/// </summary>
internal static class ThumbnailCachePrimer
{
    // Comparer must be passed explicitly — ToFrozenSet() does NOT inherit the source HashSet's
    // comparer on .NET 10, so omitting it produces a case-sensitive set (verified by test).
    internal static readonly FrozenSet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp",
            ".tif", ".tiff", ".webp", ".heic", ".heif",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Every file (success or skip) is subject to the same throttle so the progress bar
    // advances at a uniform rate. Skips additionally always report for the log.
    private const int ProgressInterval = 50;

    // Trigger a COM recycle after N consecutive failures rather than aborting immediately.
    private const int ConsecutiveFailureThreshold = 30;

    // Proactively recycle the COM object every N files to prevent degradation on large runs.
    // LocalThumbnailCache can degrade silently after thousands of extractions; a fresh instance
    // reconnects to the Shell service and avoids hitting the consecutive threshold at all.
    private const int ComRecycleInterval = 2000;

    // Hard abort only after this many crisis recycles (each triggered by 30 consecutive failures).
    private const int MaxComRecycles = 3;

    // Soft abort: once past the warm-up, abort if > 80% of files have failed.
    // Catches a fundamentally broken COM object that recycling cannot fix.
    private const double FailureRateThreshold = 0.80;
    private const int FailureRateWarmup = 100;

    /// <summary>
    /// Starts thumbnail cache priming on a background STA thread.
    /// Returns: <c>true</c> = cache write confirmed; <c>false</c> = could not verify;
    /// <c>null</c> = no files were primed (empty folder or all failed before verification).
    /// </summary>
    public static Task<bool?> PrimeAsync(
        string folder,
        bool recurse,
        uint thumbnailSize,
        IProgress<PrimeProgress> progress,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Thread-pool threads are MTA; IThumbnailCache requires STA. A dedicated Thread is the
        // correct primitive here — SetApartmentState must be called before Start(), and
        // Task.Run / Parallel.ForEachAsync cannot satisfy that requirement.
        var thread = new Thread(() =>
        {
            try
            {
                bool? verified = RunPriming(folder, recurse, thumbnailSize, progress, cancellationToken);
                tcs.SetResult(verified);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }) { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <returns>
    /// <c>true</c> if a spot-check confirmed the cache entry was written,
    /// <c>false</c> if the spot-check found no entry, <c>null</c> if there was nothing to check.
    /// </returns>
    private static bool? RunPriming(
        string folder,
        bool recurse,
        uint size,
        IProgress<PrimeProgress> progress,
        CancellationToken ct)
    {
        var files = CollectImages(folder, recurse);

        if (files.Count == 0)
        {
            progress.Report(new PrimeProgress(0, 0, 0, string.Empty));
            return null;
        }

        var cache = CreateCacheInstance();
        try
        {
            int errorCount = 0;
            int consecutiveFails = 0;
            int recycleCount = 0;
            string? firstSuccessfulFile = null;

            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Proactive recycle every N files — prevents silent degradation on large folders
                // without waiting for 30 consecutive failures to accumulate first.
                if (i > 0 && i % ComRecycleInterval == 0)
                {
                    Trace.TraceInformation("RunPriming: proactive COM recycle at file {0}/{1}", i, files.Count);
                    cache = RecycleCacheInstance(cache);
                    consecutiveFails = 0;
                }

                string file = files[i];
                string name = Path.GetFileName(file);
                bool ok = TryPrimeFile(cache, file, size);

                if (!ok)
                {
                    errorCount++;
                    consecutiveFails++;

                    if (consecutiveFails >= ConsecutiveFailureThreshold)
                    {
                        if (recycleCount >= MaxComRecycles)
                            throw new InvalidOperationException(
                                $"Priming aborted after {recycleCount} COM recycle(s): " +
                                "the thumbnail cache server remains unresponsive.");

                        // Crisis recycle: release the degraded instance, pause briefly so the
                        // Shell service can recover, then create a fresh instance and continue.
                        Trace.TraceWarning(
                            "RunPriming: crisis COM recycle #{0} after {1} consecutive failures at file {2}",
                            recycleCount + 1, consecutiveFails, i);
                        Thread.Sleep(300);
                        cache = RecycleCacheInstance(cache);
                        consecutiveFails = 0;
                        recycleCount++;
                    }

                    if (i + 1 >= FailureRateWarmup &&
                        (double)errorCount / (i + 1) > FailureRateThreshold)
                        throw new InvalidOperationException(
                            $"Priming aborted: {errorCount:N0} / {i + 1:N0} files failed " +
                            $"({errorCount * 100 / (i + 1)}%) — COM server may be unresponsive.");
                }
                else
                {
                    consecutiveFails = 0;
                    firstSuccessfulFile ??= file;
                }

                // Report on: first file (immediate feedback), every N files, last file, or any skip.
                if (i == 0 || !ok || (i + 1) % ProgressInterval == 0 || i == files.Count - 1)
                    progress.Report(new PrimeProgress(i + 1, files.Count, errorCount, name,
                        SkippedFile: ok ? null : name));
            }

            if (firstSuccessfulFile is null)
                return null;

            // Spot-check: re-query the first successfully primed file with WTS_INCACHEONLY.
            return VerifyCacheEntry(cache, firstSuccessfulFile, size);
        }
        finally
        {
            Marshal.FinalReleaseComObject(cache);
        }
    }

    /// <summary>Creates a fresh IThumbnailCache COM instance; throws on failure.</summary>
    private static NativeMethods.IThumbnailCache CreateCacheInstance()
    {
        try
        {
            var cacheType = Type.GetTypeFromCLSID(NativeMethods.CLSID_LocalThumbnailCache)!;
            return (NativeMethods.IThumbnailCache)Activator.CreateInstance(cacheType)!;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"Windows thumbnail cache COM server unavailable (0x{ex.HResult:X8}). " +
                "Ensure the Windows Shell is functioning correctly.", ex);
        }
    }

    /// <summary>
    /// Creates a fresh IThumbnailCache instance then releases <paramref name="old"/>.
    /// Creating first ensures that if creation fails, <paramref name="old"/> is still intact
    /// and the caller's finally block can release it exactly once.
    /// </summary>
    private static NativeMethods.IThumbnailCache RecycleCacheInstance(NativeMethods.IThumbnailCache old)
    {
        var fresh = CreateCacheInstance(); // throws before old is touched if COM is unavailable
        Marshal.FinalReleaseComObject(old);
        return fresh;
    }

    /// <summary>
    /// Queries the cache for <paramref name="path"/> using WTS_INCACHEONLY — no extraction.
    /// Returns <c>true</c> if the entry exists in the cache, <c>false</c> otherwise.
    /// </summary>
    private static bool VerifyCacheEntry(NativeMethods.IThumbnailCache cache, string path, uint size)
    {
        try
        {
            Guid iid = NativeMethods.IID_IShellItem;
            int hr = NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var shellItem);
            if (hr != 0 || shellItem is null) return false;
            try
            {
                hr = cache.GetThumbnail(shellItem, size, NativeMethods.WTS_INCACHEONLY,
                    out var bitmap, out _, out _);
                if (bitmap is not null) Marshal.FinalReleaseComObject(bitmap);
                return hr == 0;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellItem);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceWarning("VerifyCacheEntry failed for '{0}': {1}", path, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Collects all image files under <paramref name="folder"/>, skipping inaccessible
    /// subdirectories and detecting symlink/junction cycles to prevent stack overflow.
    /// </summary>
    internal static IReadOnlyList<string> CollectImages(string folder, bool recurse)
    {
        var result = new List<string>();
        // Visited set is only needed during recursion; it guards against symlink/junction cycles.
        var visited = recurse ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        GatherImages(folder, recurse, result, visited);
        return result;
    }

    private static void GatherImages(string folder, bool recurse, List<string> result, HashSet<string>? visited)
    {
        // Resolve to canonical path before adding to visited so symlinks and junctions
        // that point back to an already-visited directory are detected and skipped.
        string realPath;
        try { realPath = Path.GetFullPath(folder); }
        catch (Exception ex)
        {
            Trace.TraceWarning("GatherImages: could not resolve path '{0}' — {1}", folder, ex.Message);
            return;
        }

        if (visited is not null && !visited.Add(realPath))
        {
            Trace.TraceWarning("GatherImages: cycle detected at '{0}' — skipping.", folder);
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (ImageExtensions.Contains(Path.GetExtension(file)))
                    result.Add(file);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("GatherImages: access denied listing files in '{0}' — {1}", folder, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            Trace.TraceWarning("GatherImages: directory not found '{0}' — {1}", folder, ex.Message);
        }

        if (!recurse) return;

        // Enumerate subdirectories separately so one inaccessible dir doesn't stop the rest.
        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(folder).ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning("GatherImages: access denied listing subdirectories of '{0}' — {1}", folder, ex.Message);
            return;
        }
        catch (DirectoryNotFoundException ex)
        {
            Trace.TraceWarning("GatherImages: directory not found '{0}' — {1}", folder, ex.Message);
            return;
        }

        foreach (string sub in subdirs)
            GatherImages(sub, recurse: true, result, visited);
    }

    /// <summary>
    /// Asks the thumbnail cache to extract and store the thumbnail for one file.
    /// Returns false for per-file errors. Lets OperationCanceledException propagate.
    /// </summary>
    private static bool TryPrimeFile(NativeMethods.IThumbnailCache cache, string path, uint size)
    {
        try
        {
            Guid iid = NativeMethods.IID_IShellItem;
            int hr = NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var shellItem);

            if (hr != 0 || shellItem is null)
                return false;

            try
            {
                hr = cache.GetThumbnail(shellItem, size, NativeMethods.WTS_EXTRACT,
                    out var bitmap, out _, out _);

                if (bitmap is not null)
                    Marshal.FinalReleaseComObject(bitmap);

                return hr == 0;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellItem);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceWarning("TryPrimeFile skipped '{0}': {1} — {2}", path, ex.GetType().Name, ex.Message);
            return false;
        }
    }
}
