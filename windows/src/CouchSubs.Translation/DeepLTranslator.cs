// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Translation;

/// <summary>
/// İsteğe bağlı çevrimiçi çeviri: kullanıcının kendi DeepL anahtarıyla. Altyazı cümle cümle, konuşmacı adları
/// çevrilmeden gönderilir; önceki altyazı bağlam olarak eklenir (ücretlendirilmez, "o"nun kim olduğunu bilir).
/// DeepL'e ulaşılamazsa ya da kota biterse o satır cihaz üstü modelle çevrilir; oyun hiç altyazısız kalmaz.
/// </summary>
public sealed class DeepLTranslator : ITextTranslator
{
    private const int CacheLimit = 4096;
    private static readonly TimeSpan LiveRequestBudget = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan LocalHeadStart = TimeSpan.FromMilliseconds(300);

    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(4) });

    private readonly string _key;
    private readonly ITextTranslator? _fallback;
    private readonly HttpClient _http;
    private readonly DeepLAvailability _availability;
    private readonly TimeSpan _requestBudget;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<SpokenLine>> _speech = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _names = new(StringComparer.Ordinal);
    private string? _previous;

    public DeepLTranslator(string key, ITextTranslator? fallback, HttpClient? http = null,
        DeepLAvailability? availability = null, TimeSpan? requestBudget = null)
    {
        _key = key.Trim();
        _fallback = fallback;
        _http = http ?? SharedClient.Value;
        _availability = availability ?? new DeepLAvailability();
        _requestBudget = requestBudget ?? LiveRequestBudget;
    }

    /// <summary>Son DeepL hatası; çeviri düzgün gidiyorsa null.</summary>
    public OnlineKeyState? LastError => _availability.LastError;

    /// <summary>Ücretsiz anahtarlar ":fx" ile biter ve ayrı adrese gider.</summary>
    internal static Uri BaseUri(string key) =>
        new(key.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com/v2/" : "https://api.deepl.com/v2/");

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(text, out var cached)) return cached;
        if (!_availability.UseOnline(out var checkQuota))
        {
            if (checkQuota) _ = ProbeQuotaAsync();
            return await FallbackAsync(text, cancellationToken).ConfigureAwait(false);
        }

        var pieces = SubtitleSegmenter.Split(text);
        foreach (var name in pieces.Select(SubtitleSegmenter.LabelName).OfType<string>())
            foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2))
                _names[word.ToLowerInvariant()] = 0;
        var names = _names.Keys.ToHashSet(StringComparer.Ordinal);
        var sources = pieces.Where(p => p.Translate).Select(p => SubtitleSegmenter.NormalizeCase(p.Text, names)).ToList();

        string[] translated;
        using var localWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<string>? pendingLocal = null;
        var useLocal = false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_fallback is not null) budget.CancelAfter(_requestBudget);
            var online = sources.Count == 0 ? Task.FromResult(Array.Empty<string>()) : RequestAsync(sources, budget.Token);
            if (_fallback is not null && !online.IsCompleted &&
                await Task.WhenAny(online, Task.Delay(LocalHeadStart, budget.Token)).ConfigureAwait(false) != online)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pendingLocal = FallbackAsync(text, localWork.Token);
            }
            translated = await online.ConfigureAwait(false);
            if (sources.Count != 0) _availability.ReportSuccess();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or DeepLException or JsonException
                                      && !cancellationToken.IsCancellationRequested)
        {
            _availability.ReportFailure(error is DeepLException deepL ? deepL.State : OnlineKeyState.Unreachable);
            useLocal = true;
            return await (pendingLocal ?? FallbackAsync(text, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            if (!useLocal && pendingLocal is not null)
            {
                localWork.Cancel();
                _ = pendingLocal.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        var builder = new StringBuilder();
        var translations = new List<string?>(pieces.Count);
        var next = 0;
        foreach (var piece in pieces)
        {
            var sentence = piece.Translate ? translated[next++] : null;
            builder.Append(sentence ?? piece.Text);
            translations.Add(sentence);
        }
        if (_speech.Count >= 512) _speech.Clear();
        _speech[text] = SpeechScript.Lines(pieces, translations);
        var result = builder.ToString();
        if (_cache.Count >= CacheLimit) _cache.Clear();
        _cache[text] = result;
        _previous = text;
        return result;
    }

    public IReadOnlyList<SpokenLine> SpokenLines(string text) =>
        _speech.TryGetValue(text, out var lines) ? lines
        : _cache.TryGetValue(text, out var translation) ? [new SpokenLine(text, translation)]
        : _fallback?.SpokenLines(text) ?? [];

    private async Task<string> FallbackAsync(string text, CancellationToken cancellationToken) =>
        _fallback is null ? throw new TranslatorException(TranslatorStatus.NeedsDownload)
            : await _fallback.TranslateAsync(text, cancellationToken).ConfigureAwait(false);

    private async Task ProbeQuotaAsync()
    {
        _availability.ObserveUsage(await CheckAsync(_key, _http).ConfigureAwait(false));
    }

    private async Task<string[]> RequestAsync(List<string> sentences, CancellationToken cancellationToken)
    {
        // Oyun konuşması: "siz" değil "sen" (DeepL Türkçede desteklemiyorsa yok sayar).
        var body = new TranslateRequest(sentences, "EN", "TR", _previous, "prefer_less", true);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri(_key), "translate"))
        {
            Content = JsonContent.Create(body, DeepLJson.Default.TranslateRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _key);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        ThrowIfFailed(response);
        var reply = await response.Content.ReadFromJsonAsync(DeepLJson.Default.TranslateResponse, cancellationToken).ConfigureAwait(false);
        if (reply?.Translations is not { } list || list.Count != sentences.Count ||
            list.Any(item => string.IsNullOrWhiteSpace(item?.Text)))
            throw new DeepLException(OnlineKeyState.Unreachable);
        return [.. list.Select(t => t.Text)];
    }

    /// <summary>Ayarlardaki "Dene" düğmesi: anahtar geçerli mi, bu ay ne kadar kullanıldı.</summary>
    public static async Task<OnlineKeyCheck> CheckAsync(string key, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        if (key.Length == 0) return new(OnlineKeyState.Invalid);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri(key), "usage"));
            request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
            using var response = await (http ?? SharedClient.Value).SendAsync(request, cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(response);
            var usage = await response.Content.ReadFromJsonAsync(DeepLJson.Default.UsageResponse, cancellationToken).ConfigureAwait(false);
            if (usage is not { CharacterCount: long used, CharacterLimit: long limit })
                return new(OnlineKeyState.Unreachable);
            if (limit > 0 && used >= limit) return new(OnlineKeyState.QuotaExceeded, used, limit);
            if (usage is { ApiKeyCharacterCount: long keyUsed, ApiKeyCharacterLimit: long keyLimit } &&
                keyLimit > 0 && keyUsed >= keyLimit)
                return new(OnlineKeyState.QuotaExceeded, keyUsed, keyLimit);
            return new(OnlineKeyState.Valid, used, limit);
        }
        catch (DeepLException error) { return new(error.State); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
                                      && !cancellationToken.IsCancellationRequested)
        {
            return new(OnlineKeyState.Unreachable);
        }
    }

    private static void ThrowIfFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new DeepLException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OnlineKeyState.Invalid,
            (HttpStatusCode)456 => OnlineKeyState.QuotaExceeded,
            _ => OnlineKeyState.Unreachable,
        });
    }
}

internal sealed class DeepLException(OnlineKeyState state) : Exception($"DeepL: {state}")
{
    public OnlineKeyState State { get; } = state;
}

internal sealed record TranslateRequest(
    [property: JsonPropertyName("text")] IReadOnlyList<string> Text,
    [property: JsonPropertyName("source_lang")] string SourceLang,
    [property: JsonPropertyName("target_lang")] string TargetLang,
    [property: JsonPropertyName("context"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Context,
    [property: JsonPropertyName("formality")] string Formality,
    [property: JsonPropertyName("preserve_formatting")] bool PreserveFormatting);

internal sealed record TranslateResponse([property: JsonPropertyName("translations")] IReadOnlyList<TranslatedText>? Translations);

internal sealed record TranslatedText([property: JsonPropertyName("text")] string Text);

internal sealed record UsageResponse(
    [property: JsonPropertyName("character_count")] long? CharacterCount,
    [property: JsonPropertyName("character_limit")] long? CharacterLimit,
    [property: JsonPropertyName("api_key_character_count")] long? ApiKeyCharacterCount,
    [property: JsonPropertyName("api_key_character_limit")] long? ApiKeyCharacterLimit);

[JsonSerializable(typeof(TranslateRequest))]
[JsonSerializable(typeof(TranslateResponse))]
[JsonSerializable(typeof(UsageResponse))]
internal sealed partial class DeepLJson : JsonSerializerContext;
