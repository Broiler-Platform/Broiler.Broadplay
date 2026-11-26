// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Native.Windows;
using Broiler.Native.Windows.MediaFoundation;
using Broiler.Native.Windows.MediaFoundation.MediaEngine;

namespace Broiler.Broadplay;

/// <summary>
/// Audio-only Windows Media Foundation session built directly on <see cref="IMFMediaEngine"/>
/// with <c>MF_MEDIA_ENGINE_AUDIOONLY</c>, avoiding the video-size query that fails with
/// <c>E_FAIL (0x80004005)</c> on audio-only formats such as MP3, WAV, M4A, FLAC, and WMA.
/// </summary>
[SupportedOSPlatform("windows7.0")]
internal sealed partial class WindowsAudioMediaSession : IBroadplayMediaSession
{
    private const uint MfMediaEngineAudioOnly = 0x1;

    private const uint EventError = 5;
    private const uint EventPlay = 8;
    private const uint EventPause = 9;
    private const uint EventLoadedMetadata = 10;
    private const uint EventLoadedData = 11;
    private const uint EventCanPlay = 12;
    private const uint EventPlaying = 13;
    private const uint EventTimeUpdate = 18;
    private const uint EventEnded = 19;
    private const uint EventDurationChange = 21;
    private const uint EventResourceLost = 1012;
    private const uint EventStreamRenderingError = 1014;

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly string? _tempFilePath;

    private IMFMediaEngine? _engine;
    private object? _factoryObject;
    private object? _attributesObject;
    private AudioEngineNotify? _notify;
    private volatile BroadplayMediaInfo _info;
    private long _positionTicks;
    private int _seekGeneration;
    private volatile BroadplayPlaybackState _state = BroadplayPlaybackState.Loading;
    private volatile string? _errorMessage;
    private double _volume = 1.0;
    private bool _isMuted;
    private int _disposed;

    private WindowsAudioMediaSession(BroadplayMediaInfo initialInfo, string? tempFilePath)
    {
        _info = initialInfo;
        _tempFilePath = tempFilePath;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Broiler.Broadplay.AudioEngine",
        };
    }

    public BroadplayMediaInfo Info => _info;

    public BroadplayPlaybackState State => Volatile.Read(ref _disposed) != 0 ? BroadplayPlaybackState.Stopped : _state;

    public TimeSpan Position => TimeSpan.FromTicks(Interlocked.Read(ref _positionTicks));
    public bool ReportsStateChanges => true;
    public event EventHandler? StateChanged;

    private void SetState(BroadplayPlaybackState state)
    {
        if (_state == state)
            return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReadPositionOnEngineThread()
    {
        double seconds = _engine?.GetCurrentTime() ?? 0;
        long ticks = double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds).Ticks : 0;
        Interlocked.Exchange(ref _positionTicks, ticks);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            _volume = clamped;
            Post(() => _engine?.SetVolume(clamped));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            Post(() => _engine?.SetMuted(value ? 1 : 0));
        }
    }

    public string? ErrorMessage => _errorMessage;

    public static bool TryOpen(
        string fullPath,
        BroadplayMediaInfo? waveInfo,
        string? tempFilePath,
        out IBroadplayMediaSession? session,
        out string? error,
        CancellationToken cancellationToken = default)
    {
        session = null;
        error = null;

        string sourceUri = new Uri(Path.GetFullPath(fullPath)).AbsoluteUri;
        var initialInfo = waveInfo is not null
            ? waveInfo with { HasLivePlayback = true }
            : new BroadplayMediaInfo(
                Kind: BroadplayMediaKind.Audio,
                FormatName: BroadplayMediaFormats.DisplayNameFor(fullPath, BroadplayMediaKind.Audio),
                Duration: TimeSpan.Zero,
                HasLivePlayback: true);

        var candidate = new WindowsAudioMediaSession(initialInfo, tempFilePath);
        try
        {
            candidate.StartAndLoad(sourceUri, TimeSpan.FromSeconds(10), cancellationToken);
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
            if (_engine is null)
                return;

            if (_state == BroadplayPlaybackState.Ended)
            {
                _ = _engine.SetCurrentTime(0);
                Interlocked.Exchange(ref _positionTicks, 0);
            }

            int hr = _engine.Play();
            if (hr < 0)
            {
                _errorMessage = FormatHResult("Audio play failed", hr);
                SetState(BroadplayPlaybackState.Failed);
            }
            else
            {
                SetState(BroadplayPlaybackState.Playing);
            }
        });
    }

    public void Pause()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Post(() =>
        {
            if (_engine is null)
                return;

            int hr = _engine.Pause();
            if (hr < 0)
            {
                _errorMessage = FormatHResult("Audio pause failed", hr);
                SetState(BroadplayPlaybackState.Failed);
            }
            else
            {
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
            if (_engine is null)
                return;

            _ = _engine.Pause();
            _ = _engine.SetCurrentTime(0);
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
            if (_engine is null || generation != Volatile.Read(ref _seekGeneration))
                return;
            BroadplayPlaybackState previous = _state;
            SetState(BroadplayPlaybackState.Seeking);
            int hr = _engine.SetCurrentTime(clamped.TotalSeconds);
            if (hr < 0)
            {
                _errorMessage = FormatHResult("Audio seek failed", hr);
                SetState(BroadplayPlaybackState.Failed);
                return;
            }
            Interlocked.Exchange(ref _positionTicks, clamped.Ticks);
            SetState(previous == BroadplayPlaybackState.Ended ? BroadplayPlaybackState.Paused : previous);
        });
    }

    public void SetVideoViewport(BRect clientRect, bool visible)
    {
        // Audio-only session has no video surface.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }

    }

    private void StartAndLoad(string sourceUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread.Start(ready);
        ready.Task.WaitAsync(timeout, cancellationToken).GetAwaiter().GetResult();

        Post(() =>
        {
            try
            {
                if (_notify is null || _engine is null)
                    throw new InvalidOperationException("Media Foundation audio engine is not initialized.");

                _notify.EventReceived += kind => Post(() => ApplyEngineEvent(kind, loaded));
                ThrowIfFailed(_engine.SetSource(sourceUri), "Failed to set audio source");
                ThrowIfFailed(_engine.Load(), "Failed to load audio source");
            }
            catch (Exception error)
            {
                loaded.TrySetException(error);
            }
        });

        loaded.Task.WaitAsync(timeout, cancellationToken).GetAwaiter().GetResult();
    }

    private void ApplyEngineEvent(uint kind, TaskCompletionSource<bool> loaded)
    {
        switch (kind)
        {
            case EventLoadedMetadata:
            case EventDurationChange:
            case EventLoadedData:
            case EventCanPlay:
                TimeSpan duration = ReadDurationUnsafe();
                if (duration > TimeSpan.Zero && duration != _info.Duration)
                    _info = _info with { Duration = duration };
                if (_state == BroadplayPlaybackState.Loading && kind is EventLoadedData or EventCanPlay)
                {
                    if (_engine?.HasAudio() == 0)
                    {
                        Fail("No decodable audio stream was found. Check the installed codecs or choose another file.");
                        return;
                    }
                    SetState(BroadplayPlaybackState.Ready);
                    loaded.TrySetResult(true);
                }
                break;
            case EventPlaying:
            case EventPlay:
                if (_state != BroadplayPlaybackState.Stopped && _state != BroadplayPlaybackState.Failed)
                    SetState(BroadplayPlaybackState.Playing);
                break;
            case EventPause:
                if (_state == BroadplayPlaybackState.Playing)
                    SetState(BroadplayPlaybackState.Paused);
                break;
            case EventTimeUpdate:
                if (_state == BroadplayPlaybackState.Playing)
                    ReadPositionOnEngineThread();
                break;
            case EventEnded:
                if (_state is BroadplayPlaybackState.Playing or BroadplayPlaybackState.Paused &&
                    _engine?.IsEnded() != 0)
                {
                    ReadPositionOnEngineThread();
                    SetState(BroadplayPlaybackState.Ended);
                }
                break;
            case EventResourceLost:
            case EventStreamRenderingError:
                Fail("Audio output is unavailable. Check the output device, then reopen the file.");
                break;
            case EventError:
                Fail("Media Foundation could not play this audio. Check the installed codecs or choose another file.");
                break;
        }

        void Fail(string message)
        {
            _errorMessage = message;
            SetState(BroadplayPlaybackState.Failed);
            loaded.TrySetException(new InvalidOperationException(message));
        }
    }

    private void ThreadMain(object? state)
    {
        var ready = (TaskCompletionSource<bool>)state!;
        bool comInitialized = false;
        bool mfStarted = false;

        try
        {
            int comResult = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);
            if (comResult == ComNative.S_OK || comResult == ComNative.S_FALSE)
                comInitialized = true;
            else if (comResult != ComNative.RPC_E_CHANGED_MODE)
                ThrowIfFailed(comResult, "COM initialization failed");

            ThrowIfFailed(
                MediaFoundationPlatformNative.MFStartup(
                    MediaFoundationPlatformNative.MF_VERSION,
                    MediaFoundationPlatformNative.MFSTARTUP_NOSOCKET),
                "Media Foundation startup failed");
            mfStarted = true;

            CreateEngineOnThread();
            ready.TrySetResult(true);

            foreach (Action work in _queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
        finally
        {
            try { _engine?.Shutdown(); }
            catch { }

            ComNative.ReleaseComObject(_engine);
            ComNative.ReleaseComObject(_factoryObject);
            ComNative.ReleaseComObject(_attributesObject);
            _engine = null;
            _factoryObject = null;
            _attributesObject = null;
            _notify = null;

            if (mfStarted)
                _ = MediaFoundationPlatformNative.MFShutdown();
            if (comInitialized)
                ComNative.CoUninitialize();
            _queue.Dispose();
            if (_tempFilePath is not null)
            {
                try { File.Delete(_tempFilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void CreateEngineOnThread()
    {
        ThrowIfFailed(MediaFoundationPlatformNative.MFCreateAttributes(out IMFAttributes attributes, 4),
            "Media Foundation audio attribute creation failed");
        _attributesObject = attributes;

        var notify = new AudioEngineNotify();
        _notify = notify;

        Guid callbackGuid = MediaFoundationNative.MF_MEDIA_ENGINE_CALLBACK;
        IntPtr notifyPointer = ComWrappers.GetOrCreateComInterfaceForObject(notify, CreateComInterfaceFlags.None);
        try
        {
            ThrowIfFailed(attributes.SetUnknown(ref callbackGuid, notifyPointer),
                "Media Foundation audio callback configuration failed");
        }
        finally
        {
            ComNative.ReleaseIUnknown(notifyPointer);
        }

        Guid syncClose = MediaFoundationNative.MF_MEDIA_ENGINE_SYNCHRONOUS_CLOSE;
        ThrowIfFailed(attributes.SetUINT32(ref syncClose, 1),
            "Media Foundation audio synchronous close configuration failed");

        Guid browserMode = MediaFoundationNative.MF_MEDIA_ENGINE_BROWSER_COMPATIBILITY_MODE;
        Guid edgeMode = MediaFoundationNative.MF_MEDIA_ENGINE_BROWSER_COMPATIBILITY_MODE_IE_EDGE;
        ThrowIfFailed(attributes.SetGUID(ref browserMode, ref edgeMode),
            "Media Foundation browser compatibility configuration failed");

        Guid clsid = MediaFoundationNative.CLSID_MFMediaEngineClassFactory;
        Guid iid = MediaFoundationNative.IID_IMFMediaEngineClassFactory;
        ThrowIfFailed(
            ComNative.CoCreateInstance(in clsid, IntPtr.Zero, ComNative.CLSCTX_INPROC_SERVER, in iid, out IntPtr factoryPtr),
            "Media Foundation class factory creation failed");

        IMFMediaEngineClassFactory factory = ComNative.GetOrCreateComObject<IMFMediaEngineClassFactory>(factoryPtr)!;
        _factoryObject = factory;
        ComNative.ReleaseIUnknown(factoryPtr);

        uint flags = MediaFoundationNative.MF_MEDIA_ENGINE_DISABLE_LOCAL_PLUGINS | MfMediaEngineAudioOnly;
        ThrowIfFailed(factory.CreateInstance(flags, attributes, out IMFMediaEngine engine),
            "Media Foundation audio engine creation failed");

        _engine = engine;
        _ = engine.SetAutoPlay(0);
        _ = engine.SetMuted(0);
        _ = engine.SetVolume(1.0);
    }

    private TimeSpan ReadDurationUnsafe()
    {
        if (_engine is null)
            return TimeSpan.Zero;

        double seconds = _engine.GetDuration();
        return double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    private void Post(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ThrowIfFailed(int hresult, string message)
    {
        if (hresult < 0)
            throw new InvalidOperationException(FormatHResult(message, hresult));
    }

    private static string FormatHResult(string message, int hresult) =>
        $"{message} (HRESULT 0x{unchecked((uint)hresult):X8}).";

    [GeneratedComClass]
    private sealed partial class AudioEngineNotify : IMFMediaEngineNotify
    {
        public event Action<uint>? EventReceived;

        public int EventNotify(uint @event, UIntPtr param1, uint param2)
        {
            EventReceived?.Invoke(@event);
            return 0;
        }
    }
}
