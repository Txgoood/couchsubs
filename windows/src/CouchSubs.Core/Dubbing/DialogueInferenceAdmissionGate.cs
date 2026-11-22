// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Conservative runtime model-admission gate for 48kHz game audio.
/// GPU availability or successful ONNX/PyTorch load is NOT sufficient.
/// Require measured sustained throughput and 90ms end-to-end age budget.
/// Any overload closes the gate; original PCM must remain unchanged.
/// </summary>
public sealed class DialogueInferenceAdmissionGate
{
    public const int FrameMilliseconds = 20;
    public const int MaxAgeMilliseconds = 90;
    public const int MinimumFrames = 64;
    private readonly Queue<double> samples = new();
    private readonly int algorithmicMs;
    private readonly int transportMs;
    private readonly int renderingMs;
    private long faults;
    public DialogueInferenceAdmissionGate(int algorithmicMs = 63,
        int transportMs = 5, int renderingMs = 5)
    {
        if (algorithmicMs < 0 || transportMs < 0 || renderingMs < 0)
            throw new ArgumentOutOfRangeException(nameof(algorithmicMs));
        this.algorithmicMs = algorithmicMs;
        this.transportMs = transportMs;
        this.renderingMs = renderingMs;
    }

    public bool Eligible { get; private set; }
    public string Status { get; private set; } = "Model warming up";
    public int MeasuredFrames => samples.Count;
    public long Faults => Interlocked.Read(ref faults);

    /// <summary>Worker-side measurement, NEVER called from an audio callback.</summary>
    public void Observe(double inferenceMs, bool producedValidPcm)
    {
        if (!producedValidPcm || !double.IsFinite(inferenceMs) || inferenceMs < 0)
        {
            Interlocked.Increment(ref faults);
            Eligible = false;
            Status = "Invalid model output or inference timing";
            samples.Clear();
            return;
        }
        if (samples.Count == 128) samples.Dequeue();
        samples.Enqueue(inferenceMs);
        if (samples.Count < MinimumFrames)
        {
            Eligible = false;
            Status = "Insufficient measured model frames";
            return;
        }

        var sorted = samples.OrderBy(v => v).ToArray();
        var p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
        var mean = samples.Average();
        // A model that averages > 20ms/20ms frame cannot keep up indefinitely,
        // even when individual results are younger than the 90ms deadline.
        Eligible = mean < FrameMilliseconds * .75 &&
                   algorithmicMs + transportMs + renderingMs + p95 <
                   MaxAgeMilliseconds;
        Status = Eligible ? "Throughput and latency budget eligible for device trial" :
            $"Model unsafe for live game sound: mean {mean:F1}ms, p95 {p95:F1}ms";
    }

    public void Reset()
    {
        samples.Clear();
        Eligible = false;
        Status = "Model warming up";
        Interlocked.Exchange(ref faults, 0);
    }
}
