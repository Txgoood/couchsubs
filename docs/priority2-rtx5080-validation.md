# Priority 2 RTX 5080 validation

The optional Windows DirectML build uses the RTX GPU for the Marian
English-to-Turkish encoder. The decoder stays on CPU. The normal build
continues to use ONNX Runtime 1.31.1 on CPU. The optional build uses
the published DirectML package 1.24.4 and requires an explicit runtime
setting. If DirectML session creation or inference fails, Marian uses
a fresh CPU encoder session.

## Build and select the adapter

```powershell
dotnet publish windows/src/CouchSubs.App -c Release -r win-x64 --self-contained -p:CouchSubsOnnxDirectML=true -o publish/CouchSubs-DirectML
$env:COUCHSUBS_MARIAN_DIRECTML = '1'
$env:COUCHSUBS_DIRECTML_DEVICE = '1'
./publish/CouchSubs-DirectML/CouchSubs.exe
```

The publish output needs the same native Remote Play and bundled model
assets as the normal portable Windows package. Device 1 is correct on
the tested MSI. DXGI returned Intel Graphics at index 0 and the NVIDIA
GeForce RTX 5080 Laptop GPU at index 1. Adapter numbering is machine
specific. DirectML uses sequential execution with memory patterns
disabled, as required by ONNX Runtime.

## MSI benchmark

| Workload | CPU p95 | RTX 5080 p95 |
|---|---:|---:|
| Marian INT8 encoder, 24 tokens, profiled | 13.116 ms | 6.998 ms |
| Silero VAD, profiled | 0.418 ms | 1.099 ms |
| Full Marian translation, 30 uncached runs | 531.7 ms | 515.0 ms |

ONNX Runtime profiling recorded DirectML node executions on adapter 1.
Some operators still ran on CPU. All 30 translated strings matched in
the full-path comparison. Both modes used the same optional runtime
version with the CPU thread cap set to one. This is a single local run.
The encoder gains do not extend to all models. Silero stays on CPU.
The benchmark excludes PS5, headset output, and Pocket TTS.
