# Contributing

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| Windows | 10 / 11 (x64) | Runtime and COM shell APIs are Windows-only |
| .NET SDK | 10.0+ | `dotnet --version` to verify |

## Commands

<!-- AUTO-GENERATED from src/ThumbnailPrimer.csproj + tests/ThumbnailPrimer.Tests.csproj -->

| Command | Description |
|---------|-------------|
| `dotnet build src\ThumbnailPrimer.csproj -c Debug` | Compile app; output to `src\bin\Debug\net10.0-windows\win-x64\` |
| `dotnet run --project src\ThumbnailPrimer.csproj` | Build and launch the app in one step |
| `dotnet test tests\ThumbnailPrimer.Tests.csproj` | Run all 34 tests (unit + integration) |
| `dotnet test tests\ThumbnailPrimer.Tests.csproj --filter "FullyQualifiedName~<Class>"` | Run a single test class |
| `dotnet publish src\ThumbnailPrimer.csproj -c Release` | Self-contained single `.exe` (~49 MB) in `src\bin\Release\net10.0-windows\win-x64\publish\` |

<!-- END AUTO-GENERATED -->

## Code Style

<!-- AUTO-GENERATED from ThumbnailPrimer.csproj -->

`TreatWarningsAsErrors=true` is set in the project file — the build fails on any compiler warning. `MSB3026`/`MSB3027` (file-lock retries) are the only exemptions, present so you can build while the app is running. There is no formatter hook configured.

<!-- END AUTO-GENERATED -->

## Testing

### Automated tests

34 tests in `tests/` — run with `dotnet test tests\ThumbnailPrimer.Tests.csproj`.

| Class | Type | What it covers |
|-------|------|----------------|
| `CollectImagesTests` | Unit | Extension filtering, case-insensitivity, recursion, file counts |
| `PrimeProgressTests` | Unit | Record equality, `with` expressions, property values |
| `PrimeAsyncTests` | Integration (COM) | Real JPEG priming, progress reporting, cancellation, live 200-image test |

The live test (`LiveTest_WithDownloadedImages_PrimesSuccessfully`) requires `C:\TestImages` to contain ≥ 200 `.jpg` files. Download them with:
```powershell
# Download 220 stock photos from picsum.photos
$folder = "C:\TestImages"; New-Item -ItemType Directory -Force $folder | Out-Null
1..220 | ForEach-Object { Invoke-WebRequest "https://picsum.photos/seed/$_/800/600" -OutFile "$folder\img_$($_.ToString('D3')).jpg" -UseBasicParsing }
```

### Manual verification

1. Clear the thumbnail cache: **Disk Cleanup → Thumbnails**, or delete `%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db` with Explorer closed.
2. Run `dotnet run --project src\ThumbnailPrimer.csproj` and prime a folder of large images.
3. Open the folder in Explorer at **View → Extra Large Icons** — thumbnails should render immediately with no lazy fill-in while scrolling.
4. The status bar shows the post-completion `WTS_INCACHEONLY` spot-check result: cache write confirmed (`true`) or unverified (`false`).

## COM Interop Changes

When modifying `NativeMethods.cs`:

- `IShellItem` and `ISharedBitmap` are intentionally empty `[ComImport]` interfaces — no methods are ever called on them.
- `IThumbnailCache` vtable slots must stay in declaration order: `GetThumbnail` (slot 3), `GetThumbnailByID` (slot 4). Reordering corrupts the vtable.
- Use `Marshal.FinalReleaseComObject` everywhere, not `Marshal.ReleaseComObject`. The CLR may hold internal references that `ReleaseComObject` will not clear.
- `Type.GetTypeFromCLSID` never returns null. The real failure is in `Activator.CreateInstance` — wrap that in `catch (COMException)`.

## PR Checklist

- [ ] `dotnet build src\ThumbnailPrimer.csproj -c Debug` — 0 warnings, 0 errors
- [ ] `dotnet test tests\ThumbnailPrimer.Tests.csproj` — 0 failures
- [ ] `dotnet publish src\ThumbnailPrimer.csproj -c Release` produces a runnable `.exe`
- [ ] Manual verification completed (see Testing above)
- [ ] `docs/CODEMAPS/` updated if new source files or COM surfaces were added
