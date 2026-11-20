// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using System.Text.Json;

namespace CouchSubs.Shell;

/// <summary>
/// Crash-safe Windows settings persistence. User game casts and keyboard
/// preferences are never overwritten in-place by a partially written JSON file.
/// The .bak file remains at the same location for recovery.
/// </summary>
public static class SettingsFilePersistence
{
    public static UserSettings Load(string path)
    {
        if (TryRead(path, out var primary)) return primary!;
        if (TryRead(path + ".bak", out var backup)) return backup!;
        return new UserSettings();
    }

    public static void Save(string path, UserSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new IOException("Cannot determine settings directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings);
            // Serialize first and flush before touching the existing settings.
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, bufferSize: 16384))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // Old valid settings are backed up atomically on successful swap.
                // If the old primary was damaged, preserve any VALID backup.
                if (TryRead(path, out _))
                    File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(temporary, path, overwrite: true);
            }
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool TryRead(string path, out UserSettings? value)
    {
        value = null;
        try
        {
            if (!File.Exists(path)) return false;
            value = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path));
            return value is not null && value.Dubbing is not null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
