// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CouchSubs.Translation;

/// <summary>
/// Optional ONNX dialogue-stem model adapter; never instantiated on PS5 audio
/// callbacks. An explicit model contract is required: float32 [1,2,N]
/// input "audio", float32 [1,2,N] output "dialogue".
/// No model is downloaded by this adapter and no music suppression is guessed.
/// </summary>
public sealed class OnnxDialogueStemAdapter : IDisposable
{
    private readonly InferenceSession session;
    public const string ModelVariable = "COUCHSUBS_DIALOGUE_STEM_MODEL";
    public const string OptInVariable = "COUCHSUBS_ENABLE_DIALOGUE_SEPARATOR";
    public const string InputName = "audio";
    public const string OutputName = "dialogue";
    public const int Channels = 2;
    public const int SampleRate = 48000;
    // Whole 10ms stereo frames only: avoids accidentally feeding a model
    // with an undocumented sample rate or variable-length tensor.
    public const int SamplesPerChannel = 480; 

    public static bool IsOptedIn =>
        Environment.GetEnvironmentVariable(OptInVariable) == "1";

    public static string? SelectedModelPath
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ModelVariable);
            return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) &&
                   File.Exists(value) ? value : null;
        }
    }

    public OnnxDialogueStemAdapter(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException("An explicit local ONNX model file is required.", path);
        using var options = new SessionOptions
        {
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = 1
        };
        // Native inference stays opt-in and must be started from a worker,
        // not the UI/audio callback. The default Windows ONNX package is CPU.
        session = new InferenceSession(path, options);
        try
        {
            if (!session.InputMetadata.TryGetValue(InputName, out var input) ||
                !session.OutputMetadata.TryGetValue(OutputName, out var output) ||
                input.ElementType != typeof(float) || output.ElementType != typeof(float) ||
                input.Dimensions.Length != 3 || output.Dimensions.Length != 3 ||
                input.Dimensions[0] != 1 || input.Dimensions[1] != Channels ||
                output.Dimensions[0] != 1 || output.Dimensions[1] != Channels ||
                (input.Dimensions[2] > 0 && input.Dimensions[2] != SamplesPerChannel) ||
                (output.Dimensions[2] > 0 && output.Dimensions[2] != SamplesPerChannel))
                throw new InvalidDataException("Unsupported dialogue-stem ONNX tensor contract.");
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs one 10ms frame on the caller's BACKGROUND worker.
    /// Returns false on invalid/stale output; original game audio must be used.
    /// Stereo interleaved PCM stays in memory and is never written to disk.
    /// This is a model integration contract, not a bundled separator model.
    /// </summary>
    public bool TrySeparate(ReadOnlySpan<float> interleaved, Span<float> dialogueStem,
        int maxLatencyMilliseconds = 90)
    {
        if (interleaved.Length != SamplesPerChannel * Channels ||
            dialogueStem.Length != interleaved.Length || maxLatencyMilliseconds is < 1 or > 1000)
            return false;
        if (interleaved.ContainsAnyExceptFinite()) return false;
        var planar = new float[interleaved.Length];
        for (var frame = 0; frame < SamplesPerChannel; frame++)
        {
            planar[frame] = interleaved[frame * 2];
            planar[SamplesPerChannel + frame] = interleaved[frame * 2 + 1];
        }
        var sw = Stopwatch.StartNew();
        try
        {
            using var input = NamedOnnxValue.CreateFromTensor(InputName,
                new DenseTensor<float>(planar, [1, Channels, SamplesPerChannel]));
            using var outputs = session.Run([input], [OutputName]);
            if (sw.ElapsedMilliseconds > maxLatencyMilliseconds || outputs.Count != 1)
                return false;
            var tensor = outputs[0].AsTensor<float>();
            if (tensor.Length != planar.Length || tensor.Rank != 3 ||
                tensor.Dimensions[0] != 1 || tensor.Dimensions[1] != Channels ||
                tensor.Dimensions[2] != SamplesPerChannel)
                return false;
            for (var frame = 0; frame < SamplesPerChannel; frame++)
            {
                var left = tensor[0, 0, frame];
                var right = tensor[0, 1, frame];
                if (!float.IsFinite(left) || !float.IsFinite(right) ||
                    MathF.Abs(left) > 1f || MathF.Abs(right) > 1f)
                    return false;
            }
            for (var frame = 0; frame < SamplesPerChannel; frame++)
            {
                dialogueStem[frame * 2] = tensor[0, 0, frame];
                dialogueStem[frame * 2 + 1] = tensor[0, 1, frame];
            }
            return true;
        }
        catch (OnnxRuntimeException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public void Dispose() => session.Dispose();
}

internal static class FiniteAudio
{
    public static bool ContainsAnyExceptFinite(this ReadOnlySpan<float> values)
    {
        foreach (var sample in values)
            if (!float.IsFinite(sample) || MathF.Abs(sample) > 1f) return true;
        return false;
    }
}
