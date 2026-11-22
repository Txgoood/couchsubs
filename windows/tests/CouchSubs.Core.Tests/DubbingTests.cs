// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public class SpeechTextTests
{
    [Fact]
    public void EmotionComesFromTheEnglishLine()
    {
        Assert.Equal(SpeechEmotion.Angry, SpeechEmotions.Guess("Get back! This is not your fight."));
        Assert.Equal(SpeechEmotion.Surprised, SpeechEmotions.Guess("What?! The door opened by itself!"));
        Assert.Equal(SpeechEmotion.Sad, SpeechEmotions.Guess("I'm sorry. I couldn't save her."));
        Assert.Equal(SpeechEmotion.Happy, SpeechEmotions.Guess("We did it! We're going home."));
        Assert.Equal(SpeechEmotion.Calm, SpeechEmotions.Guess("Breathe, boy. Take your time."));
        Assert.Null(SpeechEmotions.Guess("We need to cross the river."));
    }

    [Fact]
    public void EverySentenceStartsLowercaseWithTheTagFirst()
    {
        Assert.Equal("[kızgın] geri çekil!", SpeechText.Prepare("Geri çekil!", SpeechEmotion.Angry));
        Assert.Equal("ileri gidelim. hemen...", SpeechText.Prepare("İleri gidelim.  Hemen…"));
        Assert.Equal("ışığı kapat.", SpeechText.Prepare("Işığı kapat."));
        Assert.Equal("ne? kapı açıldı!", SpeechText.Prepare("Ne?! Kapı açıldı!!"));
    }

    [Fact]
    public void NumbersAreSpelledOut()
    {
        Assert.Equal("bin iki yüz elli", TurkishNumbers.Words(1250));
        Assert.Equal("iki milyon yüz beş", TurkishNumbers.Words(2_000_105));
        Assert.Equal("yüz", TurkishNumbers.Words(100));
        Assert.Equal("üç kapı, yüzde yirmi şans, saat on dört otuz, üç virgül beş metre, bin beş yüz altın",
            TurkishNumbers.SpellOut("3 kapı, %20 şans, saat 14:30, 3,5 metre, 1.500 altın"));
        Assert.Equal("dokuz sıfır beş", TurkishNumbers.SpellOut("09:05"));
    }
}

public class VoiceCastTests
{
    [Fact]
    public void GenericSpeakerAliasesUseSharedVoicesWithoutAddingCharacters()
    {
        var cast = new VoiceCast { UnknownNpcVoice = DubbingVoice.Male3 };
        cast.Assign(DubbingVoice.Female3, "Jason");
        cast.Assign(DubbingVoice.Female2, "Worker");
        foreach (var name in new[] { "Worker", "Vorker", "Woikers", "Ldcal", "Töu'rists", "Staff-member", "In Spanish" })
        {
            Assert.Equal(DubbingVoice.Male3, cast.VoiceFor(new SpokenLine("Hello.", "Merhaba.", name), out var assigned));
            Assert.False(assigned);
        }
        Assert.Equal(DubbingVoice.Female3, cast.VoiceFor(new SpokenLine("Hello.", "Merhaba.", "Jason"), out _));
        Assert.Equal(DubbingVoice.Female1, cast.VoiceFor(new SpokenLine("Hello.", "Merhaba.", "Woman"), out _));
        Assert.Equal(2, cast.Assignments.Count);
        Assert.Equal(DubbingVoice.Male3, cast.Copy().UnknownNpcVoice);
    }
    [Fact]
    public void EachCharacterKeepsItsVoice()
    {
        var cast = new VoiceCast();
        var kratos = cast.VoiceFor(new SpokenLine("", "", "KRATOS"), out var assigned);
        Assert.True(assigned);
        var freya = cast.VoiceFor(new SpokenLine("", "", "Freya"), out _);
        var atreus = cast.VoiceFor(new SpokenLine("", "", "ATREUS"), out _);
        Assert.Equal(VoiceGender.Male, kratos.Gender());
        Assert.Equal(VoiceGender.Female, freya.Gender());
        Assert.Equal(VoiceGender.Male, atreus.Gender());
        Assert.Equal(cast.Narrator, kratos);
        Assert.Equal(cast.Narrator, atreus);
        Assert.Equal(cast.Partner, freya);
        Assert.Equal(kratos, cast.VoiceFor(new SpokenLine("", "", "Kratos"), out assigned));
        Assert.False(assigned);
    }

    [Fact]
    public void TwoDistinctUnknownSpeakersGetTwoPersistentVoices()
    {
        var cast = new VoiceCast();
        Assert.Equal(cast.Narrator, cast.VoiceFor(new SpokenLine("", "", "MYSTERY ONE"), out _));
        Assert.Equal(cast.Partner, cast.VoiceFor(new SpokenLine("", "", "MYSTERY TWO"), out _));
        Assert.Equal(cast.Narrator, cast.VoiceFor(new SpokenLine("", "", "MYSTERY ONE"), out _));
        var saved = JsonSerializer.Deserialize<VoiceCast>(JsonSerializer.Serialize(cast))!;
        Assert.Equal(saved.Partner, saved.VoiceFor(new SpokenLine("", "", "MYSTERY TWO"), out _));
    }

    [Theory]
    [InlineData("KRATOS: Leave now!", "KRATOS: Hemen git!", "KRATOS", "Hemen git!")]
    [InlineData("FREYA: Stop.", "Dur.", "FREYA", "Dur.")]
    public void NamedDialogueKeepsSpeakerAndStripsLabels(string source, string translated, string speaker, string spoken)
    {
        var line = Assert.Single(SubtitleDialogue.Parse(source, translated));
        Assert.Equal(speaker, line.Speaker);
        Assert.Equal(spoken, line.Turkish);
    }

    [Theory]
    [InlineData("Michael: We need a car.", "Bir arabaya ihtiyacımız var.", "Michael")]
    [InlineData("Franklin: Come on!", "Hadi!", "Franklin")]
    [InlineData("Trevor: Get out!", "Çık dışarı!", "Trevor")]
    [InlineData("Lucia: Let's go.", "Gidelim.", "Lucia")]
    [InlineData("Jason: Wait.", "Bekle.", "Jason")]
    public void GtaSpeakerNamesAreRecognizedCaseInsensitively(string source, string translated, string expected)
    {
        var line = Assert.Single(SubtitleDialogue.Parse(source, translated));
        Assert.Equal(expected, line.Speaker);
        Assert.Equal(translated, line.Turkish);
    }

    [Fact]
    public void TwoDashedLinesUseBothVoices()
    {
        var lines = SubtitleDialogue.Parse("- Where are you?\n- Here.", "- Neredesin?\n- Buradayım.");
        Assert.Equal(2, lines.Count);
        var cast = new VoiceCast();
        Assert.Equal(cast.Narrator, cast.VoiceFor(lines[0], out _));
        Assert.Equal(cast.Partner, cast.VoiceFor(lines[1], out _));
    }

    [Fact]
    public void UnlabeledTextDoesNotGuessSpeaker()
    {
        var line = Assert.Single(SubtitleDialogue.Parse("We have to leave.", "Gitmemiz gerekiyor."));
        Assert.Null(line.Speaker);
        Assert.False(line.NewTurn);
    }

    [Fact]
    public void DashedTurnsAlternateVoices()
    {
        var cast = new VoiceCast();
        var first = cast.VoiceFor(new SpokenLine("", "", NewTurn: true), out _);
        var second = cast.VoiceFor(new SpokenLine("", "", NewTurn: true), out _);
        Assert.NotEqual(first, second);
        Assert.Equal(second, cast.VoiceFor(new SpokenLine("", ""), out _));
    }

    [Fact]
    public void UnlimitedCharactersReuseExistingVoiceFilesWithoutNewTtsModels()
    {
        var cast = new VoiceCast();
        for (var i = 0; i < 500; i++)
        {
            var name = $"GTA NPC {i:000}";
            cast.SetGroup(name, i % 3 == 0 ? CharacterGroup.Male :
                i % 3 == 1 ? CharacterGroup.Female : CharacterGroup.Creature);
            cast.Assign(DubbingVoices.All[i % DubbingVoices.All.Length], name);
        }
        var restored = JsonSerializer.Deserialize<VoiceCast>(JsonSerializer.Serialize(cast))!;
        Assert.Equal(500, restored.Assignments.Count);
        Assert.Equal(500, restored.Groups.Count);
        Assert.Equal(CharacterGroup.Creature, restored.GroupFor("GTA NPC 002"));
        Assert.Equal(cast.Assignments["GTA NPC 499"], restored.Assignments["GTA NPC 499"]);
        Assert.Equal(6, restored.Assignments.Values.Distinct().Count());
    }

    [Fact]
    public void NewGroupDoesNotReplaceAssignedCharacterVoice()
    {
        var cast = new VoiceCast();
        cast.Assign(DubbingVoice.Male3, "Trevor");
        cast.SetGroup("Trevor", CharacterGroup.Male);
        Assert.Equal(DubbingVoice.Male3, cast.VoiceFor(new SpokenLine("", "", "TREVOR"), out _));
        Assert.Equal(CharacterGroup.Male, cast.GroupFor("trevor"));
        cast.Remove("Trevor");
        Assert.Empty(cast.Assignments);
        Assert.Empty(cast.Groups);
    }

    [Fact]
    public void ManualChoiceIsSaved()
    {
        var cast = new VoiceCast();
        cast.Assign(DubbingVoice.Female3, "Mimir");
        var restored = JsonSerializer.Deserialize<VoiceCast>(JsonSerializer.Serialize(cast))!;
        Assert.Equal(DubbingVoice.Female3, restored.VoiceFor(new SpokenLine("", "", "MIMIR"), out _));
        Assert.Contains("\"Female3\"", JsonSerializer.Serialize(cast));
    }
}

public class SpeechAudioTests
{
    private static float[] Tone(double seconds) =>
        Enumerable.Range(0, (int)(seconds * 24000)).Select(i => MathF.Sin(i * 0.1f) * 0.5f).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * 24000)];

    [Fact]
    public void SmoothEdgesAvoidHardStartAndStopWithoutChangingLength()
    {
        var source = new SpeechAudio(Enumerable.Repeat(0.8f, 2400).ToArray(), 24000);
        var softened = source.SoftEdges();
        Assert.Equal(source.Samples.Length, softened.Samples.Length);
        Assert.Equal(0f, softened.Samples[0]);
        Assert.Equal(0f, softened.Samples[^1]);
        Assert.InRange(softened.Samples[100], 0.01f, 0.8f);
        Assert.Equal(0.8f, softened.Samples[1200], 5);
        Assert.Equal(0.8f, source.Samples[0]); // Source is unchanged.
    }

    [Fact]
    public void SmoothEdgesHandleVeryShortSpeech()
    {
        var audio = new SpeechAudio([0.6f, 0.4f, 0.3f], 24000);
        var softened = audio.SoftEdges();
        Assert.Equal(3, softened.Samples.Length);
        Assert.Equal(0f, softened.Samples[0]);
    }

    [Fact]
    public void LongPausesAreShortenedTo200Ms()
    {
        var audio = new SpeechAudio([.. Silence(0.2), .. Tone(0.3), .. Silence(0.4), .. Tone(0.3), .. Silence(0.5)], 24000);
        Assert.Equal(0.91, audio.Tightened().Duration, 0.03);
    }

    [Fact]
    public void ShortPausesStay()
    {
        var audio = new SpeechAudio([.. Tone(0.3), .. Silence(0.1), .. Tone(0.3)], 24000);
        Assert.Equal(audio.Duration, audio.Tightened(lead: 0, tail: 0).Duration, 0.02);
    }

    [Fact]
    public void WavRoundTripAndStreamedSize()
    {
        var wav = new SpeechAudio([0f, 0.5f, -0.5f, 0.25f], 24000).ToWav();
        var decoded = SpeechAudio.FromWav(wav);
        Assert.Equal(24000, decoded.SampleRate);
        Assert.Equal(4, decoded.Samples.Length);
        Assert.Equal(0.5f, decoded.Samples[1], 0.001f);
        wav[40] = wav[41] = wav[42] = wav[43] = 0xFF;
        Assert.Equal(4, SpeechAudio.FromWav(wav).Samples.Length);
    }
}

public class DubbingModelTests
{
    [Fact]
    public void UnicodeWindowsProfileRequiresAsciiRuntimePath()
    {
        Assert.False(DubbingModel.IsAsciiPath(@"C:\Users\Msı_HX\AppData\Local\CouchSubs"));
        Assert.True(DubbingModel.IsAsciiPath(@"C:\Users\Public\CouchSubs\tts-runtime"));
    }

    [Fact]
    public void LocalConfigPointsAtDownloadedFiles()
    {
        const string original = "weights_path: hf://wite-tech/pocket-tts-turkish/model.safetensors\r\nflow_lm:\r\n  lookup_table:\r\n" +
                                "    tokenizer_path: hf://wite-tech/pocket-tts-turkish/tokenizer.model\r\n";
        var directory = Path.Combine("C:", "Users", "Ali's PC", "dubbing");
        var local = DubbingModel.LocalConfig(original, directory);
        Assert.Contains("weights_path: '" + Path.Combine(directory, "model.safetensors").Replace("'", "''") + "'", local);
        Assert.Contains("    tokenizer_path: '", local);
        Assert.EndsWith("capitalize_first_letter: false\n", local);
    }
}

public class PocketTtsExitTests
{
    [Fact]
    public void ExitMessageShowsTheLastErrorLine()
    {
        var message = PocketTtsServer.ExitMessage(1, new Queue<string>(["loading model", "Error: model.safetensors not found"]));
        Assert.Equal("pocket-tts kapandı (çıkış kodu 1): Error: model.safetensors not found", message);
    }

    [Fact]
    public void ExitMessageWithoutOutputKeepsTheExitCode()
    {
        Assert.Equal("pocket-tts kapandı (çıkış kodu 3)", PocketTtsServer.ExitMessage(3, new Queue<string>()));
    }
}
