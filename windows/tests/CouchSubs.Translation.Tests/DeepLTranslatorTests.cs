// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text;
using System.Text.Json;
using CouchSubs.Core;
using CouchSubs.Translation;
using Xunit;

namespace CouchSubs.Translation.Tests;

public class DeepLTranslatorTests
{
    /// <summary>DeepL yerine geçen sahte sunucu: gelen istekleri saklar, verilen yanıtı döner.</summary>
    private sealed class FakeDeepL(Func<HttpRequestMessage, string, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return reply(request, body);
        }
    }

    private sealed class FakeLocal : ITextTranslator
    {
        public List<string> Calls { get; } = [];
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Add(text);
            return Task.FromResult("yerel: " + text);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SlowDeepL : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Her cümleyi "[tr] cümle" diye çevirir.</summary>
    private static HttpResponseMessage Echo(HttpRequestMessage request, string body)
    {
        var texts = JsonDocument.Parse(body).RootElement.GetProperty("text").EnumerateArray().Select(t => t.GetString());
        return Json(JsonSerializer.Serialize(new { translations = texts.Select(t => new { text = "[tr] " + t }) }));
    }

    [Fact]
    public async Task SpeakerLabelStaysAndPreviousSubtitleIsContext()
    {
        var deepL = new FakeDeepL(Echo);
        var translator = new DeepLTranslator("abc:fx", new FakeLocal(), new HttpClient(deepL));

        Assert.Equal("KRATOS: [tr] We move on.", await translator.TranslateAsync("KRATOS: We move on."));
        await translator.TranslateAsync("Stay close.");

        var (first, firstBody) = deepL.Requests[0];
        Assert.Equal("https://api-free.deepl.com/v2/translate", first.RequestUri!.AbsoluteUri);
        Assert.Equal("DeepL-Auth-Key abc:fx", first.Headers.Authorization!.ToString());
        var json = JsonDocument.Parse(firstBody).RootElement;
        Assert.Equal("We move on.", json.GetProperty("text")[0].GetString());
        Assert.Equal("TR", json.GetProperty("target_lang").GetString());
        Assert.False(json.TryGetProperty("context", out _));
        Assert.Equal("KRATOS: We move on.", JsonDocument.Parse(deepL.Requests[1].Body).RootElement.GetProperty("context").GetString());
    }

    [Fact]
    public async Task SameSubtitleIsNotSentTwice()
    {
        var deepL = new FakeDeepL(Echo);
        var translator = new DeepLTranslator("abc", new FakeLocal(), new HttpClient(deepL));
        await translator.TranslateAsync("Who are you?");
        await translator.TranslateAsync("Who are you?");
        Assert.Single(deepL.Requests);
        Assert.StartsWith("https://api.deepl.com/", deepL.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    /// <summary>Kota biterse ya da DeepL'e ulaşılamazsa oyun altyazısız kalmaz; bir süre DeepL hiç denenmez.</summary>
    [Fact]
    public async Task FallsBackToLocalModelWhenDeepLFails()
    {
        var deepL = new FakeDeepL((_, _) => new HttpResponseMessage((HttpStatusCode)456));
        var local = new FakeLocal();
        var translator = new DeepLTranslator("abc:fx", local, new HttpClient(deepL));

        Assert.Equal("yerel: Run!", await translator.TranslateAsync("Run!"));
        Assert.Equal(OnlineKeyState.QuotaExceeded, translator.LastError);
        Assert.Equal("yerel: Hide!", await translator.TranslateAsync("Hide!"));
        Assert.Single(deepL.Requests);
        Assert.Equal(["Run!", "Hide!"], local.Calls);
    }

    [Fact]
    public async Task QuotaIsSharedAndRecoversThroughUsageWithoutBlockingDialogue()
    {
        var clock = new TestClock();
        var availability = new DeepLAvailability(clock);
        var limited = true;
        var provider = new FakeDeepL((request, body) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json("""{"character_count":0,"character_limit":1000000}""");
            if (limited) return new HttpResponseMessage((HttpStatusCode)456);
            return Echo(request, body);
        });
        using var http = new HttpClient(provider);
        var local = new FakeLocal();
        var first = new DeepLTranslator("abc:fx", local, http, availability);
        var afterRestart = new DeepLTranslator("abc:fx", local, http, availability);

        Assert.Equal("yerel: Run!", await first.TranslateAsync("Run!"));
        clock.Now += TimeSpan.FromMinutes(59);
        Assert.Equal("yerel: Hide!", await afterRestart.TranslateAsync("Hide!"));
        Assert.Single(provider.Requests);
        limited = false;
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal("yerel: Wait!", await afterRestart.TranslateAsync("Wait!"));
        Assert.Equal(HttpMethod.Get, provider.Requests[1].Request.Method);
        Assert.Equal("[tr] Follow me.", await first.TranslateAsync("Follow me."));
        Assert.Null(afterRestart.LastError);
    }

    [Fact]
    public async Task SlowNetworkFallsBackButCallerCancellationDoesNotBlockDeepL()
    {
        using var http = new HttpClient(new SlowDeepL());
        var local = new FakeLocal();
        var state = new DeepLAvailability();
        var translator = new DeepLTranslator("abc", local, http, state, TimeSpan.FromMilliseconds(80));
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => translator.TranslateAsync("Old line.", cancelled.Token));
        Assert.Null(state.LastError);
        Assert.Equal("yerel: New line.", await translator.TranslateAsync("New line."));
        Assert.Equal(OnlineKeyState.Unreachable, state.LastError);
        Assert.Equal(["New line."], local.Calls);
    }

    [Fact]
    public async Task InvalidOnlinePayloadFallsBackToLocalTranslation()
    {
        using var http = new HttpClient(new FakeDeepL((_, _) => Json("broken")));
        var translator = new DeepLTranslator("abc:fx", new FakeLocal(), http);
        Assert.Equal("yerel: Stay here.", await translator.TranslateAsync("Stay here."));
        Assert.Equal(OnlineKeyState.Unreachable, translator.LastError);
    }

    [Fact]
    public async Task KeyCheckReportsUsageAndRejectedKeys()
    {
        var usage = new FakeDeepL((_, _) => Json("""{"character_count":1200,"character_limit":500000}"""));
        Assert.Equal(new OnlineKeyCheck(OnlineKeyState.Valid, 1200, 500000), await DeepLTranslator.CheckAsync("k:fx", new HttpClient(usage)));
        Assert.EndsWith("/v2/usage", usage.Requests[0].Request.RequestUri!.AbsoluteUri);

        var rejected = new FakeDeepL((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.Equal(OnlineKeyState.Invalid, (await DeepLTranslator.CheckAsync("k", new HttpClient(rejected))).State);

        var full = new FakeDeepL((_, _) => Json("""{"character_count":500000,"character_limit":500000}"""));
        Assert.Equal(OnlineKeyState.QuotaExceeded, (await DeepLTranslator.CheckAsync("k", new HttpClient(full))).State);

        var keyFull = new FakeDeepL((_, _) => Json("""
            {"character_count":750000,"character_limit":1000000,"api_key_character_count":200000,"api_key_character_limit":200000}
            """));
        Assert.Equal(new OnlineKeyCheck(OnlineKeyState.QuotaExceeded, 200000, 200000),
            await DeepLTranslator.CheckAsync("k", new HttpClient(keyFull)));

        Assert.Equal(OnlineKeyState.Invalid, (await DeepLTranslator.CheckAsync("  ")).State);
    }
}
