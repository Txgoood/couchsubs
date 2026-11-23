// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

public sealed record TranslationMemoryScope(string Game, string SourceLanguage, string TargetLanguage);
public sealed record TranslationMemoryLine(TranslationMemoryScope Scope, string Source, string Translation);

public interface ITranslationMemory
{
    bool TryGet(TranslationMemoryScope scope, string source, out string translation);
    IReadOnlyList<TranslationMemoryLine> Saved(TranslationMemoryScope scope);
    Task SaveAsync(TranslationMemoryLine line);
    Task RemoveAsync(TranslationMemoryScope scope, string source);
}

public sealed class FileTranslationMemory : ITranslationMemory
{
    private readonly string path;
    private readonly SemaphoreSlim writes = new(1);
    private Dictionary<Key, TranslationMemoryLine> lines = [];

    public FileTranslationMemory(string path)
    {
        this.path = Path.GetFullPath(path);
        try { lines = Read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable file must not interrupt translation. Mutations re-read it before writing.
        }
    }

    public bool TryGet(TranslationMemoryScope scope, string source, out string translation)
    {
        if (Volatile.Read(ref lines).TryGetValue(Key.For(scope, source), out var line))
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
        return Volatile.Read(ref lines).Where(pair => pair.Key.Scope == normalized)
            .Select(pair => pair.Value).OrderBy(line => line.Source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task SaveAsync(TranslationMemoryLine line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(line.Translation);
        return UpdateAsync(Key.For(line.Scope, line.Source), line with { Translation = line.Translation.Trim() });
    }

    public Task RemoveAsync(TranslationMemoryScope scope, string source) => UpdateAsync(Key.For(scope, source), null);

    private async Task UpdateAsync(Key key, TranslationMemoryLine? entry)
    {
        await writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Merge with disk while holding the file lock in case two app instances are open.
                using var guard = File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                var updated = Read();
                if (entry is null) updated.Remove(key);
                else updated[key] = entry;

                var temporary = path + ".tmp";
                try
                {
                    using (var output = File.Create(temporary)) JsonSerializer.Serialize(output, updated.Values);
                    File.Move(temporary, path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                Volatile.Write(ref lines, updated);
            }).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }

    private Dictionary<Key, TranslationMemoryLine> Read()
    {
        Dictionary<Key, TranslationMemoryLine> result = [];
        try
        {
            using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entries = JsonSerializer.Deserialize<TranslationMemoryLine[]>(input) ?? throw new JsonException();
            foreach (var entry in entries)
            {
                if (entry?.Scope is not { SourceLanguage.Length: > 0, TargetLanguage.Length: > 0 } ||
                    string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Translation))
                    throw new JsonException();
                result[Key.For(entry.Scope, entry.Source)] = entry;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return result;
    }

    private sealed record Key(TranslationMemoryScope Scope, string Source)
    {
        public static Key For(TranslationMemoryScope scope, string source = "") => new(new TranslationMemoryScope(
            GameCastLibrary.Normalize(scope.Game).ToUpperInvariant(),
            scope.SourceLanguage.ToLowerInvariant(), scope.TargetLanguage.ToLowerInvariant()),
            source.ReplaceLineEndings("\n").Trim());
    }
}

internal sealed class MemoryTranslator(ITextTranslator inner, ITranslationMemory memory, TranslationMemoryScope scope)
    : ITextTranslator
{
    public bool TryGetSavedTranslation(string source, out string translation) => memory.TryGet(scope, source, out translation);

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
