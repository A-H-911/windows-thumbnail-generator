# Thumbnail Cache Primer

A Windows desktop utility that pre-warms Explorer's thumbnail cache so a folder full of large images shows thumbnails instantly — instead of generating them lazily while you scroll.

## How it works

Windows Explorer stores extracted thumbnails in a shared cache database under `%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db`. When you open a folder for the first time, Explorer generates thumbnails on demand, causing a visible delay for large collections.

This tool calls the same internal COM API Explorer uses — `IThumbnailCache::GetThumbnail` from `thumbcache.dll` — for every image in a folder, forcing Windows to extract and persist each thumbnail to the shared cache upfront. The next time you open the folder in Explorer (Extra Large Icons view), thumbnails are already there.

## Supported formats

`.jpg` `.jpeg` `.png` `.gif` `.bmp` `.tif` `.tiff` `.webp` `.heic` `.heif`

## Requirements

- **Windows 10 or 11** (x64)
- **.NET 10 runtime** — or use the self-contained `.exe` (see [Build](#build))

## Usage

1. Launch `ThumbnailPrimer.exe`
2. Click **Browse…** and select the image folder
3. Optionally tick **Include subfolders**
4. Choose a thumbnail size (256 px recommended — matches Explorer's Large Icons view)
5. Click **Prime Cache**

The progress bar tracks each file. Any files that could not be primed are listed in the **Skipped files** log. When complete, open the folder in Explorer with **View → Extra Large Icons** to confirm thumbnails are pre-loaded.

## Thumbnail sizes

| Size | Use case |
|------|----------|
| 96 px | Small Icons / Details pane |
| **256 px** | **Large Icons (recommended)** |
| 1024 px | Extra Large Icons |

Each size is cached independently. If you intend to use Extra Large Icons, prime at 1024 px.

## Build

```powershell
# Debug build
dotnet build src\ThumbnailPrimer.csproj -c Debug

# Run directly
dotnet run --project src\ThumbnailPrimer.csproj

# Self-contained single .exe (~49 MB, no .NET install required)
dotnet publish src\ThumbnailPrimer.csproj -c Release
# Output: src\bin\Release\net10.0-windows\win-x64\publish\ThumbnailPrimer.exe
```

The build is warning-free (`TreatWarningsAsErrors=true`). `MSB3026`/`MSB3027` file-lock retries are exempted so you can rebuild while the app is running.

## Tests

```powershell
# Run all tests
dotnet test tests\ThumbnailPrimer.Tests.csproj

# Run a single test class
dotnet test tests\ThumbnailPrimer.Tests.csproj --filter "FullyQualifiedName~CollectImagesTests"
```

## Project layout

```
src/
  NativeMethods.cs          Windows Shell COM interop (P/Invoke + COM declarations)
  ThumbnailCachePrimer.cs   Core logic: file enumeration + COM priming loop
  MainForm.cs               WinForms UI
  Program.cs                [STAThread] entry point
  ThumbnailPrimer.csproj

tests/
  CollectImagesTests.cs     Image collection and cycle-detection tests
  PrimeProgressTests.cs     Progress reporting tests
  PrimeAsyncTests.cs        End-to-end priming tests
  ThumbnailPrimer.Tests.csproj

docs/
  CODEMAPS/                 Architecture and dependency maps
  diagrams/                 Swim-lane process diagram
  CONTRIBUTING.md
```

## Design notes

**Threading** — `IThumbnailCache` is an STA COM server. All COM work runs on a dedicated `Thread` with `ApartmentState.STA` set before `Start()`. A `TaskCompletionSource<bool?>` bridges the result back to the UI's `async/await`. `Progress<PrimeProgress>` marshals progress callbacks to the UI's `SynchronizationContext`.

**COM resilience** — Three safeguards prevent a degraded COM object from stalling a large run:

1. *Proactive recycle* every 2,000 files — reconnects to the Shell service before degradation builds up.
2. *Crisis recycle* after 30 consecutive failures — releases the broken instance, waits 300 ms, and creates a fresh one (up to 3 times).
3. *Failure-rate abort* — if more than 80% of files fail after the first 100, the run is aborted with a clear error rather than grinding through the remainder.

**Cycle detection** — `CollectImages` resolves each path with `Path.GetFullPath` and tracks a `HashSet<string>` of visited directories to detect symlink/junction loops before recursing.

**Post-completion verification** — After priming, the first successfully primed file is re-queried with `WTS_INCACHEONLY` (no extraction). The result indicates whether the cache write was confirmed (`true`), unconfirmed (`false`), or nothing was primed (`null`).

## License

MIT
