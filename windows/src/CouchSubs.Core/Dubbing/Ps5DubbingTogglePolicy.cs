// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Decide PS5 Remote Play stream capture at session creation time.
/// The dubbing checkbox can be toggled without reconnecting PS5, so audio
/// capture must be available while the Headset output is selected even when
/// Turkish dubbing starts disabled. A checked checkbox controls Turkish TTS,
/// not whether the Remote Play stream contains audio.
/// </summary>
public static class Ps5DubbingTogglePolicy
{
    public static bool RequiresAudioCapture(
        bool headsetSelected, bool mixerActive, bool acousticMode, bool neuralMode) =>
        headsetSelected || mixerActive || acousticMode || neuralMode;
}
