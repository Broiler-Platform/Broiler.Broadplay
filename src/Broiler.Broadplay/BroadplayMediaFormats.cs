// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Broiler.Media.Containers;
using Broiler.Media.Containers.Mp3;

namespace Broiler.Broadplay;

/// <summary>
/// Recognizes the audio and video files Broiler Broadplay opens, by leading bytes first
/// and file extension second.
/// </summary>
internal static class BroadplayMediaFormats
{
    public static readonly (string Extension, string DisplayName)[] AudioFormats =
    [
        (".wav", "WAVE Audio"),
        (".mp3", "MP3 Audio"),
        (".m4a", "MPEG-4 Audio"),
        (".aac", "AAC Audio"),
        (".flac", "FLAC Audio"),
        (".ogg", "Ogg Audio"),
        (".wma", "Windows Media Audio"),
    ];

    public static readonly (string Extension, string DisplayName)[] VideoFormats =
    [
        (".mp4", "MPEG-4 Video"),
        (".mov", "QuickTime Video"),
        (".webm", "WebM Video"),
        (".mkv", "Matroska Video"),
        (".avi", "AVI Video"),
        (".wmv", "Windows Media Video"),
    ];

    private static readonly string[] AllAudioExtensions =
    [
        ".wav", ".wave", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".oga", ".wma",
    ];

    private static readonly string[] AllVideoExtensions =
    [
        ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv",
    ];

    public static string FilterPattern(string extension) => extension switch
    {
        ".wav" => "*.wav;*.wave",
        ".ogg" => "*.ogg;*.oga",
        ".mp4" => "*.mp4;*.m4v",
        _ => "*" + extension,
    };

    public static string AllAudioFilterPattern =>
        string.Join(";", Array.ConvertAll(AllAudioExtensions, static ext => "*" + ext));

    public static string AllVideoFilterPattern =>
        string.Join(";", Array.ConvertAll(AllVideoExtensions, static ext => "*" + ext));

    public static bool HasAudioExtension(string path) => HasExtension(path, AllAudioExtensions);

    public static bool HasVideoExtension(string path) => HasExtension(path, AllVideoExtensions);

    public static bool HasMediaExtension(string path) => HasAudioExtension(path) || HasVideoExtension(path);

    /// <summary>
    /// Identifies an audio or video stream from its leading bytes, using the filename only
    /// to disambiguate container formats that carry either audio or video (such as ISO BMFF
    /// <c>ftyp</c>, EBML, or ASF).
    /// </summary>
    public static bool TryDetectKindFromSignature(ReadOnlySpan<byte> data, string? path, out BroadplayMediaKind kind)
    {
        // RIFF/WAVE
        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data[8..12].SequenceEqual("WAVE"u8))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        // RIFF/AVI
        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data[8..12].SequenceEqual("AVI "u8))
        {
            kind = BroadplayMediaKind.Video;
            return true;
        }

        // FLAC
        if (StartsWith(data, "fLaC"u8))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        // Ogg
        if (StartsWith(data, "OggS"u8))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        // MP3 with ID3 tag or MPEG-1/2/2.5 Layer III frame sync
        if (StartsWith(data, "ID3"u8) ||
            (data.Length >= Mp3FrameHeader.Size && Mp3FrameHeader.TryParse(data, out _)) ||
            (data.Length >= 2 && data[0] == 0xFF && (data[1] is 0xFB or 0xF3 or 0xF2 or 0xFA or 0xE3 or 0xE2)))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        // ADTS AAC frame sync
        if (data.Length >= 2 && data[0] == 0xFF && (data[1] is 0xF1 or 0xF9))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        // ISO BMFF (MP4 / M4V / M4A / MOV): bytes 4..8 == "ftyp"
        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8))
        {
            ReadOnlySpan<byte> brand = data[8..12];
            if (brand.SequenceEqual("M4A "u8) || brand.SequenceEqual("M4B "u8) ||
                (!string.IsNullOrEmpty(path) && HasAudioExtension(path)))
            {
                kind = BroadplayMediaKind.Audio;
                return true;
            }

            kind = BroadplayMediaKind.Video;
            return true;
        }

        // EBML (Matroska / WebM): 0x1A 0x45 0xDF 0xA3
        if (StartsWith(data, [0x1A, 0x45, 0xDF, 0xA3]))
        {
            kind = !string.IsNullOrEmpty(path) && HasAudioExtension(path)
                ? BroadplayMediaKind.Audio
                : BroadplayMediaKind.Video;
            return true;
        }

        // ASF (WMV / WMA): 30 26 B2 75 8E 66 CF 11
        if (StartsWith(data, [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]))
        {
            kind = !string.IsNullOrEmpty(path) && HasAudioExtension(path)
                ? BroadplayMediaKind.Audio
                : BroadplayMediaKind.Video;
            return true;
        }

        if (MediaContainerProbe.Detect(data) is MediaContainerFormat.Wave or MediaContainerFormat.Mp3 or
            MediaContainerFormat.Flac or MediaContainerFormat.Ogg)
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        kind = default;
        return false;
    }

    public static bool TryDetectKindFromExtension(string path, out BroadplayMediaKind kind)
    {
        if (HasAudioExtension(path))
        {
            kind = BroadplayMediaKind.Audio;
            return true;
        }

        if (HasVideoExtension(path))
        {
            kind = BroadplayMediaKind.Video;
            return true;
        }

        kind = default;
        return false;
    }

    public static string DisplayNameFor(string path, BroadplayMediaKind kind)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" or ".wave" => "WAVE Audio",
            ".mp3" => "MP3 Audio",
            ".m4a" => "MPEG-4 Audio",
            ".aac" => "AAC Audio",
            ".flac" => "FLAC Audio",
            ".ogg" or ".oga" => "Ogg Audio",
            ".wma" => "Windows Media Audio",
            ".mp4" or ".m4v" => "MPEG-4 Video",
            ".mov" => "QuickTime Video",
            ".webm" => "WebM Video",
            ".mkv" => "Matroska Video",
            ".avi" => "AVI Video",
            ".wmv" => "Windows Media Video",
            _ => kind == BroadplayMediaKind.Audio ? "Audio" : "Video",
        };
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        string extension = Path.GetExtension(path);
        foreach (string known in extensions)
        {
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
