// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Atmosphere-preserving algorithms on already mixed stereo PS5 audio.
/// None of these profiles perform English dialogue source separation or
/// infer the position of individual voices from game audio.
/// </summary>
public enum AtmosphereMixMode
{
    /// <summary>Keep the prior Build #137 all-channel ducking behavior.</summary>
    Legacy = 0,
    /// <summary>Leave original stereo game mix at full level and add Turkish voice.</summary>
    SafeOriginal = 1,
    /// <summary>Duck mainly the shared center (mid); preserve directional side (L-R).</summary>
    SpatialBalanced = 2,
    /// <summary>More aggressive center-only ducking; stereo side is preserved.</summary>
    Cinematic = 3,
    /// <summary>Conservative signal-level adaptive center reduction while Turkish speech plays.</summary>
    AdaptiveDialogue = 4
}
