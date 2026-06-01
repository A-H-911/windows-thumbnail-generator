using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using ThumbnailPrimer;

namespace ThumbnailPrimer.Tests;

/// <summary>
/// Integration tests for ThumbnailCachePrimer.PrimeAsync.
/// These call the real Windows Shell COM APIs and require a working Windows Shell.
/// </summary>
public sealed class PrimeAsyncTests : IDisposable
{
    private readonly string _dir;

    public PrimeAsyncTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ThumbnailPrimerIntegration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a real, thumbnailable JPEG at the given path.</summary>
    private static string CreateJpeg(string folder, string name, Color color)
    {
        var path = Path.Combine(folder, name);
        using var bmp = new Bitmap(256, 256);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        bmp.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static IProgress<PrimeProgress> SilentProgress() =>
        new Progress<PrimeProgress>(_ => { });

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsNull_WhenFolderContainsNoImages()
    {
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "not an image");

        bool? result = await ThumbnailCachePrimer.PrimeAsync(
            _dir, recurse: false, thumbnailSize: 256,
            SilentProgress(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CompletesSuccessfully_WithValidJpegs()
    {
        // 5 solid-colour 256×256 JPEGs — real enough for the Windows Shell thumbnail extractor.
        CreateJpeg(_dir, "red.jpg",    Color.Red);
        CreateJpeg(_dir, "green.jpg",  Color.Green);
        CreateJpeg(_dir, "blue.jpg",   Color.Blue);
        CreateJpeg(_dir, "yellow.jpg", Color.Yellow);
        CreateJpeg(_dir, "white.jpg",  Color.White);

        bool? result = await ThumbnailCachePrimer.PrimeAsync(
            _dir, recurse: false, thumbnailSize: 256,
            SilentProgress(), CancellationToken.None);

        // true  = cache write confirmed by WTS_INCACHEONLY spot-check
        // false = primed but cache write could not be verified (still OK — task did not throw)
        // null would indicate no files were processed — that would be a bug.
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ReportsProgress_ForEachBatch()
    {
        for (int i = 0; i < 5; i++)
            CreateJpeg(_dir, $"img_{i}.jpg", Color.FromArgb(i * 40, i * 40, i * 40));

        int reportCount = 0;
        var progress = new Progress<PrimeProgress>(_ => Interlocked.Increment(ref reportCount));

        await ThumbnailCachePrimer.PrimeAsync(
            _dir, recurse: false, thumbnailSize: 256,
            progress, CancellationToken.None);

        // ProgressInterval = 50, but ≤5 files always fires on the last file.
        Assert.True(reportCount >= 1, $"Expected ≥1 progress report, got {reportCount}.");
    }

    [Fact]
    public async Task ThrowsOperationCanceled_WhenTokenAlreadyCancelled()
    {
        // 30 files so there is definitely work queued when the cancel is seen.
        for (int i = 0; i < 30; i++)
            CreateJpeg(_dir, $"img_{i:D2}.jpg", Color.FromArgb(i * 8, i * 8, i * 8));

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before calling PrimeAsync

        // Awaiting a cancelled Task raises TaskCanceledException (subclass of OperationCanceledException).
        var ex = await Record.ExceptionAsync(() =>
            ThumbnailCachePrimer.PrimeAsync(
                _dir, recurse: false, thumbnailSize: 256,
                SilentProgress(), cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public void Recursive_FindsBothRootAndSubdirectoryImages()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        CreateJpeg(_dir, "root.jpg", Color.Red);
        CreateJpeg(sub,  "sub.jpg",  Color.Blue);

        var files = ThumbnailCachePrimer.CollectImages(_dir, recurse: true);

        Assert.Equal(2, files.Count);
    }

    // ── live test with downloaded images ─────────────────────────────────────

    /// <summary>
    /// Runs only when C:\TestImages has ≥200 JPEGs (populated by scripts\download-test-images.ps1).
    /// Confirms PrimeAsync completes and processes ≥90% of the files.
    /// </summary>
    [Fact]
    public async Task LiveTest_WithDownloadedImages_PrimesSuccessfully()
    {
        const string downloadFolder = @"C:\TestImages";
        if (!Directory.Exists(downloadFolder))
            return; // download script not yet run — skip silently

        var imageCount = Directory.GetFiles(downloadFolder, "*.jpg").Length;
        if (imageCount < 200)
            return; // not enough images — skip silently

        int lastDone = 0;
        var progress = new Progress<PrimeProgress>(p =>
            Interlocked.Exchange(ref lastDone, p.Done));

        bool? result = await ThumbnailCachePrimer.PrimeAsync(
            downloadFolder, recurse: false, thumbnailSize: 256,
            progress, CancellationToken.None);

        Assert.True(lastDone >= (int)(imageCount * 0.9),
            $"Expected ≥90% of {imageCount} files primed, processed {lastDone}.");
    }
}
