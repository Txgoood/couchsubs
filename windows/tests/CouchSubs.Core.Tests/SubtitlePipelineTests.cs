// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

internal sealed class FakeReader(params string[] texts) : ITextReader
{
    private readonly Queue<string> texts = new(texts);
    public List<int> ReadThreads { get; } = [];
    public string Read(Frame image)
    {
        lock (ReadThreads) ReadThreads.Add(Environment.CurrentManagedThreadId);
        lock (texts) return texts.Count == 0 ? "" : texts.Dequeue();
    }
}

/// <summary>Satırları yerleriyle döndüren okuyucu.</summary>
internal sealed class LinesReader(Reading reading) : ITextReader
{
    public string Read(Frame image) => reading.Text;
    public Reading ReadLines(Frame image, bool deep) => reading;
}

internal sealed class FakeTranslator : ITextTranslator
{
    public Exception? Error { get; set; }
    public int FailuresLeft { get; set; }
    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (Error is not null) throw Error;
        if (FailuresLeft > 0) { FailuresLeft--; throw new TimeoutException(); }
        return Task.FromResult("TR: " + text);
    }
}

/// Çevirileri test bırakana kadar bekletir; açıldıktan sonra gelenler beklemez.
internal sealed class GatedTranslator : ITextTranslator
{
    private readonly object gate = new();
    private readonly List<(string Text, TaskCompletionSource Done)> waiting = [];
    private bool isOpen;
    public List<string> Requested { get; } = [];

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? done = null;
        lock (gate)
        {
            Requested.Add(text);
            if (!isOpen) { done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); waiting.Add((text, done)); }
        }
        if (done is not null) await done.Task;
        return "TR: " + text;
    }

    public void Open()
    {
        lock (gate) { isOpen = true; foreach (var w in waiting) w.Done.SetResult(); waiting.Clear(); }
    }

    /// Yalnız bu satırın çevirisini bitirir (sırayı test belirler).
    public async Task Release(string text)
    {
        while (true)
        {
            lock (gate)
            {
                var index = waiting.FindIndex(w => w.Text == text);
                if (index >= 0) { var entry = waiting[index]; waiting.RemoveAt(index); entry.Done.SetResult(); return; }
            }
            await Task.Delay(5);
        }
    }
}

/// <summary>A translator that honors cancellation of an outdated dialogue.</summary>
internal sealed class CancelAwareTranslator : ITextTranslator
{
    private readonly TaskCompletionSource started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Cancelled { get; private set; }
    public Task FirstStarted => started.Task;

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (text == "First!")
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelCount);
                throw;
            }
        }
        return "TR: " + text;
    }

    private int cancelCount;
    public int CancelCount => Volatile.Read(ref cancelCount);
}

internal sealed class FakeSender : ISubtitleSender
{
    public List<string> Sent { get; } = [];
    public List<int> Durations { get; } = [];
    public List<TextBox?> Boxes { get; } = [];
    public int Clears { get; private set; }
    public int ReplaceClears { get; private set; }
    public void ClearForNewLine() => ReplaceClears++;
    public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null)
    {
        lock (Sent) { Sent.Add(text); Durations.Add(durationMs); Boxes.Add(box); }
    }
    public void Clear() => Clears++;
}

public class SubtitlePipelineTests
{
    private static readonly Frame Image = Frame.Blank(64, 64);

    private static async Task Run(SubtitlePipeline pipeline, int frames, bool waitEach = true)
    {
        for (var i = 0; i < frames; i++)
        {
            await pipeline.ProcessAsync(Image, new CropRegion());
            if (waitEach) await pipeline.WaitForTranslationsAsync();
        }
    }

    [Fact]
    public async Task ButtonPromptsAreNotTranslated()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(
            new FakeReader("R2 Swing", "R2 Swing", "Hold the gate\nR2 Swing", "Hold the gate\nR2 Swing"), new FakeTranslator(), sender);
        await Run(pipeline, 4);
        Assert.Equal(["TR: Hold the gate"], sender.Sent);
    }

    [Fact]
    public async Task ShopPromptOcrErrorsDoNotReachTranslationOrSpeech()
    {
        const string dialogue = "Press on.";
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Press Ofo talk", "Press Ofo talk",
            "Pres @ t, browse the shop menu", "Pres @ t, browse the shop menu", dialogue),
            new FakeTranslator(), sender);
        var speechCount = 0;
        pipeline.SpeechReady += (_, _) => speechCount++;

        await Run(pipeline, 5);

        Assert.Equal([$"TR: {dialogue}"], sender.Sent);
        Assert.Equal(1, speechCount);
    }

    [Theory]
    [InlineData("Pres @ o browse-the-Shop menu")]
    [InlineData("Pre S) Oht browse the shop menu")]
    [InlineData("Hol o interact with")]
    [InlineData("Ho(d interact with")]
    [InlineData("Hold to interact with")]
    [InlineData("Purchase a longarm")]
    [InlineData("Return to Andrew")]
    [InlineData("Press E to open inventory")]
    public async Task GameplayInstructionsCanBeShownButNeverSpoken(string prompt)
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(prompt, prompt), new DistinctTranslator(), sender);
        var spoken = 0;
        pipeline.SpeechReady += (_, _) => spoken++;

        await Run(pipeline, 2);

        Assert.Equal([DistinctTranslator.Target], sender.Sent);
        Assert.Equal(0, spoken);
    }

    [Fact]
    public async Task CaptionWinsWhenInterfaceTextAndDialogueAreTogether()
    {
        var reading = Reading.FromText("Andrew: Do you need anything?\nPres @ o browse-the-Shop menu");
        var pipeline = new SubtitlePipeline(new LinesReader(reading), new DistinctTranslator(), new FakeSender());
        var spoken = new List<string>();
        pipeline.SpeechReady += (lines, _) => spoken.AddRange(lines.Select(line => line.English));

        await Run(pipeline, 2);

        Assert.Equal("Andrew: Do you need anything?", pipeline.LastPair?.Source);
        Assert.Equal(["Do you need anything?"], spoken);
    }

    [Fact]
    public async Task CorrectedSpeakerLabelCanUpdateCaptionWithoutSubmittingSpeechAgain()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Oh, Lootin' Jason.", "Afygrew.• Oh, Lootin' Jason.",
            "Jason: Just looking."), new DistinctTranslator(), sender);
        var spoken = new List<string>();
        pipeline.SpeechReady += (lines, _) => spoken.AddRange(lines.Select(line => line.English));

        await Run(pipeline, 2);
        Assert.Equal("Andrew: Oh, Lootin' Jason.", pipeline.LastPair?.Source);
        await Run(pipeline, 1);
        Assert.Equal(["Oh, Lootin' Jason.", "Just looking."], spoken);
    }

    [Fact]
    public async Task PositionedInterfaceModeDoesNotSendObjectivesAsSubtitles()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Purchase a longarm", "Purchase a longarm",
            "Andrew: Ready to pick something?"), new DistinctTranslator(), sender) { ShowInterfaceFallback = false };
        await Run(pipeline, 3);
        Assert.Equal([DistinctTranslator.Target], sender.Sent);
        Assert.Equal("Andrew: Ready to pick something?", pipeline.LastPair?.Source);
    }

    [Theory]
    [InlineData(0.38, 0.62, true)]
    [InlineData(0.04, 0.28, false)]
    public async Task UnlabelledTextNeedsRepeatedReadingInTheSubtitlePosition(double left, double right, bool expectedSpeech)
    {
        var reading = new Reading([new TextLine("Leave it with me", new TextBox(left, .75, right, .84))]);
        var pipeline = new SubtitlePipeline(new LinesReader(reading), new DistinctTranslator(), new FakeSender());
        var count = 0;
        pipeline.SpeechReady += (_, _) => count++;

        await Run(pipeline, 1);
        Assert.Equal(0, count);
        await Run(pipeline, 1);

        Assert.NotNull(pipeline.LastPair);
        Assert.Equal(expectedSpeech ? 1 : 0, count);
    }

    [Fact]
    public async Task OpeningAPromptDoesNotInvalidateDialogueAlreadyReadyToPlay()
    {
        var pipeline = new SubtitlePipeline(new FakeReader("Need anything?", "Purchase a longarm", "Purchase a longarm"),
            new DistinctTranslator(), new FakeSender());
        var readiness = new List<Func<bool>>();
        pipeline.SpeechReady += (_, canStart) => readiness.Add(canStart);

        await Run(pipeline, 3);

        Assert.True(Assert.Single(readiness)());
        Assert.Equal("Purchase a longarm", pipeline.LastPair?.Source);
    }

    private sealed class DistinctTranslator : ITextTranslator
    {
        public const string Target = "Türkçe deneme çıktısı.";
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Target);
    }

    [Fact]
    public async Task OldWorkerTailDoesNotReplaceJasonsNewTurn()
    {
        var pipeline = new SubtitlePipeline(new FakeReader(
            "Worker: Shit wow... not gonna lie,\nwasn't expecting an apology but...sure. Good!",
            "wasn't expecting an apology but...sure. Good!\n'Jason: Well that was pathetic."),
            new DistinctTranslator(), new FakeSender());
        var spoken = new List<CouchSubs.Core.Dubbing.SpokenLine>();
        pipeline.SpeechReady += (lines, _) => spoken.AddRange(lines);

        await Run(pipeline, 2);

        Assert.Equal(["Worker", "Jason"], spoken.Select(line => line.Speaker));
        Assert.Equal("Well that was pathetic.", spoken[^1].English);
    }

    [Theory]
    [InlineData("'Jason: Just relax.", "Jason", "Just relax.")]
    [InlineData("Jason:.Uh, was that out of line?", "Jason", "Uh, was that out of line?")]
    [InlineData("Localr Humility! What a concept!", "Local", "Humility! What a concept!")]
    public async Task DamagedSpeakerLabelStillSelectsTheCorrectSpeaker(string raw, string name, string body)
    {
        var pipeline = new SubtitlePipeline(new FakeReader(raw), new DistinctTranslator(), new FakeSender());
        var spoken = new List<CouchSubs.Core.Dubbing.SpokenLine>();
        pipeline.SpeechReady += (lines, _) => spoken.AddRange(lines);
        await Run(pipeline, 1);
        var line = Assert.Single(spoken);
        Assert.Equal(name, line.Speaker);
        Assert.Equal(body, line.English);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubtitleBoxIsSentInFrameCoordinates(bool alreadyCropped)
    {
        var sender = new FakeSender();
        var reading = new Reading([new TextLine("Hold the gate.", new TextBox(0.25, 0.5, 0.75, 1))]);
        var pipeline = new SubtitlePipeline(new LinesReader(reading), new FakeTranslator(), sender);
        var area = new CropRegion(Top: 0.6, Bottom: 1.0, Left: 0.1, Right: 0.9);
        if (alreadyCropped) await pipeline.ProcessAreaAsync(RegionCropper.Crop(Image, area), area);
        else await pipeline.ProcessAsync(Image, area);
        await pipeline.WaitForTranslationsAsync();
        var box = Assert.Single(sender.Boxes)!.Value;
        Assert.Equal(0.3, box.Left, 9);
        Assert.Equal(0.8, box.Top, 9);
        Assert.Equal(0.7, box.Right, 9);
        Assert.Equal(1.0, box.Bottom, 9);
    }

    [Fact]
    public async Task CurrentSubtitleExposesTheSourceWhileTranslationIsPending()
    {
        var translator = new GatedTranslator();
        var sender = new FakeSender();
        var reading = new Reading([new TextLine("We should go now.", new TextBox(.25, .5, .75, .65))]);
        var pipeline = new SubtitlePipeline(new LinesReader(reading), translator, sender);
        var region = new CropRegion(Top: .6, Bottom: 1, Left: .1, Right: .9);
        await pipeline.ProcessAsync(Image, region);
        try
        {
            Assert.Empty(sender.Sent);
            Assert.Equal("We should go now.", pipeline.CurrentSubtitle?.Text);
            Assert.Equal(.8, pipeline.CurrentSubtitle!.Box!.Value.Top, 6);
            pipeline.Reset();
            Assert.Null(pipeline.CurrentSubtitle);
        }
        finally
        {
            translator.Open();
            await pipeline.WaitForTranslationsAsync();
        }
    }

    [Fact]
    public void CenterStripLinesAreReportedInRegionCoordinates()
    {
        var reading = ReadingFallback.FirstLines(Image, candidate => candidate.Width == 64
            ? Reading.Empty
            : new Reading([new TextLine("Go!", new TextBox(0, 0.5, 1, 1))]));
        var box = reading.Box!.Value;
        Assert.Equal(19.0 / 64, box.Left, 9);
        Assert.Equal(45.0 / 64, box.Right, 9);
        Assert.Equal(0.5, box.Top, 9);
    }

    [Fact]
    public async Task StableTextIsTranslatedAndSentOnce()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate", "Hold the gate"), new FakeTranslator(), sender);
        await Run(pipeline, 3);
        Assert.Equal(["TR: Hold the gate"], sender.Sent);
        Assert.Equal("Hold the gate", pipeline.LastPair?.Source);
    }

    [Fact]
    public async Task LiveTurkishSubtitleIsSpokenWhenTranslatorDoesNotProvideSpeakerLines()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(
            new FakeReader("Hold the gate", "Hold the gate"), new FakeTranslator(), sender);
        var speech = new List<CouchSubs.Core.Dubbing.SpokenLine>();
        pipeline.SpeechReady += (lines, _) => speech.AddRange(lines);
        await Run(pipeline, 2);
        Assert.Equal(["TR: Hold the gate"], sender.Sent);
        var spoken = Assert.Single(speech);
        Assert.Equal("Hold the gate", spoken.English);
        Assert.Equal("TR: Hold the gate", spoken.Turkish);
    }

    [Fact]
    public async Task TextRecognitionRunsOffTheCallingThread()
    {
        var reader = new FakeReader("Hold the gate");
        var pipeline = new SubtitlePipeline(reader, new FakeTranslator(), new FakeSender());
        var caller = Environment.CurrentManagedThreadId;
        await pipeline.ProcessAsync(Image, new CropRegion());
        Assert.DoesNotContain(caller, reader.ReadThreads);
    }

    [Fact]
    public async Task MissingModelSendsNothing()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate"),
            new FakeTranslator { Error = new TranslatorException(TranslatorStatus.NeedsDownload) }, sender);
        await Run(pipeline, 2);
        Assert.Empty(sender.Sent);
        Assert.Equal(TranslatorStatus.NeedsDownload, pipeline.LastError);
    }

    [Fact]
    public async Task FailedTranslationIsRetriedWhileLineStaysOnScreen()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate", "Hold the gate", "Hold the gate"),
            new FakeTranslator { FailuresLeft = 1 }, sender);
        await Run(pipeline, 4);
        Assert.Equal(["TR: Hold the gate"], sender.Sent);
    }

    /// Kareler saniyede ~20 kez okunur; tek tük okunamayan kare altyazıyı TV'den silmemeli.
    [Fact]
    public async Task ShortReadingGapDoesNotClearTheTv()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate", "", "", "Hold the gate"), new FakeTranslator(), sender);
        await Run(pipeline, 5);
        Assert.Equal(0, sender.Clears);
        Assert.Equal(["TR: Hold the gate"], sender.Sent);
    }

    [Fact]
    public async Task SubtitleGoneForSixFramesClearsTheTv()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate", "", "", "", "", "", ""), new FakeTranslator(), sender);
        await Run(pipeline, 8);
        Assert.Equal(1, sender.Clears);
    }

    /// Çeviri sürerken okuma durmamalı; yoksa kısa satırlar kaçar.
    [Fact]
    public async Task ReadingContinuesWhileTranslating()
    {
        var translator = new GatedTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate", "Run", "Run"), translator, new FakeSender());
        await Run(pipeline, 4, waitEach: false);
        translator.Open();
        await pipeline.WaitForTranslationsAsync();
        Assert.Equal(["Hold the gate", "Run"], translator.Requested);
    }

    /// Hızlı konuşmada yeni satır gelse de önceki satırın çevirisi gösterilir; satır kaybolmaz.
    [Fact]
    public async Task EarlierLineIsShownWhenItsTranslationArrivesFirst()
    {
        var sender = new FakeSender();
        var translator = new GatedTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate.", "Run!"), translator, sender);
        await Run(pipeline, 2, waitEach: false);
        // The first translation is stale once a newer English line appears.
        await translator.Release("Hold the gate.");
        await translator.Release("Run!");
        await pipeline.WaitForTranslationsAsync();
        Assert.Equal(["TR: Run!"], sender.Sent);
    }

    [Fact]
    public async Task NewSpeechCancelsTheEarlierTranslationBeforeItCanDub()
    {
        var translator = new CancelAwareTranslator();
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(
            new FakeReader("First!", "Second!"), translator, sender);
        var spoken = new List<CouchSubs.Core.Dubbing.SpokenLine>();
        pipeline.SpeechReady += (lines, _) => spoken.AddRange(lines);

        await pipeline.ProcessAsync(Image, new CropRegion());
        await translator.FirstStarted;
        await pipeline.ProcessAsync(Image, new CropRegion());
        await pipeline.WaitForTranslationsAsync();

        Assert.Equal(1, translator.CancelCount);
        Assert.Equal(["TR: Second!"], sender.Sent);
        Assert.Single(spoken);
        Assert.Equal("Second!", spoken[0].English);
    }

    [Fact]
    public async Task LostOcrFramesDoNotCreateTwoTurkishTranslations()
    {
        var samples = new List<string> { "The police are coming!" };
        samples.AddRange(Enumerable.Repeat("", 6));
        samples.AddRange(Enumerable.Repeat("The police are coming!", 8));
        samples.Add("Get out!");
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader([.. samples]),
            new FakeTranslator(), sender);
        await Run(pipeline, samples.Count);
        Assert.Equal(["TR: The police are coming!", "TR: Get out!"], sender.Sent);
    }

    [Fact]
    public async Task NewEnglishLineClearsOldTurkishImmediately()
    {
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader("First!", "Second!"),
            new FakeTranslator(), sender);
        await Run(pipeline, 2);
        Assert.Equal(["TR: First!", "TR: Second!"], sender.Sent);
        Assert.Equal(2, sender.ReplaceClears);
        Assert.Equal(0, sender.Clears);
    }

    [Theory]
    [InlineData(SubtitleTimingMode.Adaptive, 2500)]
    [InlineData(SubtitleTimingMode.Extended, 4700)]
    [InlineData(SubtitleTimingMode.MatchEnglish, 15000)]
    public void TimingsRespectSelectedMode(SubtitleTimingMode mode, int minimum)
    {
        var duration = SubtitlePipeline.SubtitleDuration("Go!", mode);
        Assert.True(duration >= minimum);
    }

    /// Yeni satırın çevirisi zaten gösterildiyse eski satırınki onun üstüne yazılmaz.
    [Fact]
    public async Task OlderTranslationArrivingAfterNewerIsDropped()
    {
        var sender = new FakeSender();
        var translator = new GatedTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate.", "Run!"), translator, sender);
        await Run(pipeline, 2, waitEach: false);
        await translator.Release("Run!");
        await WaitUntil(() => sender.Sent.Count == 1);
        await translator.Release("Hold the gate.");
        await pipeline.WaitForTranslationsAsync();
        Assert.Equal(["TR: Run!"], sender.Sent);
    }

    /// Satır ekrandan kalktıktan sonra çevirisi gelirse yine de okunacak kadar kısa süre gösterilir.
    [Fact]
    public async Task TranslationArrivingAfterLineLeftIsShownBriefly()
    {
        var sender = new FakeSender();
        var translator = new GatedTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate.", "", "", "", "", "", ""), translator, sender);
        await Run(pipeline, 7, waitEach: false);
        Assert.Equal(1, sender.Clears);
        translator.Open();
        await pipeline.WaitForTranslationsAsync();
        Assert.Equal(["TR: Hold the gate."], sender.Sent);
        Assert.Equal(2000 + "TR: Hold the gate.".Length * 60, sender.Durations[0]);
    }

    /// Çeviri durdurulduysa geç gelen çeviri TV'ye gönderilmez.
    [Fact]
    public async Task TranslationArrivingAfterStopIsDropped()
    {
        var sender = new FakeSender();
        var translator = new GatedTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader("Hold the gate", "Hold the gate"), translator, sender);
        await Run(pipeline, 2, waitEach: false);
        pipeline.Reset();
        translator.Open();
        await pipeline.WaitForTranslationsAsync();
        Assert.Empty(sender.Sent);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline); await Task.Delay(5); }
    }
}

public class ReadingFallbackTests
{
    private static readonly Frame Image = Frame.Blank(64, 64);

    [Fact]
    public void ShortLinesGetAZoomedSecondReading()
    {
        var stripWidth = (int)Math.Round(64 * 0.4) * 2;
        var text = ReadingFallback.FirstReading(Image, candidate => candidate.Width == stripWidth ? "Go!" : "");
        Assert.Equal("Go!", text);
    }

    /// İki yedek okuma da yazı bulursa ortadaki şeridinki seçilir.
    [Fact]
    public void CenterStripReadingWinsOverZoomedRegion()
    {
        var text = ReadingFallback.FirstReading(Image, candidate => candidate.Width switch { 64 => "", 128 => "G0!", _ => "Go!" });
        Assert.Equal("Go!", text);
    }

    [Fact]
    public void FullReadingIsUsedWhenItFindsText()
    {
        var calls = 0;
        var text = ReadingFallback.FirstReading(Image, _ => { Interlocked.Increment(ref calls); return "Hold the gate"; });
        Assert.Equal("Hold the gate", text);
        Assert.Equal(1, calls);
    }

    /// Bölge boşken iki yedek okuma art arda değil aynı anda yapılır; yeni satır daha erken fark edilir.
    [Fact]
    public void FallbackReadingsRunAtTheSameTime()
    {
        // İki yedek okuma birbirini bekler; art arda yapılsalardı buluşamaz, süre dolardı.
        using var barrier = new Barrier(2);
        var met = 0;
        ReadingFallback.FirstReading(Image, candidate =>
        {
            if (candidate.Width != 64 && barrier.SignalAndWait(TimeSpan.FromSeconds(10))) Interlocked.Increment(ref met);
            return "";
        });
        Assert.Equal(2, met);
    }
}

public class RegionCropperTests
{
    [Fact]
    public void DefaultRegionCropsBottomCenterBand()
    {
        var cropped = RegionCropper.Crop(Frame.Blank(1920, 1080), new CropRegion());
        Assert.InRange(cropped.Width, 1382, 1384);
        Assert.InRange(cropped.Height, 378, 379);
    }

    [Fact]
    public void CropKeepsThePixelsOfTheRegion()
    {
        var image = Frame.Blank(10, 10);
        image.Pixels[(9 * image.Stride) + (9 * 4)] = 200; // sağ alt köşe, mavi kanal
        var cropped = RegionCropper.Crop(image, new CropRegion(0.5, 1, 0.5, 1));
        Assert.Equal(200, cropped.Pixels[((cropped.Height - 1) * cropped.Stride) + ((cropped.Width - 1) * 4)]);
    }
}
