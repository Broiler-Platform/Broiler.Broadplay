// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Media;
using Broiler.Media.Audio;
using Broiler.Media.Audio.Managed;
using Broiler.Media.Containers;

namespace Broiler.Broadplay;

/// <summary>Whether an opened media stream is audio-only or carries video.</summary>
public enum BroadplayMediaKind
{
    Audio,
    Video,
}

/// <summary>Transport state of an active media session.</summary>
public enum BroadplayPlaybackState
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Ended,
    Failed,
    Ready,
    Seeking,
}

/// <summary>The caller retains its byte buffer. The host owns any temporary materialization.</summary>
public abstract record BroadplayMediaSource(string Name, BroadplayMediaKind KindHint)
{
    public sealed record LocalFile(string Path, BroadplayMediaKind Hint) : BroadplayMediaSource(Path, Hint);
    public sealed record Bytes(string DisplayName, ReadOnlyMemory<byte> Content, BroadplayMediaKind Hint)
        : BroadplayMediaSource(DisplayName, Hint);
}

public readonly record struct BroadplayOpenResult(IBroadplayMediaSession? Session, string? Error);

public readonly record struct BroadplayPlaybackSnapshot(
    BroadplayPlaybackState State, TimeSpan Position, TimeSpan? Duration, bool CanSeek, bool CanSetVolume, string? Error);

/// <summary>Starts an open on the owning UI thread and returns when the candidate can be adopted.</summary>
public interface IBroadplayAsyncMediaHost : IBroadplayMediaHost
{
    Task<BroadplayOpenResult> OpenAsync(BroadplayMediaSource source, CancellationToken cancellationToken);
}

/// <summary>Metadata and optional audio waveform peaks for an opened media stream.</summary>
public sealed record BroadplayMediaInfo(
    BroadplayMediaKind Kind,
    string FormatName,
    TimeSpan Duration,
    int VideoWidth = 0,
    int VideoHeight = 0,
    int SampleRate = 0,
    int Channels = 0,
    int BitsPerSample = 0,
    float[]? WaveformPeaks = null,
    bool HasLivePlayback = true);

/// <summary>A live audio or video playback session owned by <see cref="BroadplayApp"/>.</summary>
public interface IBroadplayMediaSession : IDisposable
{
    event EventHandler? StateChanged { add { } remove { } }

    bool ReportsStateChanges => false;

    bool CanSeek => Info.HasLivePlayback && Info.Duration > TimeSpan.Zero;

    bool CanSetVolume => Info.HasLivePlayback;

    BroadplayPlaybackSnapshot GetSnapshot()
    {
        BroadplayMediaInfo info = Info;
        return new(State, Position, info.Duration > TimeSpan.Zero ? info.Duration : null, CanSeek, CanSetVolume, ErrorMessage);
    }

    BroadplayMediaInfo Info { get; }

    BroadplayPlaybackState State { get; }

    TimeSpan Position { get; }

    double Volume { get; set; }

    bool IsMuted { get; set; }

    string? ErrorMessage { get; }

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);

    /// <summary>
    /// Updates the client-space video presentation rectangle and whether the video surface
    /// should be visible (hidden when dialogs cover the window or when viewing another tab).
    /// Audio-only sessions ignore this call.
    /// </summary>
    void SetVideoViewport(BRect clientRect, bool visible);
}

/// <summary>
/// Platform media playback host supplied by a head (Windows Media Foundation, or the
/// platform-neutral managed fallback on heads without a native video/audio backend).
/// </summary>
public interface IBroadplayMediaHost : IDisposable
{
    bool SupportsAudio { get; }

    bool SupportsVideo { get; }

    bool TryOpen(string path, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error);

    bool TryOpenBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error);
}

internal enum BroadplayAudioAnalysisStatus
{
    Succeeded,
    UnsupportedFormat,
    InvalidData,
}

/// <summary>
/// Uses <c>Broiler.Media.Audio.Managed</c> and <c>Broiler.Media.Containers</c> to inspect
/// supported audio streams (WAVE, MP3, native FLAC, and Ogg FLAC), decode PCM, and compute
/// a compact peak-amplitude envelope for the audio view.
/// </summary>
internal static class BroadplayAudioAnalyzer
{
    private const int DefaultPeakBars = 96;
    private static readonly MediaCodecCatalog AudioCatalog = new(ManagedAudioCodecs.CreateCodecs());
    private static readonly MediaLimits AnalysisLimits = new(
        maxEncodedBytes: 256L * 1024 * 1024,
        maxDecodedBytes: 512L * 1024 * 1024,
        maxDecodedSamples: 48_000L * 60L * 60L * 2L);

    public static bool TryAnalyze(Stream stream, string name, out BroadplayMediaInfo? info, out string? error,
        bool includeWaveform = true) =>
        Analyze(stream, name, out info, out error, includeWaveform) == BroadplayAudioAnalysisStatus.Succeeded;

    public static BroadplayAudioAnalysisStatus Analyze(Stream stream, string name, out BroadplayMediaInfo? info,
        out string? error, bool includeWaveform = true)
    {
        info = null;
        error = null;

        try
        {
            AudioDecodeOptions options = new(
                outputSampleFormat: AudioSampleFormat.Float32Interleaved,
                limits: AnalysisLimits);
            MediaSourceHints hints = CreateHints(name);

            BroadplayAudioAnalysisStatus selectStatus = TrySelectAudioSource(
                stream, name, hints, options,
                out AudioCodec? directCodec,
                out IDemuxReader? demuxReader,
                out DemuxTrack? demuxTrack,
                out IAudioPacketDecoder? packetDecoder,
                out MediaContainerFormat containerFormat,
                out error);
            if (selectStatus != BroadplayAudioAnalysisStatus.Succeeded)
                return selectStatus;

            using (packetDecoder)
            {
                if (directCodec is not null)
                {
                    if (stream.CanSeek)
                        stream.Position = 0;

                    AudioStreamInfo streamInfo = directCodec.GetInfoAsync(new MediaInput(stream, hints), options)
                        .AsTask().GetAwaiter().GetResult();

                    float[]? peaks = null;
                    TimeSpan duration = streamInfo.Duration ?? TimeSpan.Zero;
                    if (includeWaveform && stream.CanSeek)
                    {
                        stream.Position = 0;
                        var collector = new PeakCollectorOutput(streamInfo.TotalFrames, DefaultPeakBars);
                        directCodec.DecodeAsync(new MediaInput(stream, hints), collector, options)
                            .AsTask().GetAwaiter().GetResult();
                        peaks = collector.GetPeaks();
                        if (duration <= TimeSpan.Zero && collector.DecodedFrames > 0 && streamInfo.SampleRate > 0)
                            duration = FramesToTime(collector.DecodedFrames, streamInfo.SampleRate);
                    }

                    info = new BroadplayMediaInfo(
                        Kind: BroadplayMediaKind.Audio,
                        FormatName: FormatNameForCodec(directCodec, name),
                        Duration: duration,
                        SampleRate: streamInfo.SampleRate,
                        Channels: streamInfo.Channels,
                        BitsPerSample: directCodec is Mp3AudioCodec ? 0 : streamInfo.BitsPerSample,
                        WaveformPeaks: peaks,
                        HasLivePlayback: true);
                    return BroadplayAudioAnalysisStatus.Succeeded;
                }

                if (demuxReader is not null && demuxTrack is not null && packetDecoder is not null)
                {
                    AudioStreamInfo streamInfo = packetDecoder.StreamInfo;
                    TimeSpan duration = streamInfo.Duration ?? TimeSpan.Zero;
                    float[]? peaks = null;
                    if (includeWaveform || duration <= TimeSpan.Zero)
                    {
                        var collector = new PeakCollectorOutput(streamInfo.TotalFrames, DefaultPeakBars);
                        DecodeDemuxedTrackAsync(demuxReader, demuxTrack, packetDecoder, collector, CancellationToken.None)
                            .GetAwaiter().GetResult();
                        if (includeWaveform)
                            peaks = collector.GetPeaks();
                        if (duration <= TimeSpan.Zero && collector.DecodedFrames > 0 && streamInfo.SampleRate > 0)
                            duration = FramesToTime(collector.DecodedFrames, streamInfo.SampleRate);
                    }

                    string formatName = containerFormat == MediaContainerFormat.Ogg &&
                        string.Equals(demuxTrack.CodecId, "flac", StringComparison.OrdinalIgnoreCase)
                        ? "Ogg FLAC Audio"
                        : BroadplayMediaFormats.DisplayNameFor(name, BroadplayMediaKind.Audio);

                    info = new BroadplayMediaInfo(
                        Kind: BroadplayMediaKind.Audio,
                        FormatName: formatName,
                        Duration: duration,
                        SampleRate: streamInfo.SampleRate,
                        Channels: streamInfo.Channels,
                        BitsPerSample: string.Equals(demuxTrack.CodecId, "mp3", StringComparison.OrdinalIgnoreCase) ? 0 : streamInfo.BitsPerSample,
                        WaveformPeaks: peaks,
                        HasLivePlayback: true);
                    return BroadplayAudioAnalysisStatus.Succeeded;
                }
            }

            return BroadplayAudioAnalysisStatus.UnsupportedFormat;
        }
        catch (MediaException ex) when (ex.Error.Code == MediaErrorCode.UnsupportedFormat)
        {
            error = ex.Message;
            return BroadplayAudioAnalysisStatus.UnsupportedFormat;
        }
        catch (MediaException ex)
        {
            error = ex.Message;
            return BroadplayAudioAnalysisStatus.InvalidData;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return BroadplayAudioAnalysisStatus.InvalidData;
        }
    }

    public static async Task DecodePcmAsync(Stream stream, string name, IAudioOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(output);

        AudioDecodeOptions options = new(
            outputSampleFormat: AudioSampleFormat.PcmS16Interleaved,
            limits: AnalysisLimits);
        MediaSourceHints hints = CreateHints(name);

        BroadplayAudioAnalysisStatus status = TrySelectAudioSource(
            stream, name, hints, options,
            out AudioCodec? directCodec,
            out IDemuxReader? demuxReader,
            out DemuxTrack? demuxTrack,
            out IAudioPacketDecoder? packetDecoder,
            out _,
            out string? error,
            cancellationToken);
        if (status != BroadplayAudioAnalysisStatus.Succeeded)
        {
            await output.FailAsync(new MediaError(
                status == BroadplayAudioAnalysisStatus.InvalidData ? MediaErrorCode.InvalidData : MediaErrorCode.UnsupportedFormat,
                error ?? "Unsupported audio stream."), cancellationToken).ConfigureAwait(false);
            throw new MediaException(new MediaError(
                status == BroadplayAudioAnalysisStatus.InvalidData ? MediaErrorCode.InvalidData : MediaErrorCode.UnsupportedFormat,
                error ?? "Unsupported audio stream."));
        }

        using (packetDecoder)
        {
            if (directCodec is not null)
            {
                if (stream.CanSeek)
                    stream.Position = 0;

                await directCodec.DecodeAsync(new MediaInput(stream, hints), output, options, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (demuxReader is not null && demuxTrack is not null && packetDecoder is not null)
            {
                await DecodeDemuxedTrackAsync(demuxReader, demuxTrack, packetDecoder, output, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static BroadplayAudioAnalysisStatus TrySelectAudioSource(
        Stream stream,
        string name,
        MediaSourceHints hints,
        AudioDecodeOptions options,
        out AudioCodec? directCodec,
        out IDemuxReader? demuxReader,
        out DemuxTrack? demuxTrack,
        out IAudioPacketDecoder? packetDecoder,
        out MediaContainerFormat containerFormat,
        out string? error,
        CancellationToken cancellationToken = default)
    {
        directCodec = null;
        demuxReader = null;
        demuxTrack = null;
        packetDecoder = null;
        containerFormat = MediaContainerFormat.Unknown;
        error = null;

        if (stream.CanSeek)
            stream.Position = 0;

        MediaCodecMatch? match = AudioCatalog.SelectAsync(
            MediaKind.Audio,
            new MediaInput(stream, hints),
            new MediaProbeOptions(AnalysisLimits),
            cancellationToken).AsTask().GetAwaiter().GetResult();

        if (match is null && stream.CanSeek)
        {
            stream.Position = 0;
            if (StartsWithId3(stream) && AudioCatalog.FindById(Mp3AudioCodec.CodecDescriptor.Id) is AudioCodec mp3Codec)
                match = new MediaCodecMatch(mp3Codec, MediaProbeResult.Match(MediaKind.Audio, MediaProbeConfidence.Low, "MP3", "audio/mpeg"));
        }

        if (match is { Codec: AudioCodec codec })
        {
            directCodec = codec;
            return BroadplayAudioAnalysisStatus.Succeeded;
        }

        if (stream.CanSeek)
            stream.Position = 0;

        containerFormat = MediaContainerProbe.DetectAsync(new MediaInput(stream, hints), AnalysisLimits, cancellationToken)
            .AsTask().GetAwaiter().GetResult();

        if (containerFormat is MediaContainerFormat.Ogg or MediaContainerFormat.Matroska or
            MediaContainerFormat.WebM or MediaContainerFormat.IsoBmff)
        {
            if (stream.CanSeek)
                stream.Position = 0;

            demuxReader = MediaDemuxer.OpenAsync(stream, containerFormat, AnalysisLimits, cancellationToken: cancellationToken)
                .AsTask().GetAwaiter().GetResult();

            if (demuxReader.Tracks.Any(t => t.Kind == MediaKind.Video))
            {
                error = "The stream contains video.";
                return BroadplayAudioAnalysisStatus.UnsupportedFormat;
            }

            foreach (DemuxTrack track in demuxReader.Tracks)
            {
                if (track.Kind != MediaKind.Audio)
                    continue;

                TrackDecoderSelection selection = MediaDemuxer.SelectDecoder(AudioCatalog, track);
                if (selection.Status == TrackDecoderStatus.Available && selection.Decoder is AudioCodec trackCodec)
                {
                    demuxTrack = track;
                    packetDecoder = trackCodec.CreatePacketDecoder(track.CodecId, track.CodecConfiguration, options);
                    return BroadplayAudioAnalysisStatus.Succeeded;
                }

                if (selection.Status == TrackDecoderStatus.UnsupportedConfiguration)
                {
                    error = selection.Diagnostic ?? "Unsupported audio track configuration.";
                    return BroadplayAudioAnalysisStatus.InvalidData;
                }
            }

            return BroadplayAudioAnalysisStatus.UnsupportedFormat;
        }

        string ext = Path.GetExtension(name);
        if (containerFormat == MediaContainerFormat.Unknown && IsManagedCodecExtension(ext))
        {
            error = $"The file is not a valid {BroadplayMediaFormats.DisplayNameFor(name, BroadplayMediaKind.Audio)} stream.";
            return BroadplayAudioAnalysisStatus.InvalidData;
        }

        return BroadplayAudioAnalysisStatus.UnsupportedFormat;
    }

    private static async Task DecodeDemuxedTrackAsync(
        IDemuxReader reader,
        DemuxTrack track,
        IAudioPacketDecoder decoder,
        IAudioOutput output,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false) is { } packet)
            {
                if (packet.TrackId != track.Id)
                    continue;

                TimeSpan? pts = packet.PresentationTimestamp is long ticks
                    ? TimeSpan.FromTicks((long)((Int128)ticks * track.TimeBase.Numerator * TimeSpan.TicksPerSecond / track.TimeBase.Denominator))
                    : null;
                await decoder.DecodePacketAsync(packet.Payload, pts, output, cancellationToken).ConfigureAwait(false);
            }

            await decoder.CompleteAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MediaException ex)
        {
            try { await output.FailAsync(ex.Error, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static MediaSourceHints CreateHints(string name)
    {
        string ext = Path.GetExtension(name);
        return new MediaSourceHints(
            mimeType: MimeTypeForExtension(ext),
            fileExtension: ext,
            displayName: Path.GetFileName(name));
    }

    private static string? MimeTypeForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".wav" or ".wave" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".ogg" or ".oga" => "audio/ogg",
        _ => null,
    };

    private static bool IsManagedCodecExtension(string extension) =>
        extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".wave", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);

    private static string FormatNameForCodec(AudioCodec codec, string name) => codec switch
    {
        WaveAudioCodec => "WAVE Audio",
        Mp3AudioCodec => "MP3 Audio",
        FlacAudioCodec => "FLAC Audio",
        _ => BroadplayMediaFormats.DisplayNameFor(name, BroadplayMediaKind.Audio),
    };

    private static bool StartsWithId3(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[3];
        int read = stream.ReadAtLeast(prefix, 3, throwOnEndOfStream: false);
        return read == 3 && prefix.SequenceEqual("ID3"u8);
    }

    private static TimeSpan FramesToTime(long frames, int sampleRate)
    {
        Int128 ticks = (Int128)frames * TimeSpan.TicksPerSecond / sampleRate;
        return ticks > long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)ticks);
    }

    private sealed class PeakCollectorOutput : IAudioOutput
    {
        private const int MaxDynamicBuckets = 4096;
        private readonly long? _totalFrames;
        private readonly int _barCount;
        private readonly float[]? _fixedPeaks;
        private readonly List<float>? _dynamicBuckets;
        private int _dynamicGroupSize = 1;
        private int _currentGroupFrames;
        private float _currentGroupPeak;
        private long _frameOffset;

        public PeakCollectorOutput(long? totalFrames, int barCount)
        {
            _barCount = Math.Max(1, barCount);
            if (totalFrames is > 0)
            {
                _totalFrames = totalFrames;
                _fixedPeaks = new float[_barCount];
            }
            else
            {
                _dynamicBuckets = [];
            }
        }

        public long DecodedFrames => _frameOffset;

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.SampleFormat != AudioSampleFormat.Float32Interleaved || buffer.FrameCount <= 0 || buffer.Channels <= 0)
                return ValueTask.CompletedTask;

            ReadOnlySpan<byte> bytes = buffer.Samples.Span;
            int channels = buffer.Channels;
            int frameStride = channels * 4;

            if (_fixedPeaks is not null && _totalFrames is long total)
            {
                for (int frame = 0; frame < buffer.FrameCount; frame++)
                {
                    int bucket = (int)Math.Min(_barCount - 1, ((_frameOffset + frame) * _barCount) / total);
                    float peak = ReadFramePeak(bytes, frame * frameStride, channels);
                    if (peak > _fixedPeaks[bucket])
                        _fixedPeaks[bucket] = Math.Min(1f, peak);
                }
            }
            else if (_dynamicBuckets is not null)
            {
                for (int frame = 0; frame < buffer.FrameCount; frame++)
                {
                    float peak = ReadFramePeak(bytes, frame * frameStride, channels);
                    if (peak > _currentGroupPeak)
                        _currentGroupPeak = peak;
                    if (++_currentGroupFrames >= _dynamicGroupSize)
                    {
                        _dynamicBuckets.Add(Math.Min(1f, _currentGroupPeak));
                        _currentGroupFrames = 0;
                        _currentGroupPeak = 0f;
                        if (_dynamicBuckets.Count >= MaxDynamicBuckets)
                            CompactDynamicBuckets();
                    }
                }
            }

            _frameOffset += buffer.FrameCount;
            return ValueTask.CompletedTask;
        }

        private void CompactDynamicBuckets()
        {
            if (_dynamicBuckets is null)
                return;
            int write = 0;
            for (int i = 0; i + 1 < _dynamicBuckets.Count; i += 2)
                _dynamicBuckets[write++] = Math.Max(_dynamicBuckets[i], _dynamicBuckets[i + 1]);
            if ((_dynamicBuckets.Count & 1) != 0)
                _dynamicBuckets[write++] = _dynamicBuckets[^1];
            _dynamicBuckets.RemoveRange(write, _dynamicBuckets.Count - write);
            _dynamicGroupSize *= 2;
        }

        private static float ReadFramePeak(ReadOnlySpan<byte> bytes, int offset, int channels)
        {
            float peak = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int sampleOffset = offset + (ch * 4);
                if (sampleOffset + 4 > bytes.Length)
                    break;
                float sample = Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(sampleOffset, 4)));
                if (sample > peak)
                    peak = sample;
            }
            return peak;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public float[] GetPeaks()
        {
            if (_fixedPeaks is not null)
                return _fixedPeaks;

            var result = new float[_barCount];
            if (_dynamicBuckets is null)
                return result;

            if (_currentGroupFrames > 0)
            {
                _dynamicBuckets.Add(Math.Min(1f, _currentGroupPeak));
                _currentGroupFrames = 0;
                _currentGroupPeak = 0f;
            }

            int count = _dynamicBuckets.Count;
            if (count == 0)
                return result;

            for (int i = 0; i < count; i++)
            {
                int bar = (int)Math.Min(_barCount - 1, ((long)i * _barCount) / count);
                if (_dynamicBuckets[i] > result[bar])
                    result[bar] = _dynamicBuckets[i];
            }

            return result;
        }
    }
}

/// <summary>
/// Platform-neutral fallback media host. Uses <c>Broiler.Media.Audio.Managed</c> and
/// <c>Broiler.Media.Containers</c> to validate and analyze WAVE, MP3, FLAC, and Ogg FLAC
/// streams and provides a stopwatch-driven timeline session for heads or tests without a
/// native audio/video playback engine.
/// </summary>
public sealed class BroadplayManagedMediaHost : IBroadplayMediaHost
{
    public bool SupportsAudio => false;

    public bool SupportsVideo => false;

    public bool TryOpen(string path, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error)
    {
        session = null;
        error = null;

        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length == 0)
            {
                error = "The file is empty.";
                return false;
            }

            return TryCreateSession(stream, path, kind, out session, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryOpenBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error)
    {
        session = null;
        error = null;

        if (bytes.IsEmpty)
        {
            error = "The file is empty.";
            return false;
        }

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        return TryCreateSession(stream, name, kind, out session, out error);
    }

    private static bool TryCreateSession(Stream stream, string name, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error)
    {
        session = null;
        error = null;

        if (kind == BroadplayMediaKind.Audio)
        {
            BroadplayAudioAnalysisStatus status = BroadplayAudioAnalyzer.Analyze(
                stream, name, out BroadplayMediaInfo? analyzedInfo, out error);
            if (status == BroadplayAudioAnalysisStatus.Succeeded && analyzedInfo is not null)
            {
                session = new ManagedTimelineMediaSession(analyzedInfo with { HasLivePlayback = false });
                return true;
            }

            if (status == BroadplayAudioAnalysisStatus.InvalidData)
                return false;
        }

        string formatName = BroadplayMediaFormats.DisplayNameFor(name, kind);
        var info = new BroadplayMediaInfo(
            Kind: kind,
            FormatName: formatName,
            Duration: TimeSpan.Zero,
            HasLivePlayback: false);
        session = new ManagedTimelineMediaSession(info);
        return true;
    }

    public void Dispose()
    {
    }

    internal sealed class ManagedTimelineMediaSession : IBroadplayMediaSession
    {
        private readonly Stopwatch _clock = new();
        private TimeSpan _basePosition = TimeSpan.Zero;
        private double _volume = 1.0;

        public ManagedTimelineMediaSession(BroadplayMediaInfo info)
        {
            Info = info;
            State = BroadplayPlaybackState.Paused;
        }

        public BroadplayMediaInfo Info { get; }

        public BroadplayPlaybackState State { get; private set; }

        public TimeSpan Position
        {
            get
            {
                TimeSpan current = _clock.IsRunning ? _basePosition + _clock.Elapsed : _basePosition;
                if (Info.Duration > TimeSpan.Zero && current >= Info.Duration)
                {
                    if (_clock.IsRunning)
                    {
                        _clock.Stop();
                        _basePosition = Info.Duration;
                        State = BroadplayPlaybackState.Ended;
                    }

                    return Info.Duration;
                }

                return current < TimeSpan.Zero ? TimeSpan.Zero : current;
            }
        }

        public double Volume
        {
            get => _volume;
            set => _volume = Math.Clamp(value, 0.0, 1.0);
        }

        public bool IsMuted { get; set; }

        public string? ErrorMessage => null;

        public void Play()
        {
            if (State == BroadplayPlaybackState.Ended)
                _basePosition = TimeSpan.Zero;
            _clock.Restart();
            State = BroadplayPlaybackState.Playing;
        }

        public void Pause()
        {
            if (_clock.IsRunning)
            {
                _basePosition = Position;
                _clock.Stop();
            }

            State = BroadplayPlaybackState.Paused;
        }

        public void Stop()
        {
            _clock.Reset();
            _basePosition = TimeSpan.Zero;
            State = BroadplayPlaybackState.Stopped;
        }

        public void Seek(TimeSpan position)
        {
            TimeSpan clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            if (Info.Duration > TimeSpan.Zero && clamped > Info.Duration)
                clamped = Info.Duration;

            _basePosition = clamped;
            if (_clock.IsRunning)
                _clock.Restart();
            if (State == BroadplayPlaybackState.Ended && clamped < Info.Duration)
                State = BroadplayPlaybackState.Paused;
        }

        public void SetVideoViewport(BRect clientRect, bool visible)
        {
        }

        public void Dispose()
        {
            _clock.Stop();
        }
    }
}
