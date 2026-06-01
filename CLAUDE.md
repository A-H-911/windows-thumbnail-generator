# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Layout

```
src/      — application source code and ThumbnailPrimer.csproj
tests/    — xUnit test project (ThumbnailPrimer.Tests.csproj)
docs/     — CODEMAPS, CONTRIBUTING
scripts/  — helper scripts
```

## Commands

```powershell
# Build app
dotnet build src\ThumbnailPrimer.csproj -c Debug

# Run app
dotnet run --project src\ThumbnailPrimer.csproj

# Run tests
dotnet test tests\ThumbnailPrimer.Tests.csproj

# Run a single test class
dotnet test tests\ThumbnailPrimer.Tests.csproj --filter "FullyQualifiedName~CollectImagesTests"

# Publish single self-contained .exe (~49 MB, no .NET install required)
dotnet publish src\ThumbnailPrimer.csproj -c Release
# Output: src\bin\Release\net10.0-windows\win-x64\publish\ThumbnailPrimer.exe
```

`TreatWarningsAsErrors=true` — the build must be warning-free. `MSB3026`/`MSB3027` (file-lock retries) are exempted so you can build while the app is running.

## Architecture

Single-project WinForms app (`net10.0-windows`, `win-x64`) with four active source files in `src/`:

```
src/NativeMethods.cs        — Windows Shell COM interop surface (P/Invoke + COM declarations)
src/ThumbnailCachePrimer.cs — Core logic: file enumeration + COM priming loop
src/MainForm.cs             — WinForms UI wired to ThumbnailCachePrimer
src/Program.cs              — [STAThread] entry point
```

`src/Form1.cs` / `src/Form1.Designer.cs` are scaffold remnants — they compile but are unused.

### What it does

Pre-warms Windows Explorer's thumbnail cache (`%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db`) so opening a folder with many large images shows thumbnails instantly instead of generating them lazily while scrolling.

**Core mechanism:** `IThumbnailCache::GetThumbnail` (Windows Shell COM, `thumbcache.dll`) with `WTS_EXTRACT = 0x0` is the same API Explorer uses internally. Calling it for each file forces Windows to extract and persist the thumbnail to the shared cache database.

### Threading model

All COM work runs on a dedicated `Thread` (not thread pool) with `ApartmentState.STA` set before `Start()` — required because `IThumbnailCache` is an STA COM server. `TaskCompletionSource<bool?>` bridges the STA thread back to the UI's `async/await`. `Progress<PrimeProgress>` marshals callbacks to the captured UI `SynchronizationContext`.

### Post-completion verification

After the loop, `VerifyCacheEntry` re-queries the first successfully primed file with `WTS_INCACHEONLY = 0x1` (cache-only — no extraction). Return value of `Task<bool?>`: `true` = cache write confirmed, `false` = not found in cache (may indicate `LocalThumbnailCache` doesn't persist from non-Explorer processes), `null` = nothing to check.

### COM interop specifics

- `NativeMethods.IShellItem` and `ISharedBitmap` are **intentionally empty** interfaces — only their COM pointers are passed/received; no methods are called on them.
- `IThumbnailCache` vtable: `GetThumbnail` is slot 3, `GetThumbnailByID` is slot 4. Both must be declared in order even though only slot 3 is called.
- All release calls use `Marshal.FinalReleaseComObject` (not `ReleaseComObject`) to drive the ref count to zero immediately, bypassing CLR internal references.
- `Type.GetTypeFromCLSID` never returns null — it always creates a COM stub `Type`. Real failures come from `Activator.CreateInstance` as `COMException`.
- `WTS_THUMBNAILID` is a 16-byte blittable struct (`{ long _part1; long _part2 }`). Fields are suppressed with `#pragma CS0169` — filled by the native caller, never read in managed code.

### Resilience in `RunPriming`

Two abort thresholds guard against a dead COM object:
1. **30 consecutive failures** — hard abort.
2. **> 80% failure rate after 100-file warm-up** — catches a slowly-degrading COM object that occasionally succeeds (which would reset the consecutive counter).

`GatherImages` resolves each path with `Path.GetFullPath` and tracks a `HashSet<string>` of visited directories to detect symlink/junction cycles before recursing — prevents stack overflow. Inaccessible subdirectories are skipped via per-directory `UnauthorizedAccessException` handling rather than aborting the entire run.
