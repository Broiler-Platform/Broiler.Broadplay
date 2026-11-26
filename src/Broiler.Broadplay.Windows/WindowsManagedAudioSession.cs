// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Media;
using Broiler.Media.Audio;

namespace Broiler.Broadplay;

/// <summary>
/// Live Windows audio playback session that decodes streams using <c>Broiler.Media</c>
/// managed decoders (WAVE, MP3, native FLAC, and Ogg FLAC) and streams 16-bit PCM to
/// <c>winmm.dll</c> <c>waveOut</c>.
/// </summary>
[SupportedOSPlatform("windows7.0")]
internal sealed partial class WindowsManagedAudioSession : IBroadplayMediaSession
{
    private const uint WaveMapper = unchecked((uint)-1);
    private const uint CallbackEvent = 0x00050000;
    private const ushort WaveFormatPcm = 1;
    private const uint WhdrDone = 0x00000001;
    private const uint WhdrPrepared = 0x00000002;
    private const uint TimeSamples = 0x0002;
    private const int BufferSlotCount = 6;

    private readonly string _path;
    private readonly string? _tempFilePath;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _commandEvent = new(false);
    private readonly AutoResetEvent _waveEvent = new(false);
    private readonly Thread _thread;

    private nint _waveOut;
    private readonly List<WaveBufferSlot> _inFlight = [];
    private BlockingCollection<AudioBuffer>? _pcmQueue;
    private CancellationTokenSource? _decodeCts;
    private Task? _decodeTask;
    private volatile string? _decodeError;
    private bool _decodeFinished;
    private long _startFrame;
    private long _completedFrames;
    private long _positionTicks;
    private volatile BroadplayMediaInfo _info;
    private volatile BroadplayPlaybackState _state = BroadplayPlaybackState.Ready;
    private volatile string? _errorMessage;
    private double _volume = 1.0;
    private bool _isMuted;
    private int _seekGeneration;
    private int _disposed;

    private WindowsManagedAudioSession(string path, BroadplayMediaInfo info, string? tempFilePath)
    {
        _path = path;
        _info = info with { HasLivePlayback = true };
        _tempFilePath = tempFilePath;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Broiler.Broadplay.ManagedAudio",
        };
    }

    public BroadplayMediaInfo Info => _info;

    public BroadplayPlaybackState State => Volatile.Read(ref _disposed) != 0 ? BroadplayPlaybackState.Stopped : _state;

    public TimeSpan Position => TimeSpan.FromTicks(Interlocked.Read(ref _positionTicks));

    public bool ReportsStateChanges => true;

    public event EventHandler? StateChanged;

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            _volume = clamped;
            Post(ApplyVolumeOnThread);
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            Post(ApplyVolumeOnThread);
        }
    }

    public string? ErrorMessage => _errorMessage;

    public static bool TryOpen(
        string fullPath,
        BroadplayMediaInfo info,
        string? tempFilePath,
        out IBroadplayMediaSession? session,
        out string? error,
        CancellationToken cancellationToken = default)
    {
        session = null;
        error = null;

        if (info.SampleRate <= 0 || info.Channels <= 0)
        {
            error = "Audio stream has invalid sample rate or channel count.";
            return false;
        }

        if (waveOutGetNumDevs() == 0)
        {
            error = "Audio output is unavailable. Check the output device, then reopen the file.";
            return false;
        }

        var candidate = new WindowsManagedAudioSession(fullPath, info, tempFilePath);
        try
        {
            candidate.StartAndVerifyOutput(TimeSpan.FromSeconds(5), cancellationToken);
            session = candidate;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            candidate.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            candidate.Dispose();
            return false;
        }
    }

    public void Play()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Post(() =>
        {
            if (_waveOut == 0)
                return;

            if (_state == BroadplayPlaybackState.Paused && _decodeTask is not null)
            {
                uint resumeResult = waveOutRestart(_waveOut);
                if (resumeResult != 0)
                {
                    Fail($"Audio resume failed (waveOut error {resumeResult}).");
                    return;
                }

                SetState(BroadplayPlaybackState.Playing);
                return;
            }

            if (_state == BroadplayPlaybackState.Ended)
            {
                _startFrame = 0;
                Interlocked.Exchange(ref _positionTicks, 0);
            }

            if (_state == BroadplayPlaybackState.Playing)
                return;

            StartDecodeFrom(_startFrame);
            SetState(BroadplayPlaybackState.Playing);
        });
    }

    public void Pause()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Post(() =>
        {
            if (_waveOut == 0)
                return;

            if (_state == BroadplayPlaybackState.Playing)
            {
                uint result = waveOutPause(_waveOut);
                if (result != 0)
                {
                    Fail($"Audio pause failed (waveOut error {result}).");
                    return;
                }

                UpdatePositionOnThread();
                SetState(BroadplayPlaybackState.Paused);
            }
        });
    }

    public void Stop()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Post(() =>
        {
            ResetPlaybackOnThread();
            _startFrame = 0;
            _completedFrames = 0;
            Interlocked.Exchange(ref _positionTicks, 0);
            SetState(BroadplayPlaybackState.Stopped);
        });
    }

    public void Seek(TimeSpan position)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        TimeSpan clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        TimeSpan duration = _info.Duration;
        if (duration > TimeSpan.Zero && clamped > duration)
            clamped = duration;

        int generation = Interlocked.Increment(ref _seekGeneration);
        Post(() =>
        {
            if (_waveOut == 0 || generation != Volatile.Read(ref _seekGeneration))
                return;

            BroadplayPlaybackState previous = _state;
            SetState(BroadplayPlaybackState.Seeking);

            ResetPlaybackOnThread();
            _startFrame = (long)Math.Round(clamped.TotalSeconds * _info.SampleRate);
            _completedFrames = 0;
            Interlocked.Exchange(ref _positionTicks, clamped.Ticks);

            if (previous == BroadplayPlaybackState.Playing)
            {
                StartDecodeFrom(_startFrame);
                SetState(BroadplayPlaybackState.Playing);
            }
            else
            {
                SetState(previous == BroadplayPlaybackState.Ended ? BroadplayPlaybackState.Paused : previous);
            }
        });
    }

    public void SetVideoViewport(BRect clientRect, bool visible)
    {
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { _commandEvent.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void StartAndVerifyOutput(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread.Start(ready);
        ready.Task.WaitAsync(timeout, cancellationToken).GetAwaiter().GetResult();
    }

    private void ThreadMain(object? state)
    {
        var ready = (TaskCompletionSource<bool>)state!;
        WaitHandle[] waitHandles = [_commandEvent, _waveEvent];

        try
        {
            OpenWaveOutOnThread();
            ApplyVolumeOnThread();
            ready.TrySetResult(true);

            while (Volatile.Read(ref _disposed) == 0)
            {
                while (_commands.TryDequeue(out Action? command))
                {
                    try
                    {
                        command();
                    }
                    catch (Exception ex)
                    {
                        Fail(ex.Message);
                    }
                }

                if (Volatile.Read(ref _disposed) != 0)
                    break;

                if (_state == BroadplayPlaybackState.Playing)
                    PumpAudioOnThread();

                int timeoutMs = _state == BroadplayPlaybackState.Playing ? 25 : Timeout.Infinite;
                _ = WaitHandle.WaitAny(waitHandles, timeoutMs);
            }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
        finally
        {
            ResetPlaybackOnThread();
            if (_waveOut != 0)
            {
                _ = waveOutClose(_waveOut);
                _waveOut = 0;
            }

            _commandEvent.Dispose();
            _waveEvent.Dispose();

            if (_tempFilePath is not null)
            {
                try { File.Delete(_tempFilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void OpenWaveOutOnThread()
    {
        int channels = _info.Channels;
        int sampleRate = _info.SampleRate;
        ushort blockAlign = checked((ushort)(channels * 2));
        var format = new WaveFormatEx
        {
            FormatTag = WaveFormatPcm,
            Channels = checked((ushort)channels),
            SamplesPerSec = checked((uint)sampleRate),
            AvgBytesPerSec = checked((uint)(sampleRate * blockAlign)),
            BlockAlign = blockAlign,
            BitsPerSample = 16,
            Size = 0,
        };

        uint result = waveOutOpen(
            out _waveOut,
            WaveMapper,
            in format,
            _waveEvent.SafeWaitHandle.DangerousGetHandle(),
            0,
            CallbackEvent);
        if (result != 0 || _waveOut == 0)
            throw new InvalidOperationException($"Audio output is unavailable (waveOut error {result}).");
    }

    private void ApplyVolumeOnThread()
    {
        if (_waveOut == 0)
            return;

        double effective = _isMuted ? 0.0 : _volume;
        ushort channelVolume = (ushort)Math.Clamp(Math.Round(effective * 0xFFFF), 0, 0xFFFF);
        uint packed = channelVolume | ((uint)channelVolume << 16);
        _ = waveOutSetVolume(_waveOut, packed);
    }

    private void StartDecodeFrom(long startFrame)
    {
        ResetDecodeOnThread();
        _completedFrames = 0;
        _decodeError = null;
        _decodeFinished = false;

        var queue = new BlockingCollection<AudioBuffer>(boundedCapacity: 16);
        var cts = new CancellationTokenSource();
        _pcmQueue = queue;
        _decodeCts = cts;

        CancellationToken token = cts.Token;
        _decodeTask = Task.Run(async () =>
        {
            try
            {
                await using FileStream stream = File.OpenRead(_path);
                var sink = new PcmQueueOutput(queue, startFrame);
                await BroadplayAudioAnalyzer.DecodePcmAsync(stream, _path, sink, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _decodeError = ex.Message;
            }
            finally
            {
                queue.CompleteAdding();
            }
        }, token);
    }

    private void PumpAudioOnThread()
    {
        if (_waveOut == 0)
            return;

        ReclaimDoneBuffersOnThread();

        if (_decodeError is { } error)
        {
            ResetPlaybackOnThread();
            Fail(error);
            return;
        }

        if (_pcmQueue is { } queue)
        {
            while (_inFlight.Count < BufferSlotCount && queue.TryTake(out AudioBuffer? buffer))
            {
                if (!SubmitBufferOnThread(buffer))
                    return;
            }

            if (queue.IsCompleted)
                _decodeFinished = true;
        }

        UpdatePositionOnThread();

        if (_decodeFinished && _inFlight.Count == 0)
        {
            long finalTicks = _info.Duration > TimeSpan.Zero
                ? _info.Duration.Ticks
                : FramesToTicks(_startFrame + _completedFrames, _info.SampleRate);
            Interlocked.Exchange(ref _positionTicks, finalTicks);
            SetState(BroadplayPlaybackState.Ended);
        }
    }

    private bool SubmitBufferOnThread(AudioBuffer buffer)
    {
        if (buffer.FrameCount <= 0 || buffer.Samples.IsEmpty)
            return true;

        byte[] data = buffer.Samples.ToArray();
        var slot = new WaveBufferSlot(data, buffer.FrameCount);
        uint prep = waveOutPrepareHeader(_waveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
        if (prep != 0)
        {
            slot.Dispose();
            Fail($"Audio buffer prepare failed (waveOut error {prep}).");
            return false;
        }

        uint write = waveOutWrite(_waveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
        if (write != 0)
        {
            _ = waveOutUnprepareHeader(_waveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
            slot.Dispose();
            Fail($"Audio write failed (waveOut error {write}).");
            return false;
        }

        _inFlight.Add(slot);
        return true;
    }

    private void ReclaimDoneBuffersOnThread()
    {
        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            WaveBufferSlot slot = _inFlight[i];
            if ((slot.ReadFlags() & WhdrDone) == 0)
                continue;

            _ = waveOutUnprepareHeader(_waveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
            _completedFrames += slot.FrameCount;
            slot.Dispose();
            _inFlight.RemoveAt(i);
        }
    }

    private void ResetPlaybackOnThread()
    {
        ResetDecodeOnThread();
        if (_waveOut != 0)
            _ = waveOutReset(_waveOut);

        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            WaveBufferSlot slot = _inFlight[i];
            if (_waveOut != 0)
                _ = waveOutUnprepareHeader(_waveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
            slot.Dispose();
        }
        _inFlight.Clear();
    }

    private void ResetDecodeOnThread()
    {
        CancellationTokenSource? cts = _decodeCts;
        Task? task = _decodeTask;
        BlockingCollection<AudioBuffer>? queue = _pcmQueue;

        _decodeCts = null;
        _decodeTask = null;
        _pcmQueue = null;
        _decodeFinished = false;
        _decodeError = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (queue is not null)
        {
            try
            {
                queue.CompleteAdding();
                while (queue.TryTake(out _)) { }
            }
            catch { }
        }

        if (task is not null)
        {
            try { _ = task.Wait(TimeSpan.FromSeconds(1)); } catch { }
        }

        queue?.Dispose();
        cts?.Dispose();
    }

    private void UpdatePositionOnThread()
    {
        if (_waveOut == 0 || _info.SampleRate <= 0)
            return;

        long playedFromReset = _completedFrames;
        var time = new MmTime { Type = TimeSamples };
        if (waveOutGetPosition(_waveOut, ref time, (uint)Marshal.SizeOf<MmTime>()) == 0 && time.Type == TimeSamples)
            playedFromReset = Math.Max(playedFromReset, time.Value);

        long totalTicks = FramesToTicks(_startFrame + playedFromReset, _info.SampleRate);
        if (_info.Duration > TimeSpan.Zero && totalTicks > _info.Duration.Ticks)
            totalTicks = _info.Duration.Ticks;
        Interlocked.Exchange(ref _positionTicks, totalTicks);
    }

    private void SetState(BroadplayPlaybackState state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string message)
    {
        _errorMessage = message;
        SetState(BroadplayPlaybackState.Failed);
    }

    private void Post(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _commands.Enqueue(action);
        try { _commandEvent.Set(); }
        catch (ObjectDisposedException) { }
    }

    private static long FramesToTicks(long frames, int sampleRate)
    {
        if (frames <= 0 || sampleRate <= 0)
            return 0;
        Int128 ticks = (Int128)frames * TimeSpan.TicksPerSecond / sampleRate;
        return ticks > long.MaxValue ? long.MaxValue : (long)ticks;
    }

    private sealed class PcmQueueOutput(BlockingCollection<AudioBuffer> queue, long startFrame) : IAudioOutput
    {
        private long _frameCursor;

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.FrameCount <= 0)
                return ValueTask.CompletedTask;

            long endFrame = _frameCursor + buffer.FrameCount;
            if (endFrame <= startFrame)
            {
                _frameCursor = endFrame;
                return ValueTask.CompletedTask;
            }

            AudioBuffer toEnqueue = buffer;
            if (_frameCursor < startFrame)
            {
                int skip = (int)(startFrame - _frameCursor);
                int remaining = buffer.FrameCount - skip;
                int byteOffset = skip * buffer.BytesPerFrame;
                TimeSpan start = buffer.Timestamp + TimeSpan.FromTicks(FramesToTicks(skip, buffer.SampleRate));
                TimeSpan dur = TimeSpan.FromTicks(FramesToTicks(remaining, buffer.SampleRate));
                toEnqueue = new AudioBuffer(
                    buffer.Samples[byteOffset..],
                    buffer.SampleFormat,
                    buffer.SampleRate,
                    buffer.Channels,
                    remaining,
                    start,
                    dur);
            }

            _frameCursor = endFrame;
            queue.Add(toEnqueue, cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            queue.CompleteAdding();
            return ValueTask.CompletedTask;
        }

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
        {
            queue.CompleteAdding();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WaveBufferSlot : IDisposable
    {
        private GCHandle _dataHandle;

        public WaveBufferSlot(byte[] data, int frameCount)
        {
            FrameCount = frameCount;
            _dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            int size = Marshal.SizeOf<WaveHdr>();
            HeaderPtr = Marshal.AllocHGlobal(size);
            var header = new WaveHdr
            {
                Data = _dataHandle.AddrOfPinnedObject(),
                BufferLength = (uint)data.Length,
            };
            Marshal.StructureToPtr(header, HeaderPtr, fDeleteOld: false);
        }

        public nint HeaderPtr { get; private set; }
        public int FrameCount { get; }

        public uint ReadFlags() => HeaderPtr == 0 ? WhdrDone : Marshal.PtrToStructure<WaveHdr>(HeaderPtr).Flags;

        public void Dispose()
        {
            if (HeaderPtr != 0)
            {
                Marshal.FreeHGlobal(HeaderPtr);
                HeaderPtr = 0;
            }

            if (_dataHandle.IsAllocated)
                _dataHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nuint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint Padding;
    }

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutGetNumDevs();

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutOpen(out nint hWaveOut, uint uDeviceID, in WaveFormatEx pwfx,
        nint dwCallback, nuint dwInstance, uint fdwOpen);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutClose(nint hWaveOut);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutPrepareHeader(nint hWaveOut, nint pwh, uint cbwh);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutUnprepareHeader(nint hWaveOut, nint pwh, uint cbwh);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutWrite(nint hWaveOut, nint pwh, uint cbwh);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutPause(nint hWaveOut);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutRestart(nint hWaveOut);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutReset(nint hWaveOut);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutSetVolume(nint hWaveOut, uint dwVolume);

    [LibraryImport("winmm.dll")]
    private static partial uint waveOutGetPosition(nint hWaveOut, ref MmTime pmmt, uint cbmmt);
}
