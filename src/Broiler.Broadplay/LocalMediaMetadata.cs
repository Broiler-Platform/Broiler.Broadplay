// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Broiler.Media;
using Broiler.Media.Containers;
using Broiler.Media.Containers.Mp3;

namespace Broiler.Broadplay;

internal sealed class NaturalMediaOrder : IComparer<string>
{
    public static readonly NaturalMediaOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        ReadOnlySpan<char> left = x.AsSpan(), right = y.AsSpan();
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                int a = i, b = j;
                while (i < left.Length && char.IsAsciiDigit(left[i])) i++;
                while (j < right.Length && char.IsAsciiDigit(right[j])) j++;
                while (a < i && left[a] == '0') a++;
                while (b < j && right[b] == '0') b++;
                int size = (i - a).CompareTo(j - b);
                if (size != 0) return size;
                int number = left[a..i].SequenceCompareTo(right[b..j]);
                if (number != 0) return number;
                continue;
            }
            int comparison = char.ToUpperInvariant(left[i++]).CompareTo(char.ToUpperInvariant(right[j++]));
            if (comparison != 0) return comparison;
        }
        int remainder = (left.Length - i).CompareTo(right.Length - j);
        return remainder != 0 ? remainder : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }
}

internal sealed record LocalMediaMetadata(string Title, string? Artist, string? Album, int Disc, int Track, TimeSpan Duration)
{
    public string Label => Title +
        (string.IsNullOrEmpty(Artist) ? "" : " · " + Artist) +
        (string.IsNullOrEmpty(Album) ? "" : " · " + Album) +
        (Duration > TimeSpan.Zero ? " · " + BroadplayMediaView.FormatTime(Duration) : "");

    public static LocalMediaMetadata Read(string path)
    {
        string title = Path.GetFileName(path);
        string? artist = null, album = null;
        int disc = 0, track = 0;
        TimeSpan duration = TimeSpan.Zero;
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> prefix = stackalloc byte[4];
            int prefixRead = stream.ReadAtLeast(prefix, 4, throwOnEndOfStream: false);
            stream.Position = 0;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".flac" || (prefixRead == 4 && prefix.SequenceEqual("fLaC"u8)))
            {
                try
                {
                    FlacStreamMetadata flac = FlacStreamMetadata.ReadAsync(stream).AsTask().GetAwaiter().GetResult();
                    if (flac.StreamInfo.SampleRate > 0 && flac.StreamInfo.TotalSamples > 0)
                        duration = TimeSpan.FromTicks((long)((Int128)flac.StreamInfo.TotalSamples * TimeSpan.TicksPerSecond / flac.StreamInfo.SampleRate));

                    foreach (string comment in flac.Comments)
                    {
                        int eq = comment.IndexOf('=');
                        if (eq <= 0)
                            continue;
                        string key = comment[..eq];
                        string value = comment[(eq + 1)..].Trim();
                        if (value.Length == 0)
                            continue;
                        if (key.Equals("TITLE", StringComparison.OrdinalIgnoreCase))
                            title = value;
                        else if (key.Equals("ARTIST", StringComparison.OrdinalIgnoreCase))
                            artist ??= value;
                        else if (key.Equals("ALBUM", StringComparison.OrdinalIgnoreCase))
                            album ??= value;
                        else if (key.Equals("DISCNUMBER", StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Split('/')[0], out int d))
                            disc = d;
                        else if (key.Equals("TRACKNUMBER", StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Split('/')[0], out int t))
                            track = t;
                    }
                }
                catch (MediaException) { }
            }
            else if (ext is not (".wav" or ".wave"))
            {
                foreach ((string id, byte[] data) in Id3Frames(stream))
                {
                    if (id is not ("TIT2" or "TPE1" or "TALB" or "TPOS" or "TRCK" or "TLEN") || data.Length < 2)
                        continue;
                    string text = DecodeText(data);
                    _ = int.TryParse(text.Split('/')[0], out int number);
                    switch (id)
                    {
                        case "TIT2" when text.Length > 0: title = text; break;
                        case "TPE1": artist = text; break;
                        case "TALB": album = text; break;
                        case "TPOS": disc = number; break;
                        case "TRCK": track = number; break;
                        case "TLEN" when number > 0: duration = TimeSpan.FromMilliseconds(number); break;
                    }
                }

                if (ext == ".mp3" || (prefixRead >= 3 && prefix[..3].SequenceEqual("ID3"u8)))
                {
                    try
                    {
                        stream.Position = 0;
                        Mp3Inspection inspection = Mp3Inspection.ReadAsync(stream).AsTask().GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(inspection.Id3.Title) && title == Path.GetFileName(path))
                            title = inspection.Id3.Title!;
                        artist ??= inspection.Id3.Artist;
                        album ??= inspection.Id3.Album;
                    }
                    catch (MediaException) { }
                }
            }

            if (duration <= TimeSpan.Zero)
            {
                stream.Position = 0;
                if (BroadplayAudioAnalyzer.TryAnalyze(stream, path, out var info, out _, includeWaveform: false))
                    duration = info!.Duration;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new(title, artist, album, disc, track, duration);
    }

    public static byte[]? Artwork(Stream stream)
    {
        foreach ((string id, byte[] data) in Id3Frames(stream))
        {
            if (id != "APIC" || data.Length < 8)
                continue;
            // Search only the bounded APIC description prefix for a codec signature.
            for (int offset = 4; offset < Math.Min(data.Length - 8, 1024); offset++)
                if (BroadplayImageFormats.ContentTypeForSignature(data.AsSpan(offset)) is "image/jpeg" or "image/png")
                    return data[offset..];
        }
        return null;
    }

    private static IEnumerable<(string, byte[])> Id3Frames(Stream stream)
    {
        var header = new byte[10];
        if (stream.ReadAtLeast(header, 10, false) != 10 || !header.AsSpan(0, 3).SequenceEqual("ID3"u8) ||
            header[3] is not (3 or 4) || header[5] != 0)
            yield break;
        int remaining = SyncSafe(header.AsSpan(6));
        if (remaining > 2 * 1024 * 1024)
            yield break;
        bool v4 = header[3] == 4;
        while (remaining >= 10 && stream.ReadAtLeast(header, 10, false) == 10 && header[0] != 0)
        {
            int size = v4 ? SyncSafe(header.AsSpan(4, 4)) : BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));
            if (size < 1 || size > remaining - 10)
                yield break;
            var data = new byte[size];
            if (stream.ReadAtLeast(data, size, false) != size)
                yield break;
            remaining -= size + 10;
            if (header[9] == 0)
                yield return (Encoding.ASCII.GetString(header, 0, 4), data);
        }
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes) =>
        ((bytes[0] & 127) << 21) | ((bytes[1] & 127) << 14) | ((bytes[2] & 127) << 7) | (bytes[3] & 127);

    private static string DecodeText(byte[] frame)
    {
        Encoding encoding = frame[0] switch
        {
            1 when frame.Length > 2 && frame[1] == 0xfe && frame[2] == 0xff => Encoding.BigEndianUnicode,
            1 => Encoding.Unicode,
            2 => Encoding.BigEndianUnicode,
            3 => Encoding.UTF8,
            _ => Encoding.Latin1,
        };
        return encoding.GetString(frame, 1, frame.Length - 1).Trim('\0', '\uFEFF', ' ');
    }
}
