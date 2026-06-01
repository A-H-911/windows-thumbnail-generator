<!-- Generated: 2026-05-31 | Updated: 2026-05-31 (line counts, InternalsVisibleTo) | Files scanned: 4 src + 3 test | Token estimate: ~550 -->

# Architecture — Thumbnail Cache Primer

## Project Type
Single-project WinForms desktop utility. `net10.0-windows`, `win-x64`, self-contained `.exe`.

## Entry Point
`Program.cs` (15 lines) — `[STAThread] Main()` → `ApplicationConfiguration.Initialize()` → `Application.Run(new MainForm())`

## Layout
```
src/    — application source + ThumbnailPrimer.csproj
tests/  — xUnit test project (ThumbnailPrimer.Tests.csproj)
```

## Source Files
```
src/Program.cs               (17 lines)   Entry point + [assembly: InternalsVisibleTo("ThumbnailPrimer.Tests")]
src/MainForm.cs             (276 lines)   WinForms UI — folder picker, options, progress, log
src/ThumbnailCachePrimer.cs (312 lines)   Core logic: enumeration + COM priming loop
src/NativeMethods.cs         (79 lines)   Windows Shell COM P/Invoke + interface declarations
src/Form1.cs / Form1.Designer.cs          Scaffold remnants — unused, safe to ignore
```

## Test Files
```
tests/CollectImagesTests.cs  (12 tests)   Extension filtering, recursion, file counts — no COM
tests/PrimeProgressTests.cs  ( 7 tests)   PrimeProgress record: equality, with-expression, props
tests/PrimeAsyncTests.cs     ( 6 tests)   Integration: real COM priming, cancellation, live 200-image run
```

## Data Flow
```
User clicks Prime Cache
  └─ MainForm.OnPrime (async void)
       ├─ ThumbnailCachePrimer.PrimeAsync(folder, recurse, size, progress, ct)
       │    ├─ CollectImages()         enumerate files, skip inaccessible dirs,
       │    │    └─ GatherImages()     detect symlink cycles via HashSet<string>
       │    ├─ CoCreateInstance(LocalThumbnailCache) via Activator.CreateInstance
       │    └─ loop: TryPrimeFile(cache, file, size)
       │         ├─ SHCreateItemFromParsingName(path) → IShellItem
       │         ├─ IThumbnailCache.GetThumbnail(item, size, WTS_EXTRACT)
       │         │    writes thumbnail to %LocalAppData%\...\thumbcache_NNN.db
       │         └─ FinalReleaseComObject(shellItem + bitmap)
       ├─ VerifyCacheEntry()   re-query with WTS_INCACHEONLY → bool?
       └─ Progress<PrimeProgress> callbacks → UI thread via SynchronizationContext
```

## Threading Model
```
UI thread (STA, WinForms message loop)
  └─ spawns: dedicated STA Thread  ← required; IThumbnailCache is STA COM
       TaskCompletionSource<bool?> bridges completion back to UI await
       Progress<T> marshals per-file callbacks to UI SynchronizationContext
```

## Resilience Guards (in RunPriming)
- `ConsecutiveFailureThreshold = 30` — hard abort on N consecutive failures
- `FailureRateThreshold = 0.80` after `FailureRateWarmup = 100` files — soft abort
- `GatherImages` cycle detection via `Path.GetFullPath` + `HashSet<string>`
- Per-directory `UnauthorizedAccessException` catch — skip, don't abort

## Key Types
```
PrimeProgress record   Done, Total, ErrorCount, LastFile, SkippedFile?
Task<bool?>            true=cache verified | false=unverified | null=nothing primed
FrozenSet<string>      ImageExtensions (9 extensions, OrdinalIgnoreCase)
```
