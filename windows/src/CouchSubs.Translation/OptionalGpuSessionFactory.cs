// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;
using Microsoft.ML.OnnxRuntime;

namespace CouchSubs.Translation;

/// <summary>
/// Opt-in DirectML execution provider negotiation for a separately packaged
/// compatible ONNX Runtime binary. A failure must never prevent CPU fallback.
/// This object is ONLY used in a supervised/background model worker, never
/// in the PS5 capture callback or on the UI thread.
/// </summary>
public static class OptionalGpuSessionFactory
{
    public const string OptInVariable = "COUCHSUBS_ENABLE_DIRECTML";
    public const string DeviceIdVariable = "COUCHSUBS_DIRECTML_DEVICE";
    public const int MaxDeviceId = 8;

    public static bool IsOptedIn(string? flag) =>
        string.Equals(flag, "1", StringComparison.Ordinal);

    public static int ParseDeviceId(string? value) =>
        int.TryParse(value, out var deviceId) && deviceId >= 0 && deviceId <= MaxDeviceId
            ? deviceId : 0;

    public static (InferenceSession Session, bool GpuActive, string Status) Open(
        string modelPath, bool gpuOptIn, int deviceId = 0)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("ONNX model not found.", modelPath);
        if (deviceId is < 0 or > MaxDeviceId)
            deviceId = 0;

        if (gpuOptIn && TryOpenDirectMl(modelPath, deviceId, out var gpuSession))
            return (gpuSession!, true, "DirectML GPU provider initialized");
        using var cpuOptions = NewOptions();
        return (new InferenceSession(modelPath, cpuOptions), false,
            gpuOptIn ? "DirectML unavailable; safe CPU fallback" :
                       "CPU default (DirectML not requested)");
    }

    internal static bool TryOpenDirectMl(string modelPath, int deviceId, out InferenceSession? session)
    {
        session = null;
        try
        {
            using var gpuOptions = NewOptions();
            gpuOptions.EnableMemoryPattern = false;
            var enable = typeof(SessionOptions).GetMethod(
                "AppendExecutionProvider_DML",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null, types: [typeof(int)], modifiers: null);
            if (enable is not null)
            {
                enable.Invoke(gpuOptions, [deviceId]);
                session = new InferenceSession(modelPath, gpuOptions);
            }
        }
        catch (Exception error) when (error is TargetInvocationException or
                                      OnnxRuntimeException or DllNotFoundException or
                                      EntryPointNotFoundException or BadImageFormatException or
                                      InvalidOperationException)
        {
            // The caller can open a fresh CPU session with its own options.
        }
        return session is not null;
    }

    private static SessionOptions NewOptions() => new()
    {
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        IntraOpNumThreads = 1
    };
}
