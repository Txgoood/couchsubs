// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Smooth stereo ambience-preserving mix envelope. Does not attempt to isolate
/// dialogue from music/effects: the PS5 signal is a single mixed soundtrack.
/// No allocation, I/O or model inference in the render callback.
/// </summary>
public sealed class AtmosphereMixEnvelope
{
    private readonly int rate;
    private float gameGain = 1f;
    private float voiceGain;
    private long invalidInputFrames;
    private long limitedFrames;
    public AtmosphereMixEnvelope(int sampleRate = 48000)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        rate = sampleRate;
    }

    public float CurrentGameGain => gameGain;
    public float CurrentVoiceGain => voiceGain;
    /// <summary>Only diagnostic counters; never stores raw audio or spoken words.</summary>
    public long InvalidInputFrames => invalidInputFrames;
    public long LimitedFrames => limitedFrames;

    /// <summary>Process one stereo frame. KeepGame=1 disables ducking entirely.</summary>
    public (float Left, float Right) Step(
        float gameLeft, float gameRight, float speechLeft, float speechRight,
        float keepGame = 0.82f, float speechVolume = 0.85f,
        AtmosphereMixMode mode = AtmosphereMixMode.Legacy)
    {
        // Broken drivers/decoders can supply non-finite samples; never let NaN or
        // Infinity poison the render chain (which otherwise stays silent until reset).
        if (!float.IsFinite(gameLeft) || !float.IsFinite(gameRight) ||
            !float.IsFinite(speechLeft) || !float.IsFinite(speechRight))
        {
            invalidInputFrames++;
            gameLeft = float.IsFinite(gameLeft) ? gameLeft : 0f;
            gameRight = float.IsFinite(gameRight) ? gameRight : 0f;
            speechLeft = float.IsFinite(speechLeft) ? speechLeft : 0f;
            speechRight = float.IsFinite(speechRight) ? speechRight : 0f;
        }
        keepGame = Math.Clamp(float.IsFinite(keepGame) ? keepGame : 1f, 0.35f, 1f);
        speechVolume = Math.Clamp(float.IsFinite(speechVolume) ? speechVolume : 0f, 0f, 1f);
        var speaking = MathF.Max(MathF.Abs(speechLeft), MathF.Abs(speechRight)) > 0.0001f;
        // Downward gain takes ~35ms, release ~180ms to avoid pumping/abrupt ambient loss.
        var target = speaking && mode != AtmosphereMixMode.SafeOriginal ? keepGame : 1f;
        var limit = 1f / (rate * (target < gameGain ? 0.035f : 0.180f));
        gameGain += Math.Clamp(target - gameGain, -limit, limit);
        // Ramps TTS start/stop independently so sudden words don't click.
        var voiceTarget = speaking ? speechVolume : 0f;
        var voiceStep = 1f / (rate * (voiceTarget > voiceGain ? 0.012f : 0.045f));
        voiceGain += Math.Clamp(voiceTarget - voiceGain, -voiceStep, voiceStep);

        float left;
        float right;
        if (mode is AtmosphereMixMode.SpatialBalanced or AtmosphereMixMode.Cinematic)
        {
            // A game stereo mix contains a mid (shared L+R) and a side (L-R)
            // component. Reduce the shared center only; retain positional side.
            // This does NOT separate English speech or know speaker positions.
            var center = (gameLeft + gameRight) * 0.5f;
            var side = (gameLeft - gameRight) * 0.5f;
            var midGain = mode == AtmosphereMixMode.Cinematic
                ? MathF.Max(0.35f, gameGain - 0.12f * (1f - gameGain))
                : gameGain;
            var centerMixed = center * midGain;
            left = centerMixed + side + speechLeft * voiceGain;
            right = centerMixed - side + speechRight * voiceGain;
        }
        else
        {
            // Preserve the exact legacy mix and its stereo behavior.
            left = gameLeft * gameGain + speechLeft * voiceGain;
            right = gameRight * gameGain + speechRight * voiceGain;
        }
        // Gentle shared limiter avoids clipping while preserving stereo balance.
        var highest = MathF.Max(1f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
        if (highest > 1f) limitedFrames++;
        return (left / highest, right / highest);
    }

    public void Reset()
    {
        gameGain = 1f;
        voiceGain = 0f;
        invalidInputFrames = 0;
        limitedFrames = 0;
    }
}
