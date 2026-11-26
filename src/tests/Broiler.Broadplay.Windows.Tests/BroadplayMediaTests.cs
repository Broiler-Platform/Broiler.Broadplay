// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using Broiler.Graphics.Geometry;
using Broiler.Native.Windows;
using Broiler.UI.FileDialog;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayMediaTests
{
    [Fact]
    public void MissingVideoSurfaceRefusesByteSource()
    {
        using var host = new WindowsMediaHost(() => 0, () => 1);
        string extension = "." + Guid.NewGuid().ToString("N");
        Assert.False(host.TryOpenBytes("clip" + extension, "not a stream"u8.ToArray(), BroadplayMediaKind.Video,
            out IBroadplayMediaSession? session, out string? error));
        Assert.Null(session);
        Assert.Contains("video surface", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), "broiler-broadplay-*" + extension));
    }

    [Theory]
    [InlineData("tone.wav", BroadplayMediaKind.Audio)]
    [InlineData("tone.mp3", BroadplayMediaKind.Audio)]
    [InlineData("extensionless-tone", BroadplayMediaKind.Audio)]
    [InlineData("audio-only.mp4", BroadplayMediaKind.Audio)]
    [InlineData("color-and-tone.mp4", BroadplayMediaKind.Video)]
    [InlineData("silent-color.mp4", BroadplayMediaKind.Video)]
    [InlineData("misnamed-video.m4a", BroadplayMediaKind.Video)]
    public void PreviewFixtureOpensInItsProbedView(string fixture, BroadplayMediaKind expected)
    {
        string? directory = Environment.GetEnvironmentVariable("BROADPLAY_PLAYBACK_FIXTURES");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory), "Generate the playback fixtures and set BROADPLAY_PLAYBACK_FIXTURES.");
        string path = Path.Combine(directory!, fixture);
        nint parent = WindowNative.CreateWindowEx(0, "STATIC", "Broadplay fixture", 0x80000000,
            0, 0, 320, 180, 0, 0, 0, 0);
        Assert.NotEqual(0, parent);
        try
        {
            using var host = new WindowsMediaHost(() => parent, () => 1);
            using FileStream input = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            int size = input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            Assert.True(BroadplayMediaFormats.TryDetectKindFromSignature(header[..size], path, out BroadplayMediaKind hint) ||
                BroadplayMediaFormats.TryDetectKindFromExtension(path, out hint));
            Assert.True(host.TryOpen(path, hint, out IBroadplayMediaSession? session, out string? error), error);
            using (session)
            {
                Assert.NotNull(session);
                Assert.Equal(expected, session.Info.Kind);
                Assert.True(session.Info.HasLivePlayback);
                Assert.InRange(session.Info.Duration.TotalSeconds, 3.8, 4.2);
            }
        }
        finally
        {
            _ = WindowNative.DestroyWindow(parent);
        }
    }

    [Fact]
    public void VideoPlaybackTracksSeekStopCompletionAndReplay()
    {
        string? directory = Environment.GetEnvironmentVariable("BROADPLAY_PLAYBACK_FIXTURES");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory), "Generate playback fixtures and set BROADPLAY_PLAYBACK_FIXTURES.");
        string path = Path.Combine(directory!, "silent-color.mp4");
        nint parent = WindowNative.CreateWindowEx(0, "STATIC", "Broadplay transport fixture", 0x80000000,
            0, 0, 320, 180, 0, 0, 0, 0);
        Assert.NotEqual(0, parent);
        try
        {
            using var host = new WindowsMediaHost(() => parent, () => 1);
            Assert.True(host.TryOpen(path, BroadplayMediaKind.Video, out var result, out string? error), error);
            using var session = Assert.IsType<WindowsVideoSession>(result);
            Assert.Equal(BroadplayPlaybackState.Ready, session.State);
            session.Seek(TimeSpan.FromSeconds(1.5));
            session.Seek(TimeSpan.FromSeconds(2.5));
            Wait(() => Math.Abs(session.Position.TotalSeconds - 2.5) < 0.3);
            session.Play();
            Wait(() => session.State == BroadplayPlaybackState.Playing);
            session.Stop();
            Wait(() => session.State == BroadplayPlaybackState.Stopped && session.Position == TimeSpan.Zero);
            session.Play();
            Wait(() => session.State == BroadplayPlaybackState.Playing);
            session.Seek(TimeSpan.FromSeconds(3.5));
            Wait(() => session.State == BroadplayPlaybackState.Ended);
            session.Play();
            Wait(() => session.State == BroadplayPlaybackState.Playing && session.Position < TimeSpan.FromSeconds(1));
            session.Dispose();
            Wait(() => session.Released.IsCompletedSuccessfully);

            void Wait(Func<bool> condition) =>
                Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)),
                    $"Actual state {session.State}, position {session.Position.TotalSeconds:F2}. {session.ErrorMessage}");
        }
        finally
        {
            _ = WindowNative.DestroyWindow(parent);
        }
    }

    [Fact]
    public void MediaFormats_RecognizeAudioAndVideoSignatures()
    {
        byte[] wav = CreateSineWave(sampleRate: 8000, channels: 1, frames: 800);
        Assert.True(BroadplayMediaFormats.TryDetectKindFromSignature(wav, "tone.bin", out BroadplayMediaKind wavKind));
        Assert.Equal(BroadplayMediaKind.Audio, wavKind);

        byte[] mp4 = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];
        Assert.True(BroadplayMediaFormats.TryDetectKindFromSignature(mp4, "clip.mp4", out BroadplayMediaKind mp4Kind));
        Assert.Equal(BroadplayMediaKind.Video, mp4Kind);

        byte[] m4a = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' '];
        Assert.True(BroadplayMediaFormats.TryDetectKindFromSignature(m4a, "song.m4a", out BroadplayMediaKind m4aKind));
        Assert.Equal(BroadplayMediaKind.Audio, m4aKind);

        byte[] flac = [(byte)'f', (byte)'L', (byte)'a', (byte)'C', 0x00, 0x00, 0x00, 0x22];
        Assert.True(BroadplayMediaFormats.TryDetectKindFromSignature(flac, "track.flac", out BroadplayMediaKind flacKind));
        Assert.Equal(BroadplayMediaKind.Audio, flacKind);
    }

    [Fact]
    public void OpenFilters_IncludeAudioAndVideoGroups()
    {
        UiFileDialogFilter[] filters = BroadplayFileFormats.CreateDefault().CreateOpenFilters();

        Assert.Contains(filters, f => f.Name == "Audio" && f.Pattern.Contains("*.wav", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filters, f => f.Name == "Video" && f.Pattern.Contains("*.mp4", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("*.wav", filters[0].Pattern, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mp4", filters[0].Pattern, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenWavBytes_SwitchesToAudioView_DecodesWaveMetadataWithoutOfferingPlayback()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        byte[] wav = CreateSineWave(sampleRate: 8000, channels: 2, frames: 8000);
        Assert.True(app.Open("sample.wav", wav));
        Assert.NotNull(app.MediaSession);
        Assert.Equal(BroadplayMediaKind.Audio, app.MediaSession!.Info.Kind);
        Assert.Equal(8000, app.MediaSession.Info.SampleRate);
        Assert.Equal(2, app.MediaSession.Info.Channels);
        Assert.Equal(16, app.MediaSession.Info.BitsPerSample);
        Assert.True(app.MediaSession.Info.Duration >= TimeSpan.FromMilliseconds(950));
        Assert.NotNull(app.MediaSession.Info.WaveformPeaks);
        Assert.Contains(app.MediaSession.Info.WaveformPeaks!, p => p > 0.2f);
        Assert.False(app.ActiveMediaView!.CanControlPlayback);
        Assert.False(app.Menu.CommandDispatcher!.TryExecute("playback.toggle"));
        app.ActiveMediaView.TogglePlayPause();
        Assert.Equal(BroadplayPlaybackState.Paused, app.MediaSession.State);
        Assert.False(app.WantsTick);

        _ = app.RenderFrame();
    }

    [Fact]
    public void OpenVideoBytes_SwitchesToVideoView_AndRendersCleanly()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        byte[] mp4Header = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0];
        Assert.True(app.Open("demo.mp4", mp4Header));
        Assert.NotNull(app.MediaSession);
        Assert.Equal(BroadplayMediaKind.Video, app.MediaSession!.Info.Kind);
        Assert.Equal("MPEG-4 Video", app.MediaSession.Info.FormatName);
        Assert.False(app.ActiveMediaView!.CanControlPlayback);

        _ = app.RenderFrame();
        Assert.True(app.ActiveMediaView!.VideoViewportBounds.Width > 0);
        Assert.True(app.ActiveMediaView.VideoViewportBounds.Height > 0);
    }

    [Fact]
    public void CorruptWav_IsRefused_WithoutReplacingCurrentView()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        // Truncated RIFF/WAVE header with no fmt/data chunk.
        byte[] brokenWav = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x24, 0x00, 0x00, 0x00, (byte)'W', (byte)'A', (byte)'V', (byte)'E'];
        Assert.False(app.Open("broken.wav", brokenWav));
        Assert.Null(app.MediaSession);
        Assert.Contains("Could not open broken.wav", app.LastAction, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenMp3Bytes_SwitchesToAudioView_DecodesMetadataAndWaveform()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        byte[] mp3 = LoadFixture("mpeg1-mono-long.mp3");
        Assert.True(app.Open("sample.mp3", mp3));
        Assert.NotNull(app.MediaSession);
        Assert.Equal(BroadplayMediaKind.Audio, app.MediaSession!.Info.Kind);
        Assert.Equal("MP3 Audio", app.MediaSession.Info.FormatName);
        Assert.Equal(44100, app.MediaSession.Info.SampleRate);
        Assert.Equal(1, app.MediaSession.Info.Channels);
        Assert.Equal(0, app.MediaSession.Info.BitsPerSample);
        Assert.True(app.MediaSession.Info.Duration >= TimeSpan.FromMilliseconds(100));
        Assert.NotNull(app.MediaSession.Info.WaveformPeaks);
        Assert.Contains(app.MediaSession.Info.WaveformPeaks!, p => p > 0.1f);
        Assert.False(app.ActiveMediaView!.CanControlPlayback);

        _ = app.RenderFrame();
    }

    [Fact]
    public void OpenFlacAndOggFlacBytes_DecodeMetadataAndWaveform()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        byte[] flac = LoadFixture("stereo.flac");
        Assert.True(app.Open("sample.flac", flac));
        Assert.NotNull(app.MediaSession);
        Assert.Equal(BroadplayMediaKind.Audio, app.MediaSession!.Info.Kind);
        Assert.Equal("FLAC Audio", app.MediaSession.Info.FormatName);
        Assert.Equal(48000, app.MediaSession.Info.SampleRate);
        Assert.Equal(2, app.MediaSession.Info.Channels);
        Assert.Equal(16, app.MediaSession.Info.BitsPerSample);
        Assert.True(app.MediaSession.Info.Duration >= TimeSpan.FromMilliseconds(120));
        Assert.NotNull(app.MediaSession.Info.WaveformPeaks);
        Assert.Contains(app.MediaSession.Info.WaveformPeaks!, p => p > 0.1f);

        byte[] oggFlac = LoadFixture("stereo-flac.ogg");
        Assert.True(app.Open("sample.ogg", oggFlac));
        Assert.NotNull(app.MediaSession);
        Assert.Equal(BroadplayMediaKind.Audio, app.MediaSession!.Info.Kind);
        Assert.Equal("Ogg FLAC Audio", app.MediaSession.Info.FormatName);
        Assert.Equal(48000, app.MediaSession.Info.SampleRate);
        Assert.Equal(2, app.MediaSession.Info.Channels);
        Assert.Equal(16, app.MediaSession.Info.BitsPerSample);
        Assert.True(app.MediaSession.Info.Duration >= TimeSpan.FromMilliseconds(120));
        Assert.NotNull(app.MediaSession.Info.WaveformPeaks);
        Assert.Contains(app.MediaSession.Info.WaveformPeaks!, p => p > 0.1f);
    }

    [Fact]
    public void CorruptMp3AndFlac_AreRefused_WithoutReplacingCurrentView()
    {
        using var host = new BroadplayUiHost(
            () => new BSize(1120, 780),
            () => 1.0,
            static () => { },
            static _ => { });
        using var app = new BroadplayApp(host, static () => { });

        byte[] brokenMp3 = [.. "ID3"u8, 3, 0, 0, 0, 0, 0, 0, .. "not-an-mp3"u8];
        Assert.False(app.Open("broken.mp3", brokenMp3));
        Assert.Null(app.MediaSession);
        Assert.Contains("Could not open broken.mp3", app.LastAction, StringComparison.Ordinal);

        byte[] brokenFlac = [.. "fLaC"u8, 0, 0, 0, 0];
        Assert.False(app.Open("broken.flac", brokenFlac));
        Assert.Null(app.MediaSession);
        Assert.Contains("Could not open broken.flac", app.LastAction, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMediaMetadata_ReadsMp3Id3WithDurationAndFlacVorbisComments()
    {
        string dir = Directory.CreateTempSubdirectory("broadplay-meta-").FullName;
        try
        {
            string flacPath = Path.Combine(dir, "track.flac");
            File.WriteAllBytes(flacPath, LoadFixture("stereo.flac"));
            LocalMediaMetadata flacMeta = LocalMediaMetadata.Read(flacPath);
            Assert.Equal("Broiler FLAC", flacMeta.Title);
            Assert.True(flacMeta.Duration >= TimeSpan.FromMilliseconds(120));

            byte[] mp3 = LoadFixture("mpeg1-mono-long.mp3");
            byte[] id3 = BuildId3v23Tag("Test Song", "Test Artist", "Test Album");
            byte[] taggedMp3 = new byte[id3.Length + mp3.Length];
            id3.CopyTo(taggedMp3, 0);
            mp3.CopyTo(taggedMp3, id3.Length);

            string mp3Path = Path.Combine(dir, "tagged.mp3");
            File.WriteAllBytes(mp3Path, taggedMp3);
            LocalMediaMetadata mp3Meta = LocalMediaMetadata.Read(mp3Path);
            Assert.Equal("Test Song", mp3Meta.Title);
            Assert.Equal("Test Artist", mp3Meta.Artist);
            Assert.Equal("Test Album", mp3Meta.Album);
            Assert.True(mp3Meta.Duration >= TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WindowsMediaHost_OpensMp3WithAnalyzedMetadataAndWaveform()
    {
        byte[] mp3 = LoadFixture("mpeg1-mono-long.mp3");
        string tempPath = Path.Combine(Path.GetTempPath(), "broiler-broadplay-mp3-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(tempPath, mp3);
        try
        {
            using var mediaHost = new WindowsMediaHost(() => IntPtr.Zero, () => 1.0);
            Assert.True(
                mediaHost.TryOpen(tempPath, BroadplayMediaKind.Audio, out IBroadplayMediaSession? session, out string? error),
                error);
            using (session)
            {
                Assert.NotNull(session);
                Assert.Equal(BroadplayMediaKind.Audio, session!.Info.Kind);
                Assert.Equal("MP3 Audio", session.Info.FormatName);
                Assert.True(session.Info.HasLivePlayback);
                Assert.Equal(44100, session.Info.SampleRate);
                Assert.Equal(1, session.Info.Channels);
                Assert.NotNull(session.Info.WaveformPeaks);
                Assert.Contains(session.Info.WaveformPeaks!, p => p > 0.1f);
                Assert.True(session.Info.Duration >= TimeSpan.FromMilliseconds(100));
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Theory]
    [InlineData("mpeg1-mono-long.mp3", 44100, 1)]
    [InlineData("stereo.flac", 48000, 2)]
    [InlineData("stereo-flac.ogg", 48000, 2)]
    public void WindowsManagedAudioSession_PlaysSeeksPausesAndStops(string fixtureName, int expectedRate, int expectedChannels)
    {
        byte[] data = LoadFixture(fixtureName);
        string ext = Path.GetExtension(fixtureName);
        string tempPath = Path.Combine(Path.GetTempPath(), "broiler-broadplay-managed-" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(tempPath, data);
        try
        {
            BroadplayMediaInfo? info;
            using (FileStream fs = File.OpenRead(tempPath))
            {
                Assert.True(BroadplayAudioAnalyzer.TryAnalyze(fs, tempPath, out info, out string? analyzeError), analyzeError);
            }
            Assert.NotNull(info);
            Assert.Equal(expectedRate, info!.SampleRate);
            Assert.Equal(expectedChannels, info.Channels);

            bool opened = WindowsManagedAudioSession.TryOpen(tempPath, info, tempFilePath: null,
                out IBroadplayMediaSession? session, out string? openError);
            Assert.SkipWhen(!opened && openError is not null && openError.Contains("unavailable", StringComparison.OrdinalIgnoreCase),
                openError ?? "No waveOut audio device available.");
            Assert.True(opened, openError);

            using (session)
            {
                Assert.NotNull(session);
                Assert.True(session!.Info.HasLivePlayback);
                Assert.Equal(BroadplayPlaybackState.Ready, session.State);
                session.Volume = 0.25;
                session.IsMuted = true;
                Assert.Equal(0.25, session.Volume);
                Assert.True(session.IsMuted);

                session.Seek(TimeSpan.FromMilliseconds(50));
                Assert.True(SpinWait.SpinUntil(() => session.Position >= TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(3)));

                session.Play();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Ended, TimeSpan.FromSeconds(5)),
                    $"Actual state: {session.State}. {session.ErrorMessage}");

                session.Play();
                Assert.True(SpinWait.SpinUntil(() => session.State is BroadplayPlaybackState.Playing or BroadplayPlaybackState.Ended, TimeSpan.FromSeconds(3)));
                session.Pause();
                Assert.True(SpinWait.SpinUntil(() => session.State is BroadplayPlaybackState.Paused or BroadplayPlaybackState.Ended, TimeSpan.FromSeconds(3)));
                session.Stop();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Stopped, TimeSpan.FromSeconds(3)));
                Assert.Equal(TimeSpan.Zero, session.Position);
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static byte[] LoadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static byte[] BuildId3v23Tag(string title, string artist, string album)
    {
        static byte[] Frame(string id, string text)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            byte[] frame = new byte[10 + 1 + utf8.Length];
            System.Text.Encoding.ASCII.GetBytes(id).CopyTo(frame, 0);
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), 1 + utf8.Length);
            frame[10] = 3; // UTF-8
            utf8.CopyTo(frame, 11);
            return frame;
        }

        byte[] f1 = Frame("TIT2", title);
        byte[] f2 = Frame("TPE1", artist);
        byte[] f3 = Frame("TALB", album);
        int bodySize = f1.Length + f2.Length + f3.Length;
        byte[] tag = new byte[10 + bodySize];
        "ID3"u8.CopyTo(tag);
        tag[3] = 3;
        tag[6] = (byte)((bodySize >> 21) & 0x7F);
        tag[7] = (byte)((bodySize >> 14) & 0x7F);
        tag[8] = (byte)((bodySize >> 7) & 0x7F);
        tag[9] = (byte)(bodySize & 0x7F);
        f1.CopyTo(tag, 10);
        f2.CopyTo(tag, 10 + f1.Length);
        f3.CopyTo(tag, 10 + f1.Length + f2.Length);
        return tag;
    }

    [Fact]
    public void WindowsMediaHost_OpensAudioOnlyStream_WithoutVideoSizeFailure()
    {
        byte[] wav = CreateSineWave(sampleRate: 44100, channels: 2, frames: 44100);
        string tempPath = Path.Combine(Path.GetTempPath(), "broiler-broadplay-test-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(tempPath, wav);
        try
        {
            using var mediaHost = new WindowsMediaHost(() => IntPtr.Zero, () => 1.0);
            Assert.True(
                mediaHost.TryOpen(tempPath, BroadplayMediaKind.Audio, out IBroadplayMediaSession? session, out string? error),
                error);
            using (session)
            {
                Assert.NotNull(session);
                Assert.Equal(BroadplayMediaKind.Audio, session!.Info.Kind);
                Assert.True(session.Info.HasLivePlayback);
                Assert.True(session.Info.Duration >= TimeSpan.FromMilliseconds(450));
                session.IsMuted = true;
                session.Volume = 0.5;
                Assert.True(session.IsMuted);
                Assert.Equal(0.5, session.Volume);
                session.Seek(TimeSpan.FromMilliseconds(750));
                Assert.True(SpinWait.SpinUntil(() => session.Position >= TimeSpan.FromMilliseconds(550), TimeSpan.FromSeconds(3)));
                session.Play();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Ended, TimeSpan.FromSeconds(4)),
                    $"Actual state: {session.State}. {session.ErrorMessage}");
                session.Play();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Playing, TimeSpan.FromSeconds(3)));
                session.Pause();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Paused, TimeSpan.FromSeconds(3)));
                session.Stop();
                Assert.True(SpinWait.SpinUntil(() => session.State == BroadplayPlaybackState.Stopped, TimeSpan.FromSeconds(3)));
                Assert.Equal(TimeSpan.Zero, session.Position);
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static byte[] CreateSineWave(int sampleRate, int channels, int frames)
    {
        const int bitsPerSample = 16;
        int blockAlign = channels * (bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataSize = frames * blockAlign;
        byte[] wav = new byte[44 + dataSize];

        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(36 + dataSize));
        "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
        "fmt "u8.CopyTo(wav.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), (uint)byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), (uint)dataSize);

        for (int i = 0; i < frames; i++)
        {
            short sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 16000);
            for (int c = 0; c < channels; c++)
            {
                int offset = 44 + (i * blockAlign) + (c * 2);
                BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(offset, 2), sample);
            }
        }

        return wav;
    }
}
