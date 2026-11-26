// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.Broadplay;

internal sealed record NamedPlaylist(string Name, string[] Sources);
internal sealed record ResumePoint(string Source, double Seconds, long Size, long ModifiedUtcTicks);
internal sealed record CollectionState
{
    public int Version { get; init; } = 1;
    public QueueEntry[] Queue { get; init; } = [];
    public NamedPlaylist[] Playlists { get; init; } = [];
    public string[] IndexedFolders { get; init; } = [];
    public string[] Recents { get; init; } = [];
    public ResumePoint[] Positions { get; init; } = [];
    public double Volume { get; init; } = 1;
    public bool Muted { get; init; }
    public bool SaveProgress { get; init; }
    public QueueRepeat Repeat { get; init; }
    public bool Shuffle { get; init; }

    public static string DesktopPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolderOption.DoNotVerify),
        "Broiler", "Broadplay", "collection.json");

    public static CollectionState Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 4 * 1024 * 1024)
            return new();
        try
        {
            CollectionState? state = JsonSerializer.Deserialize(json, CollectionJson.Default.CollectionState);
            if (state is not { Version: 1 } || state.Queue is null || state.Playlists is null ||
                state.IndexedFolders is null || state.Recents is null || state.Positions is null)
                return new();
            return state with
            {
                Queue = state.Queue.Where(item => item is not null && item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Source))
                    .DistinctBy(item => item.Id).Take(10000).ToArray(),
                Playlists = state.Playlists.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name) && item.Sources is not null)
                    .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(1000).ToArray(),
                Positions = state.Positions.Where(item => item is not null && item.Source is not null && double.IsFinite(item.Seconds))
                    .Take(500).ToArray(),
                Volume = double.IsFinite(state.Volume) ? Math.Clamp(state.Volume, 0, 1) : 1,
                Repeat = Enum.IsDefined(state.Repeat) ? state.Repeat : QueueRepeat.Off,
            };
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, CollectionJson.Default.CollectionState);

    public static CollectionState Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= 4 * 1024 * 1024 ? Parse(File.ReadAllText(path)) : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Write(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, CollectionJson.Default.CollectionState);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

[JsonSerializable(typeof(CollectionState))]
internal sealed partial class CollectionJson : JsonSerializerContext;

internal static class LocalPlaylist
{
    public static bool IsPlaylist(string path) =>
        Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".m3u8", StringComparison.OrdinalIgnoreCase);

    public static bool IsRemote(string source)
    {
        return source.StartsWith("//", StringComparison.Ordinal) || source.StartsWith("\\\\", StringComparison.Ordinal) ||
            (!Path.IsPathFullyQualified(source) && Uri.TryCreate(source, UriKind.Absolute, out _));
    }

    public static QueueEntry[] Import(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The playlist exceeds 2 MiB.");
        string directory = Path.GetDirectoryName(file.FullName)!;
        var result = new List<QueueEntry>();
        foreach (string raw in File.ReadLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This is an HLS manifest. Local playlist import does not open streams.");
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (result.Count >= 10000)
                throw new InvalidDataException("The playlist exceeds 10,000 entries.");
            bool remote = IsRemote(line);
            result.Add(QueueEntry.Create(remote ? line : Path.GetFullPath(line, directory), remote));
        }
        return result.ToArray();
    }

    public static void Export(string path, IEnumerable<QueueEntry> entries)
    {
        if (IsRemote(path))
            throw new InvalidDataException("Choose a local playlist destination.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine("#EXTM3U");
        foreach (QueueEntry entry in entries)
        {
            string line = entry.Remote ? entry.Source : Path.GetRelativePath(directory, entry.Source);
            if (line.IndexOfAny(['\r', '\n']) < 0)
                writer.WriteLine(line.StartsWith('#') ? "." + Path.DirectorySeparatorChar + line : line);
        }
    }
}
