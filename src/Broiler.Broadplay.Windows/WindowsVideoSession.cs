// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Native.Windows;

namespace Broiler.Broadplay;

internal sealed partial class WindowsVideoSession : IBroadplayMediaSession
{
    private readonly nint _hwnd;
    private readonly Func<double> _scale;
    private readonly Action _destroy;
    private readonly Callback _callback;
    private readonly Player _player;
    private readonly WindowsVideoAudioVolume _mixer = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _commands = Task.CompletedTask;
    private volatile BroadplayPlaybackState _state = BroadplayPlaybackState.Loading;
    private volatile string? _error;
    private long _position;
    private int _sampling;
    private int _seekGeneration;
    private int _disposed;
    private double _volume = 1;
    private bool _muted;
    private BRect _viewport;
    private double _lastScale;
    private bool _visible;

    private WindowsVideoSession(nint hwnd, Func<double> scale, Action destroy, Player player, Callback callback,
        BroadplayMediaInfo info)
    {
        _hwnd = hwnd;
        _scale = scale;
        _destroy = destroy;
        _player = player;
        _callback = callback;
        Info = info;
        _state = BroadplayPlaybackState.Ready;
        callback.Changed = OnEvent;
    }

    public BroadplayMediaInfo Info { get; }
    public BroadplayPlaybackState State => _state;
    public string? ErrorMessage => _error;
    public bool ReportsStateChanges => true;
    internal Task Released => _released.Task;
    public event EventHandler? StateChanged;

    public TimeSpan Position
    {
        get
        {
            if (_state == BroadplayPlaybackState.Playing && Interlocked.Exchange(ref _sampling, 1) == 0)
                Queue(() => { try { SamplePosition(); return 0; } finally { Volatile.Write(ref _sampling, 0); } });
            return TimeSpan.FromTicks(Interlocked.Read(ref _position));
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            UpdateVolume();
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            UpdateVolume();
        }
    }

    private void UpdateVolume()
    {
        bool muted = _muted;
        float volume = (float)_volume;
        Queue(() => ApplyVolume(volume, muted));
    }

    private int ApplyVolume(float volume, bool muted)
    {
        if (volume == 1 && !muted)
        {
            _mixer.Restore();
        }
        else if (_mixer.Apply(volume, muted))
        {
            // Clear any fallback attenuation from before the audio session existed.
            int reset = _player.SetMute(0);
            return reset < 0 ? reset : _player.SetVolume(1);
        }

        int result = _player.SetMute(muted ? 1 : 0);
        return result < 0 ? result : _player.SetVolume(muted ? 0 : volume);
    }

    public void Play() => Queue(() =>
    {
        if (_state == BroadplayPlaybackState.Ended)
        {
            int seek = _player.SetPosition(Guid.Empty, new TickValue { Type = 20, Value = 0 });
            if (seek < 0)
                return seek;
        }
        return _player.Play();
    });
    public void Pause() => Queue(() => _player.Pause());
    public void Stop()
    {
        Interlocked.Increment(ref _seekGeneration);
        Queue(() => _player.Stop());
    }

    public void Seek(TimeSpan position)
    {
        long ticks = Math.Clamp(position.Ticks, 0, Info.Duration.Ticks);
        int generation = Interlocked.Increment(ref _seekGeneration);
        Queue(() => generation == Volatile.Read(ref _seekGeneration)
            ? _player.SetPosition(Guid.Empty, new TickValue { Type = 20, Value = ticks })
            : 0);
    }

    public void SetVideoViewport(BRect clientRect, bool visible)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        double scale = _scale();
        bool show = visible && clientRect.Width >= 2 && clientRect.Height >= 2;
        if (!show)
        {
            if (_visible)
                _ = WindowNative.ShowWindow(_hwnd, 0);
            _visible = false;
            return;
        }
        if (_visible && _viewport == clientRect && _lastScale == scale)
            return;
        _visible = true;
        _viewport = clientRect;
        _lastScale = scale;
        _ = WindowNative.SetWindowPos(_hwnd, 0, (int)Math.Round(clientRect.X * scale), (int)Math.Round(clientRect.Y * scale),
            (int)Math.Round(clientRect.Width * scale), (int)Math.Round(clientRect.Height * scale), 0x0010);
        _ = WindowNative.ShowWindow(_hwnd, 4);
        Queue(() => _player.UpdateVideo());
    }

    public static async Task<BroadplayOpenResult> OpenAsync(string path, nint hwnd, Func<double> scale,
        Action destroy, CancellationToken cancellation)
    {
        var callback = new Callback();
        Player? player = null;
        try
        {
            player = await Task.Run(() =>
            {
                ThrowIfFailed(MFPCreateMediaPlayer(path, 0, 1, callback, hwnd, out Player result));
                return result;
            }, cancellation).ConfigureAwait(false);
            await callback.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation).ConfigureAwait(false);
            ThrowIfFailed(player.GetNativeVideoSize(out VideoSize native, out VideoSize display));
            long duration = player.GetDuration(Guid.Empty, out TickValue ticks) >= 0 ? ticks.Value : 0;
            var info = new BroadplayMediaInfo(BroadplayMediaKind.Video, BroadplayMediaFormats.DisplayNameFor(path, BroadplayMediaKind.Video),
                TimeSpan.FromTicks(Math.Max(0, duration)), VideoWidth: display.Width > 0 ? display.Width : native.Width,
                VideoHeight: display.Height > 0 ? display.Height : native.Height);
            var session = new WindowsVideoSession(hwnd, scale, destroy, player, callback, info);
            player = null;
            return new(session, null);
        }
        catch (Exception error) when (error is COMException or TimeoutException)
        {
            return new(null, "Windows could not present this video. " + error.Message);
        }
        finally
        {
            if (player is not null)
                Shutdown(player);
        }
    }

    private void OnEvent(int kind, int result)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (result < 0 || kind == 10)
        {
            _error = $"Video playback failed (0x{result:X8}). Reopen the file or choose another.";
            _state = BroadplayPlaybackState.Failed;
        }
        else
        {
            _state = kind switch
            {
                0 => BroadplayPlaybackState.Playing,
                1 => BroadplayPlaybackState.Paused,
                2 => BroadplayPlaybackState.Stopped,
                11 => BroadplayPlaybackState.Ended,
                _ => _state,
            };
            if (kind == 2)
                Interlocked.Exchange(ref _position, 0);
            if (kind == 0 && (_muted || _volume != 1))
                UpdateVolume();
            if (kind is 1 or 3 or 11)
                Queue(() => { SamplePosition(); StateChanged?.Invoke(this, EventArgs.Empty); return 0; });
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SamplePosition()
    {
        if (_player.GetPosition(Guid.Empty, out TickValue ticks) >= 0)
            Interlocked.Exchange(ref _position, Math.Max(0, ticks.Value));
    }

    private void Queue(Func<int> command)
    {
        lock (_gate)
        {
            if (_disposed != 0)
                return;
            Task prior = _commands;
            _commands = Task.Run(async () =>
            {
                await prior.ConfigureAwait(false);
                if (_disposed != 0)
                    return;
                try
                {
                    int result = command();
                    if (result < 0)
                        OnEvent(10, result);
                }
                catch (COMException ex)
                {
                    OnEvent(10, ex.HResult);
                }
            });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _callback.Changed = null;
            _ = ShowWindowAsync(_hwnd, 0);
            Task prior = _commands;
            _ = Task.Run(async () =>
            {
                try { await prior.ConfigureAwait(false); }
                finally
                {
                    try { _mixer.Restore(); }
                    finally
                    {
                        try { Shutdown(_player); }
                        finally
                        {
                            try { _destroy(); }
                            finally { _released.TrySetResult(); }
                        }
                    }
                }
            });
        }
    }

    private static void Shutdown(Player player)
    {
        try { _ = player.Shutdown(); }
        finally { ComNative.ReleaseComObject(player); }
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
            throw new COMException(null, hr);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint hwnd, int command);

    [LibraryImport("mfplay.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFPCreateMediaPlayer(string path, int autoplay, int options,
        PlayerCallback callback, nint hwnd, out Player player);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct TickValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Value;
    }

    internal readonly record struct VideoSize(int Width, int Height);

    [GeneratedComInterface, Guid("766C8FFB-5FDB-4FEA-A28D-B912996F51BD")]
    internal partial interface PlayerCallback
    {
        [PreserveSig] void OnMediaPlayerEvent(nint header);
    }

    [GeneratedComClass]
    private sealed partial class Callback : PlayerCallback
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<int, int>? Changed;
        public void OnMediaPlayerEvent(nint header)
        {
            int kind = Marshal.ReadInt32(header);
            int result = Marshal.ReadInt32(header, sizeof(int));
            if (result < 0)
                Ready.TrySetException(new COMException(null, result));
            else if (kind == 6)
                Ready.TrySetResult();
            Changed?.Invoke(kind, result);
        }
    }

    // IMFPMediaPlayer slot order follows the Windows SDK's mfplay.h.
    [GeneratedComInterface, Guid("A714590A-58AF-430A-85BF-44F5EC838D85")]
    internal partial interface Player
    {
        [PreserveSig] int Play();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int FrameStep();
        [PreserveSig] int SetPosition(in Guid type, in TickValue value);
        [PreserveSig] int GetPosition(in Guid type, out TickValue value);
        [PreserveSig] int GetDuration(in Guid type, out TickValue value);
        [PreserveSig] int SetRate(float value);
        [PreserveSig] int GetRate(out float value);
        [PreserveSig] int GetSupportedRates(int forward, out float slowest, out float fastest);
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int CreateMediaItemFromURL(nint path, int sync, nuint userData, out nint item);
        [PreserveSig] int CreateMediaItemFromObject(nint source, int sync, nuint userData, out nint item);
        [PreserveSig] int SetMediaItem(nint item);
        [PreserveSig] int ClearMediaItem();
        [PreserveSig] int GetMediaItem(out nint item);
        [PreserveSig] int GetVolume(out float volume);
        [PreserveSig] int SetVolume(float volume);
        [PreserveSig] int GetBalance(out float balance);
        [PreserveSig] int SetBalance(float balance);
        [PreserveSig] int GetMute(out int muted);
        [PreserveSig] int SetMute(int muted);
        [PreserveSig] int GetNativeVideoSize(out VideoSize native, out VideoSize aspect);
        [PreserveSig] int GetIdealVideoSize(out VideoSize min, out VideoSize max);
        [PreserveSig] int SetVideoSourceRect(nint rect);
        [PreserveSig] int GetVideoSourceRect(nint rect);
        [PreserveSig] int SetAspectRatioMode(uint mode);
        [PreserveSig] int GetAspectRatioMode(out uint mode);
        [PreserveSig] int GetVideoWindow(out nint window);
        [PreserveSig] int UpdateVideo();
        [PreserveSig] int SetBorderColor(uint color);
        [PreserveSig] int GetBorderColor(out uint color);
        [PreserveSig] int InsertEffect(nint effect, int optional);
        [PreserveSig] int RemoveEffect(nint effect);
        [PreserveSig] int RemoveAllEffects();
        [PreserveSig] int Shutdown();
    }
}
