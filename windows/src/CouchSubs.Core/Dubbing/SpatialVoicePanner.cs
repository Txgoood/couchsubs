// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Conservative per-line spatial placement of *generated Turkish audio*.
/// Pan is accepted only from explicit, sufficiently confident positional
/// metadata; a stereo game soundtrack or speaker identity alone cannot tell
/// which side a character occupies. Missing/uncertain information stays center.
/// No gameplay sound channels are modified by this helper.
/// </summary>
public static class SpatialVoicePanner
{
    public const float MinimumConfidence = 0.90f;
    public const float MaximumPan = 0.65f;

    public static (float Left, float Right) Gains(float estimatedPan, float confidence)
    {
        if (!float.IsFinite(estimatedPan) || !float.IsFinite(confidence) ||
            confidence < MinimumConfidence || estimatedPan is < -1f or > 1f)
            return (1f, 1f);

        // Keep a strong presence in both channels to avoid unnatural hard-panning.
        var pan = Math.Clamp(estimatedPan, -MaximumPan, MaximumPan);
        return (pan > 0f ? 1f - pan * 0.5f : 1f,
                pan < 0f ? 1f + pan * 0.5f : 1f);
    }
}
