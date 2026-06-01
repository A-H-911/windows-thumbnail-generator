<!-- Generated: 2026-05-31 | Updated: 2026-05-31 (added test deps, src/ paths) | Token estimate: ~350 -->

# Dependencies — Thumbnail Cache Primer

## Runtime Platform
- **.NET 10** (`net10.0-windows`) — LTS, GA Nov 2025
- **WinForms** (`UseWindowsForms=true`) — UI framework
- **win-x64** — sole publish target; 32-bit not supported

## Windows Shell COM (no NuGet — OS-provided)
| Symbol | Source | Purpose |
|--------|--------|---------|
| `IThumbnailCache` | `thumbcache.dll` via `CLSID {50EF4544-...}` | Extract + persist thumbnails |
| `IShellItem` | `shell32.dll` | Shell handle for a file path |
| `ISharedBitmap` | `thumbcache.dll` | Bitmap returned by GetThumbnail |
| `SHCreateItemFromParsingName` | `shell32.dll` P/Invoke | Convert file path → IShellItem |

## .NET BCL APIs (notable)
- `System.Collections.Frozen.FrozenSet<T>` — read-only set, requires .NET 8+
- `Marshal.FinalReleaseComObject` — deterministic COM release
- `Interlocked.Exchange` — thread-safe CancellationTokenSource swap
- `System.Diagnostics.Trace` — per-file warning output (no logging framework)

## NuGet Packages (test project only — `tests/ThumbnailPrimer.Tests.csproj`)
| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.NET.Test.Sdk` | 17.12.0 | Test host and runner infrastructure |
| `xunit` | 2.9.3 | Test framework (`[Fact]`, `[Theory]`, `Assert`) |
| `xunit.runner.visualstudio` | 2.8.2 | VS / `dotnet test` adapter |

The app project (`src/ThumbnailPrimer.csproj`) has **zero** NuGet dependencies.

## Build Tooling
- **dotnet SDK 10.0.300** confirmed installed
- `TreatWarningsAsErrors=true` on both projects; `MSB3026`/`MSB3027` exempted for file-lock dev builds

## Publish Output
```
src\bin\Release\net10.0-windows\win-x64\publish\ThumbnailPrimer.exe
Self-contained, ~49 MB (EnableCompressionInSingleFile), no prerequisites
```
