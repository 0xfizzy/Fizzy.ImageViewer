# Validation

Windows and .NET 8+ SDK are required. No real cameras or motion devices are needed.

```powershell
dotnet build Fizzy.ImageViewer.csproj -c Release
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
```

The optional Robot adapter suite expects the sibling RobotController checkout. It compiles the
actual adapter source files and references Robot.Core; this avoids loading the application's
plugin deployment graph into the test host. Full Source build separately verifies integration.

```powershell
dotnet test tests/RobotAdapter.Tests/RobotAdapter.Tests.csproj -c Debug
../scripts/Build.ps1 -Target RobotController -Mode Source -Configuration Debug
```

The library suite uses independent hidden STA windows, injected presenters and explicit gates
for queue tests. It exercises ownership, pixels, submission/notification order, frozen snapshots,
format changes and TIFF readback. Passing it does not verify physical screen presentation,
mixed-DPI monitors, remote desktop or real hardware. These require a separate interactive check.
Parallax has a CPU adapter and a CUDA/D3DImage adapter. The latter requires a local NVIDIA GPU,
matching CUDA/D3D9 adapter and the native display DLL. Run its native `display_contracts` test
and `Parallax.exe --profile simulated --smoke --smoke-cuda-display` as documented in the
Parallax CUDA component guide. Those checks cover actual surface pixels and zero display D2H;
locked sessions, remote desktop and driver resets remain separate interactive checks.
No package release is performed by these validation commands.

Synthetic benchmark (hidden windows, commit timings, no hardware):

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
```

It covers 1080p/4K, Gray8/Bgr24, 30/60 input FPS, one/two viewers and line sampling on/off.
The direct WritePixels reference measures only the old copy operation, not an old application
build. Neither benchmark measures physical presentation or chart-window rendering. Allocation
counts include the harness; private-memory deltas include pooling, JIT and GC effects.

## Pixel queries

PixelQueryTests cover GPU ownership during async reads, CPU stride/finite statistics, explicit ROI
snapshots and query options. QuerySchedulingTests inject slow sources to verify merged sampling,
STA responsiveness, replacement, geometry invalidation, expiration and shutdown ownership.
Parallax real-device smoke covers native gather/statistics/ROI equivalence, 65,537-point chunking,
actual D2H counters, warmed pool reuse and final pinned/request cleanup. Its benchmark writes
`build/Debug/smoke/gpu-query-benchmark.json`: 30 sequential 1080p BGR operations per scenario.
Commit timing is a UI submission measurement, not monitor presentation; the benchmark's >100 ms
count is operation age, not the interactive coordinator's expired-result counter.
