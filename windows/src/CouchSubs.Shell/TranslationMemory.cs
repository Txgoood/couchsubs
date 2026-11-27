// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.RegularExpressions;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

public sealed record TranslationMemoryScope(string Game, string SourceLanguage, string TargetLanguage);
public sealed record TranslationMemoryLine(TranslationMemoryScope Scope, string Source, string Translation)
{
    public DateTimeOffset? AutomaticallySavedAt { get; init; }
    public bool Excluded { get; init; }
}

public interface ITranslationMemory
{
    bool TryGet(TranslationMemoryScope scope, string source, out string translation);
    bool TryGetManual(TranslationMemoryScope scope, string source, out string translation)
    {
        translation = "";
        return false;
    }
    IReadOnlyList<TranslationMemoryLine> Saved(TranslationMemoryScope scope);
    Task SaveAsync(TranslationMemoryLine line);
    Task RemoveAsync(TranslationMemoryScope scope, string source);
    void Record(TranslationMemoryLine line);
    Task FlushAsync();
}

public sealed partial class FileTranslationMemory : ITranslationMemory
{
    private const int AutomaticLimit = 10_000;
    private const int PendingLimit = 512;
    private readonly string path;
    private readonly string? legacyPath;
    private readonly SemaphoreSlim writes = new(1);
    private readonly object pendingGate = new();
    private readonly Dictionary<Key, TranslationMemoryLine> pending = [];
    private Snapshot snapshot = new([]);
    private bool writeScheduled;

    public FileTranslationMemory(string path, string? legacyPath = null)
    {
        this.path = Path.GetFullPath(path);
        this.legacyPath = legacyPath;
        try { snapshot = new Snapshot(Read()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable file must not interrupt translation. Mutations re-read it before writing.
        }
    }

    public bool TryGet(TranslationMemoryScope scope, string source, out string translation) =>
        TryGet(scope, source, out translation, manualOnly: false);

    public bool TryGetManual(TranslationMemoryScope scope, string source, out string translation) =>
        TryGet(scope, source, out translation, manualOnly: true);

    private bool TryGet(TranslationMemoryScope scope, string source, out string translation, bool manualOnly)
    {
        var current = Volatile.Read(ref snapshot);
        var key = Key.For(scope, source);
        if ((current.Lines.TryGetValue(key, out var line) || current.Equivalent.TryGetValue(key.Equivalent(), out line)) &&
            !line.Excluded && (!manualOnly || line.AutomaticallySavedAt is null))
        {
            translation = line.Translation;
            return true;
        }
        translation = "";
        return false;
    }

    public IReadOnlyList<TranslationMemoryLine> Saved(TranslationMemoryScope scope)
    {
        var normalized = Key.For(scope).Scope;
        return Volatile.Read(ref snapshot).Lines.Where(pair => pair.Key.Scope == normalized && !pair.Value.Excluded)
            .Select(pair => pair.Value).OrderBy(line => line.Source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task SaveAsync(TranslationMemoryLine line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(line.Translation);
        var key = Key.For(line.Scope, line.Source);
        return UpdateAsync(stored =>
        {
            if (stored.Equivalent.TryGetValue(key.Equivalent(), out var prior) &&
                (prior.Excluded || prior.AutomaticallySavedAt is not null))
                stored.Lines.Remove(Key.For(prior.Scope, prior.Source));
            stored.Lines[key] = new(line.Scope, line.Source, line.Translation.Trim());
            return true;
        });
    }

    public Task RemoveAsync(TranslationMemoryScope scope, string source) => UpdateAsync(stored =>
    {
        // Keep the exclusion so the next OCR pass does not save the deleted entry again.
        stored.Lines[Key.For(scope, source)] = new(scope, source, "") { Excluded = true };
        return true;
    });

    public void Record(TranslationMemoryLine line)
    {
        if (!line.Source.Any(char.IsLetter) || !line.Translation.Any(char.IsLetter) ||
            TextMatch.Normalize(line.Source) == TextMatch.Normalize(line.Translation)) return;

        var key = Key.For(line.Scope, line.Source).Equivalent();
        lock (pendingGate)
        {
            if (!Volatile.Read(ref snapshot).Equivalent.ContainsKey(key) && pending.Count < PendingLimit && !pending.ContainsKey(key))
                pending.Add(key, line with { AutomaticallySavedAt = DateTimeOffset.UtcNow });
            if (pending.Count == 0 || writeScheduled) return;
            writeScheduled = true;
        }
        _ = WriteAfterDelayAsync();
    }

    private async Task WriteAfterDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        lock (pendingGate) writeScheduled = false;
        try { await FlushAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // The batch stays pending and the next game update or normal exit can retry it.
        }
    }

    public async Task FlushAsync()
    {
        KeyValuePair<Key, TranslationMemoryLine>[] batch = [];
        await UpdateAsync(stored =>
        {
            lock (pendingGate) batch = [.. pending];
            var changed = false;
            foreach (var (key, line) in batch)
            {
                if (!stored.Equivalent.TryAdd(key, line)) continue;
                stored.Lines.Add(Key.For(line.Scope, line.Source), line);
                changed = true;
            }
            if (changed) TrimAutomatic(stored.Lines);
            return changed;
        }, hasWork: () => { lock (pendingGate) return pending.Count > 0; }).ConfigureAwait(false);

        lock (pendingGate)
        {
            foreach (var (key, entry) in batch)
                if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) pending.Remove(key);
        }
    }

    private static void TrimAutomatic(Dictionary<Key, TranslationMemoryLine> lines)
    {
        var automatic = lines.Where(pair => pair.Value.AutomaticallySavedAt is not null).ToArray();
        if (automatic.Length <= AutomaticLimit) return;
        foreach (var (key, _) in automatic.OrderBy(pair => pair.Value.AutomaticallySavedAt).Take(automatic.Length - AutomaticLimit))
            lines.Remove(key);
    }

    private async Task UpdateAsync(Func<Snapshot, bool> update, Func<bool>? hasWork = null)
    {
        await writes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (hasWork?.Invoke() == false) return;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Merge with disk while holding the file lock in case two app instances are open.
                using var guard = File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                var stored = new Snapshot(Read());
                if (update(stored)) Write(stored.Lines.Values);
                Volatile.Write(ref snapshot, new Snapshot(stored.Lines));
            }).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }

    private void Write(IEnumerable<TranslationMemoryLine> lines)
    {
        var temporary = path + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) JsonSerializer.Serialize(output, lines);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private Dictionary<Key, TranslationMemoryLine> Read() =>
        Read(path) ?? (legacyPath is null ? null : Read(legacyPath)) ?? [];

    private static Dictionary<Key, TranslationMemoryLine>? Read(string file)
    {
        Dictionary<Key, TranslationMemoryLine> result = [];
        try
        {
            using var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entries = JsonSerializer.Deserialize<TranslationMemoryLine[]>(input) ?? throw new JsonException();
            foreach (var entry in entries)
            {
                if (entry?.Scope is not { SourceLanguage.Length: > 0, TargetLanguage.Length: > 0 } ||
                    string.IsNullOrWhiteSpace(entry.Source) || (!entry.Excluded && string.IsNullOrWhiteSpace(entry.Translation)))
                    throw new JsonException();
                result[Key.For(entry.Scope, entry.Source)] = entry;
            }
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        return result;
    }

    private sealed class Snapshot(Dictionary<Key, TranslationMemoryLine> lines)
    {
        public Dictionary<Key, TranslationMemoryLine> Lines { get; } = lines;
        public Dictionary<Key, TranslationMemoryLine> Equivalent { get; } = Index(lines);

        private static Dictionary<Key, TranslationMemoryLine> Index(Dictionary<Key, TranslationMemoryLine> lines)
        {
            Dictionary<Key, TranslationMemoryLine> index = [];
            foreach (var (key, line) in lines)
            {
                var equivalent = key.Equivalent();
                if (index.TryGetValue(equivalent, out var existing) && Priority(existing) >= Priority(line)) continue;
                index[equivalent] = line;
            }
            return index;
        }

        private static int Priority(TranslationMemoryLine line) => line.Excluded ? 1 : line.AutomaticallySavedAt is null ? 2 : 0;
    }

    [GeneratedRegex(@"[^\S\n]*\n[^\S\n]*|[^\S\n]+")]
    private static partial Regex Whitespace();

    private sealed record Key(TranslationMemoryScope Scope, string Source)
    {
        public static Key For(TranslationMemoryScope scope, string source = "") => new(new TranslationMemoryScope(
            GameCastLibrary.Normalize(scope.Game).ToUpperInvariant(),
            scope.SourceLanguage.ToLowerInvariant(), scope.TargetLanguage.ToLowerInvariant()),
            source.ReplaceLineEndings("\n").Trim());

        // Keep words, punctuation and speaker line breaks. Small OCR formatting changes can share a record.
        public Key Equivalent() => this with
        {
            Source = Whitespace().Replace(Source, match => match.Value.Contains('\n') ? "\n" : " ")
                .Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"').Replace("…", "...")
                .ToUpperInvariant()
        };
    }
}

internal sealed class MemoryTranslator(ITextTranslator inner, ITranslationMemory memory, TranslationMemoryScope scope)
    : ITextTranslator
{
    public bool TryGetSavedTranslation(string source, out string translation) => memory.TryGet(scope, source, out translation);
    public bool TryGetManualTranslation(string source, out string translation) => memory.TryGetManual(scope, source, out translation);

    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryGetSavedTranslation(text, out var saved)
            ? Task.FromResult(saved) : inner.TranslateAsync(text, cancellationToken);
    }

    public IReadOnlyList<SpokenLine> SpokenLines(string source, string translation) =>
        TryGetSavedTranslation(source, out var saved) && saved == translation
            ? SubtitleDialogue.Parse(source, translation) : inner.SpokenLines(source, translation);
}
