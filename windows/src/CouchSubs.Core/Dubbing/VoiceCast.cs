// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace CouchSubs.Core.Dubbing;

/// <summary>Dublaj sesleri: Pocket TTS Turkish'in hazır sesleri (yapay, gerçek kişi değil).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DubbingVoice>))]
public enum DubbingVoice { Female1, Female2, Female3, Male1, Male2, Male3, Female4, Female5, Female6, Male4, Male5, Male6 }

public enum VoiceGender { Female, Male }

/// <summary>Karakter düzenleme grubu. Ses üretici sayısını veya karakter havuzunu sınırlamaz.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CharacterGroup>))]
public enum CharacterGroup { Unknown, Male, Female, Child, Creature, Narrator }

public static class DubbingVoices
{
    public static readonly DubbingVoice[] All = Enum.GetValues<DubbingVoice>();
    public static readonly DubbingVoice[] References = [DubbingVoice.Female1, DubbingVoice.Female2, DubbingVoice.Female3,
        DubbingVoice.Male1, DubbingVoice.Male2, DubbingVoice.Male3];

    // Variants reuse the installed references and gently change the generated voice.
    public static DubbingVoice Reference(this DubbingVoice voice) => voice switch
    {
        DubbingVoice.Female4 => DubbingVoice.Female1,
        DubbingVoice.Female5 => DubbingVoice.Female2,
        DubbingVoice.Female6 => DubbingVoice.Female3,
        DubbingVoice.Male4 => DubbingVoice.Male1,
        DubbingVoice.Male5 => DubbingVoice.Male2,
        DubbingVoice.Male6 => DubbingVoice.Male3,
        _ => voice,
    };

    public static double PlaybackRate(this DubbingVoice voice) => voice switch
    {
        DubbingVoice.Female4 or DubbingVoice.Male4 => 1.09,
        DubbingVoice.Female5 or DubbingVoice.Male5 => 0.91,
        DubbingVoice.Female6 or DubbingVoice.Male6 => 0.94,
        _ => 1,
    };

    /// <summary>Modeldeki dosya adı ("female_1").</summary>
    public static string FileName(this DubbingVoice voice) => voice.Reference() switch
    {
        DubbingVoice.Female1 => "female_1",
        DubbingVoice.Female2 => "female_2",
        DubbingVoice.Female3 => "female_3",
        DubbingVoice.Male1 => "male_1",
        DubbingVoice.Male2 => "male_2",
        _ => "male_3",
    };

    public static VoiceGender Gender(this DubbingVoice voice) =>
        voice.Reference() is DubbingVoice.Female1 or DubbingVoice.Female2 or DubbingVoice.Female3 ? VoiceGender.Female : VoiceGender.Male;
}

/// <summary>
/// Karakterlere ses dağıtır (Mac: VoiceCast.swift). Her konuşmacı adı kalıcı olarak bir sese bağlanır; etiketsiz
/// satırlar anlatıcı sesiyle, tireyle değişen konuşmalar iki ses arasında sırayla okunur.
/// </summary>
public sealed class VoiceCast
{
    /// <summary>Adın büyük harfli biçimi → ses. Ayarlardan değiştirilebilir.</summary>
    public Dictionary<string, DubbingVoice> Assignments { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Karakterin düzenleme grubu; modelin sesini değiştirmez, kullanıcı isterse ayarlar.</summary>
    public Dictionary<string, CharacterGroup> Groups { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Unconfirmed names heard in dialogue (NOT a mapping to whoever is speaking).</summary>
    public Dictionary<string, int> SuggestedNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DubbingVoice Narrator { get; set; } = DubbingVoice.Male1;
    /// <summary>Tireli konuşmada sıradaki kişinin sesi.</summary>
    public DubbingVoice Partner { get; set; } = DubbingVoice.Female1;
    public DubbingVoice? MaleNpcVoice { get; set; }
    public DubbingVoice? FemaleNpcVoice { get; set; }
    public DubbingVoice? UnknownNpcVoice { get; set; }
    [JsonIgnore] public bool ReserveGtaLeadVoices { get; init; }

    // Etiketsiz repliklerde sadece açık diyalog tiresi varsa dönüşümlü ses verilir.
    // İlk tireli konuşma anlatıcıdan başlar.
    private bool _lastWasPartner = true;

    public static string Key(string name) => name.Trim().ToUpperInvariant();

    public static CharacterGroup? GenericSpeakerGroup(string name)
    {
        var label = string.Concat(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark &&
                        char.IsLetter(c))).ToUpperInvariant();
        return label switch
        {
            "MAN" or "MEN" or "MALE" or "OLDMAN" or "BOY" or "MALEVOICE" => CharacterGroup.Male,
            "WOMAN" or "WOMEN" or "FEMALE" or "OLDWOMAN" or "GIRL" or "FEMALEVOICE" => CharacterGroup.Female,
            "WORKER" or "WORKERS" or "VORKER" or "WOIKERS" or "LOCAL" or "LOCALS" or "LDCAL" or
            "TOURIST" or "TOURISTS" or "STAFFMEMBER" or "STAFFMEMBERS" or "STAFFMEMBE" or
            "STAFF" or "CIVILIAN" or "CIVILIANS" or "PEDESTRIAN" or "PEDESTRIANS" or
            "CUSTOMER" or "CUSTOMERS" or "CLERK" or "SHOPKEEPER" or "CASHIER" or
            "GUNSMITH" or "GUNSTORECLERK" or "UNKNOWN" or "UNKNOWNSPEAKER" or "IN" or "INSPANISH"
                => CharacterGroup.Unknown,
            _ => null
        };
    }

    private DubbingVoice? NpcOverride(CharacterGroup group) => group switch
    {
        CharacterGroup.Male => MaleNpcVoice,
        CharacterGroup.Female => FemaleNpcVoice,
        _ => UnknownNpcVoice,
    };

    private DubbingVoice? LeadVoice(string key) => ReserveGtaLeadVoices ? key switch
    {
        "JASON" => Assignments.GetValueOrDefault("JASON", Assignments.GetValueOrDefault("JASON DUVAL", DubbingVoice.Male1)),
        "LUCIA" => Assignments.GetValueOrDefault("LUCIA", Assignments.GetValueOrDefault("LUCIA CAMINOS", DubbingVoice.Female1)),
        _ => null,
    } : null;

    private string SpeakerKey(string name) => (ReserveGtaLeadVoices ? Key(name) : "") switch
    {
        "JASON DUVAL" => "JASON",
        "LUCIA CAMINOS" => "LUCIA",
        _ => Key(name),
    };

    private DubbingVoice ChooseVoice(CharacterGroup group, bool balance = true)
    {
        var reserved = ReserveGtaLeadVoices
            ? new[] { LeadVoice("JASON")!.Value.Reference(), LeadVoice("LUCIA")!.Value.Reference() }
            : new[] { Narrator.Reference(), Partner.Reference() };
        // Two primary voices may be manually set to the same gender; that still leaves alternatives.
        var ordered = DubbingVoices.All.Where(voice => group switch
        {
            CharacterGroup.Male => voice.Gender() == VoiceGender.Male,
            CharacterGroup.Female or CharacterGroup.Child => voice.Gender() == VoiceGender.Female,
            _ => true,
        });
        var available = ordered.Where(voice => !reserved.Contains(voice.Reference()));
        return balance ? available.OrderBy(voice => Assignments.Values.Count(used => used == voice)).First() : available.First();
    }

    public DubbingVoice NpcVoice(CharacterGroup group) => NpcOverride(group) ?? ChooseVoice(group, balance: false);

    /// <summary>
    /// Persistent character voices use their group and the least-used unreserved profile.
    /// </summary>
    public DubbingVoice VoiceFor(SpokenLine line, out bool assigned)
    {
        assigned = false;
        if (!string.IsNullOrWhiteSpace(line.Speaker))
        {
            var group = GenericSpeakerGroup(line.Speaker);
            var key = SpeakerKey(line.Speaker);
            // Labels like WOMAN 1 and WOMAN 2 are distinct. A bare WOMAN label
            // cannot identify a different person in each scene.
            if (group is { } generic && !key.Any(char.IsDigit)) return NpcVoice(generic);
            if (Assignments.TryGetValue(key, out var known)) return known;
            var voice = LeadVoice(key) ?? ChooseVoice(group ?? GroupFor(key));
            Assignments[key] = voice;
            assigned = true;
            return voice;
        }
        if (line.NewTurn) _lastWasPartner = !_lastWasPartner;
        return _lastWasPartner ? Partner : Narrator;
    }

    /// <summary>Collect a dialogue clue; never assign it to the current speaker.</summary>
    public bool SuggestName(string name)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 32 || SuggestedNames.Count > 250 || GenericSpeakerGroup(name) is not null)
            return false;
        var key = SpeakerKey(name);
        if (Assignments.ContainsKey(key) || SuggestedNames.GetValueOrDefault(key) >= 3) return false;
        SuggestedNames[key] = SuggestedNames.GetValueOrDefault(key) + 1;
        return true;
    }

    public void DismissSuggestion(string name) => SuggestedNames.Remove(SpeakerKey(name));

    /// <summary>Manually correct a character name without changing its chosen Turkish voice.</summary>
    public void Rename(string oldName, string newName)
    {
        var oldKey = SpeakerKey(oldName);
        var newKey = SpeakerKey(newName);
        if (newKey == oldKey && Assignments.ContainsKey(oldKey)) return;
        if (newKey.Length is 0 or > 64 ||
            !Assignments.TryGetValue(oldKey, out var voice))
            throw new InvalidOperationException("Geçersiz karakter adı veya kaynak bulunamadı.");
        if (Assignments.ContainsKey(newKey))
            throw new InvalidOperationException("Bu karakter adı zaten kayıtlı; yanlışlıkla iki sesi birleştirmemek için önce kontrol et.");
        Assignments.Remove(oldKey);
        Assignments[newKey] = voice;
        if (Groups.Remove(oldKey, out var group)) Groups[newKey] = group;
        SuggestedNames.Remove(newKey);
        SuggestedNames.Remove(oldKey);
    }

    public void AcceptSuggestion(string name, CharacterGroup group = CharacterGroup.Unknown)
    {
        if (!SuggestedNames.Remove(SpeakerKey(name))) return;
        SetGroup(name, group == CharacterGroup.Unknown ? GroupFor(name) : group);
    }

    public void Assign(DubbingVoice voice, string name) => Assignments[SpeakerKey(name)] = voice;

    public CharacterGroup GroupFor(string name)
    {
        var key = SpeakerKey(name);
        if (Groups.TryGetValue(key, out var group)) return group;
        return NameGender.Guess(name) switch
        {
            VoiceGender.Male => CharacterGroup.Male,
            VoiceGender.Female => CharacterGroup.Female,
            _ => CharacterGroup.Unknown,
        };
    }

    public void SetGroup(string name, CharacterGroup group)
    {
        var key = SpeakerKey(name);
        Groups[key] = group;
        if (!Assignments.ContainsKey(key)) Assignments[key] = LeadVoice(key) ?? ChooseVoice(group);
    }

    public void ReassignAutomatically(string name)
    {
        var key = SpeakerKey(name);
        Assignments.Remove(key);
        if (ReserveGtaLeadVoices && key == "JASON") Assignments.Remove("JASON DUVAL");
        if (ReserveGtaLeadVoices && key == "LUCIA") Assignments.Remove("LUCIA CAMINOS");
        VoiceFor(new SpokenLine("", "", key), out _);
    }

    public void Remove(string name)
    {
        var key = SpeakerKey(name);
        Assignments.Remove(key);
        Groups.Remove(key);
        SuggestedNames.Remove(key);
    }

    public VoiceCast Copy(bool? reserveGtaLeadVoices = null) => new()
    {
        Assignments = new Dictionary<string, DubbingVoice>(Assignments, StringComparer.Ordinal),
        Groups = new Dictionary<string, CharacterGroup>(Groups, StringComparer.Ordinal),
        SuggestedNames = new Dictionary<string, int>(SuggestedNames, StringComparer.OrdinalIgnoreCase),
        Narrator = Narrator,
        Partner = Partner,
        MaleNpcVoice = MaleNpcVoice,
        FemaleNpcVoice = FemaleNpcVoice,
        UnknownNpcVoice = UnknownNpcVoice,
        ReserveGtaLeadVoices = reserveGtaLeadVoices ?? ReserveGtaLeadVoices,
    };
}

/// <summary>Addan cinsiyet tahmini: oyun karakterleri ve yaygın İngilizce adlar (Mac ile aynı listeler).</summary>
public static class NameGender
{
    private static readonly HashSet<string> Female = new(StringComparer.Ordinal)
    {
        "lucia", "amanda", "tracey", "tanisha", "denise", "patricia", "paige", "maude", "molly", "taliana",
        "freya", "faye", "angrboda", "sif", "thrud", "ellie", "abby", "dina", "tess", "marlene", "riley", "aloy", "elisabet",
        "beta", "mary", "jane", "mj", "may", "rio", "hailey", "lara", "jill", "claire", "ada", "ashley", "tifa", "aerith",
        "yuffie", "jessie", "yuna", "lulu", "rikku", "ciri", "yennefer", "triss", "kassandra", "aya", "evie", "lydia",
        "anna", "emma", "olivia", "sophia", "isabella", "mia", "amelia", "harper", "evelyn", "abigail", "emily", "ella",
        "elizabeth", "sofia", "avery", "scarlett", "grace", "chloe", "victoria", "lily", "hannah", "zoe", "nora", "sarah",
        "rachel", "rebecca", "laura", "julia", "maria", "kate", "katie", "lucy", "alice", "eve", "helen", "linda", "susan",
        "karen", "nancy", "lisa", "betty", "margaret", "sandra", "kim", "mother", "mom", "mum", "queen", "princess", "lady",
        "girl", "woman", "sister", "aunt", "grandma", "witch", "goddess", "daughter", "wife",
    };

    private static readonly HashSet<string> Male = new(StringComparer.Ordinal)
    {
        "michael", "franklin", "trevor", "lamar", "lester", "jimmy", "ron", "wade", "devin", "solomon", "martin",
        "brad", "dave", "jason", "niko", "roman", "luis", "johnny", "chop",
        "kratos", "atreus", "mimir", "baldur", "thor", "odin", "brok", "sindri", "tyr", "heimdall", "freyr", "durlin", "joel",
        "tommy", "bill", "henry", "jesse", "lev", "isaac", "owen", "manny", "jerry", "rost", "erend", "varl", "sylens",
        "kotallo", "peter", "miles", "harry", "otto", "kraven", "venom", "norman", "aaron", "ganke", "leon", "chris",
        "albert", "wesker", "ethan", "cloud", "barret", "zack", "sephiroth", "noctis", "tidus", "auron", "geralt",
        "jaskier", "alexios", "bayek", "ezio", "altair", "connor", "edward", "arthur", "dutch", "john", "micah", "hosea",
        "nathan", "nate", "drake", "sully", "jin", "khotun", "deacon", "boozer", "james", "robert", "michael", "william",
        "david", "richard", "joseph", "thomas", "charles", "daniel", "matthew", "anthony", "mark", "paul", "steven",
        "andrew", "kenneth", "kevin", "brian", "george", "jack", "jacob", "luke", "max", "father", "dad", "boy", "king",
        "prince", "lord", "sir", "man", "brother", "uncle", "grandpa", "son", "husband", "soldier", "guard", "captain",
        "commander", "general",
    };

    public static VoiceGender? Guess(string name)
    {
        var lower = name.ToLowerInvariant();
        if (Female.Contains(lower)) return VoiceGender.Female;
        if (Male.Contains(lower)) return VoiceGender.Male;
        // Çok sözcüklü adda sözcüklere bakılır ("OLD MAN", "Mary Jane").
        foreach (var word in lower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Female.Contains(word)) return VoiceGender.Female;
            if (Male.Contains(word)) return VoiceGender.Male;
        }
        return null;
    }
}
