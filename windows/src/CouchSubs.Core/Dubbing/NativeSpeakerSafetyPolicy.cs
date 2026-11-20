// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Crash containment for the experimental Sherpa native speaker extractor.
/// Windows .NET Runtime 1026 records on Build #146 showed an unhandled native
/// access violation at SpeakerEmbeddingExtractor.get_Dim(); a managed catch
/// cannot safely recover from this failure in the parent process.
/// Until the extractor is run and health-checked in a separate process, do NOT
/// instantiate/start it inside CouchSubs.exe, even with an installed model.
/// Named subtitles and existing manually assigned casts remain available.
/// </summary>
public static class NativeSpeakerSafetyPolicy
{
    public static bool InProcessInferenceAllowed => false;
}
