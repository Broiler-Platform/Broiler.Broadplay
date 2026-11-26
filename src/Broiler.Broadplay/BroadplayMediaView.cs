// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.UI;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Button.Standard;
using Broiler.UI.Slider.Standard;

namespace Broiler.Broadplay;

internal sealed class BroadplayMediaView : UiElement
{
    private const double ControlBarHeight = 82;
    private static readonly BColor ControlBarBackground = BColor.FromArgb(0xFF, 0x1E, 0x24, 0x2B);
    private static readonly BColor ControlBarBorder = BColor.FromArgb(0xFF, 0x3A, 0x44, 0x4F);
    private static readonly BColor GlyphColor = BColor.FromArgb(0xFF, 0xF0, 0xF4, 0xF8);
    private static readonly BColor WaveformUnplayed = BColor.FromArgb(0xFF, 0x52, 0x62, 0x73);
    private static readonly BColor SubtitleColor = BColor.FromArgb(0xFF, 0xB8, 0xC4, 0xD0);

    private readonly BroadplayMediaKind _kind;
    private readonly Action _stateChanged;
    private readonly StandardLabel _headline = new()
    {
        Font = new BFontStyle("Segoe UI", 18, BFontWeight.SemiBold),
        Foreground = GlyphColor,
        Trimming = UiTextTrimming.CharacterEllipsis,
    };
    private readonly StandardLabel _details = new()
    {
        Font = new BFontStyle("Segoe UI", 13),
        Foreground = SubtitleColor,
        Trimming = UiTextTrimming.CharacterEllipsis,
    };
    private readonly StandardLabel _timeLabel = new()
    {
        Font = new BFontStyle("Segoe UI", 12),
        Foreground = GlyphColor,
    };

    private IBroadplayMediaSession? _session;
    private string _mediaTitle = string.Empty;
    private BRect _stageRect;
    private readonly StandardButton _play = new() { Text = "Play", AccessibleName = "Play" };
    private readonly StandardButton _mute = new() { Text = "Mute", AccessibleName = "Mute" };
    private readonly StandardSlider _seek = new() { AccessibleName = "Playback position in seconds", StepFrequency = 0, SmallChange = 5 };
    private readonly StandardSlider _volume = new() { AccessibleName = "Volume percent", SmallChange = 5 };
    private bool _syncingControls;

    public BroadplayMediaView(BroadplayMediaKind kind, Action stateChanged)
    {
        _kind = kind;
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        Focusable = true;
        AddChild(_play);
        AddChild(_seek);
        AddChild(_mute);
        AddChild(_volume);
        _play.Clicked += (_, _) => TogglePlayPause();
        _mute.Clicked += (_, _) => ToggleMute();
        _seek.ValueChanged += (_, _) =>
        {
            if (!_syncingControls && CanControlPlayback && _session!.CanSeek)
                _session!.Seek(TimeSpan.FromSeconds(_seek.Value));
        };
        _volume.ValueChanged += (_, _) =>
        {
            if (!_syncingControls && CanControlPlayback && _session!.CanSetVolume)
            {
                _session!.Volume = _volume.Value / 100;
                NotifyChanged();
            }
        };
    }

    public bool CanControlPlayback => _session is { Info.HasLivePlayback: true,
        State: not (BroadplayPlaybackState.Loading or BroadplayPlaybackState.Failed) };

    public void FocusTransport()
    {
        if (CanControlPlayback)
            _play.Focus();
        else
            Focus();
    }
    public BroadplayMediaKind Kind => _kind;

    public IBroadplayMediaSession? MediaSession => _session;

    /// <summary>
    /// Client-space rectangle reserved for native video presentation (inside the stage,
    /// above the transport bar). Empty when the view has no room.
    /// </summary>
    public BRect VideoViewportBounds
    {
        get
        {
            if (_stageRect.Width <= 4 || _stageRect.Height <= 4)
                return BRect.Empty;
            double width = _stageRect.Width - 4;
            double height = _stageRect.Height - 4;
            if (_session?.Info is { VideoWidth: > 0, VideoHeight: > 0 } info)
            {
                double aspect = (double)info.VideoWidth / info.VideoHeight;
                width = Math.Min(width, height * aspect);
                height = Math.Min(height, width / aspect);
            }
            return new BRect(_stageRect.Left + (_stageRect.Width - width) / 2,
                _stageRect.Top + (_stageRect.Height - height) / 2, width, height);
        }
    }

    public void Attach(IBroadplayMediaSession? session, string title)
    {
        _session = session;
        _mediaTitle = title ?? string.Empty;
        SyncControls(session?.Position ?? TimeSpan.Zero, session?.Info.Duration ?? TimeSpan.Zero);
        Invalidate(UiInvalidationKind.Render);
    }

    public void TogglePlayPause()
    {
        if (!CanControlPlayback)
            return;

        if (_session!.State == BroadplayPlaybackState.Playing)
            _session!.Pause();
        else
            _session!.Play();

        NotifyChanged();
    }

    public void Stop()
    {
        if (!CanControlPlayback)
            return;

        _session!.Stop();
        NotifyChanged();
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (!CanControlPlayback || !_session!.CanSeek)
            return;

        _session!.Seek(_session.Position + delta);
        NotifyChanged();
    }

    public void ToggleMute()
    {
        if (!CanControlPlayback || !_session!.CanSetVolume)
            return;

        _session!.IsMuted = !_session!.IsMuted;
        NotifyChanged();
    }

    public void AdjustVolume(double delta)
    {
        if (!CanControlPlayback || !_session!.CanSetVolume)
            return;

        _session!.Volume = Math.Clamp(_session!.Volume + delta, 0.0, 1.0);
        if (_session!.Volume > 0 && _session!.IsMuted)
            _session!.IsMuted = false;
        NotifyChanged();
    }

    public static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        return value.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}", (int)value.TotalHours, value.Minutes, value.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", (int)value.TotalMinutes, value.Seconds);
    }

    protected override BSize MeasureCore(BSize availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : 760,
        double.IsFinite(availableSize.Height) ? availableSize.Height : 480);

    protected override void ArrangeCore(BRect rect)
    {
        double stageHeight = Math.Max(0, rect.Height - ControlBarHeight);
        _stageRect = new BRect(rect.Left, rect.Top, rect.Width, stageHeight);
        double left = rect.Left + 8;
        double width = Math.Max(0, rect.Width - 16);
        double buttonWidth = Math.Min(width * 0.33, 82);
        double top = _stageRect.Bottom + 4;
        Place(_play, new BRect(left, top, buttonWidth, 34));
        Place(_seek, new BRect(left + buttonWidth + 8, top, Math.Max(0, width - buttonWidth - 8), 34));
        Place(_mute, new BRect(left, top + 40, buttonWidth, 34));
        double volumeWidth = Math.Min(125, Math.Max(0, (width - buttonWidth - 16) * 0.55));
        Place(_volume, new BRect(left + buttonWidth + 8, top + 40, volumeWidth, 34));
        Place(_timeLabel, new BRect(left + buttonWidth + volumeWidth + 16, top + 40,
            Math.Max(0, width - buttonWidth - volumeWidth - 16), 34));
    }

    private static void Place(UiElement element, BRect rect)
    {
        element.Measure(rect.Size);
        element.Arrange(rect);
    }

    protected override bool OnInput(UiInputEvent input)
    {
        if (!CanControlPlayback || input.Kind != UiInputEventKind.KeyboardKey ||
            input.KeyTransition != KeyboardKeyTransition.Down || input.KeyModifiers != KeyboardModifierState.None)
            return false;

        // Child controls get the key first, so their own shortcuts stay local.
        if (input.NativeKeyCode == 0x20 || input.KeyName is "Space" or "Spacebar")
            TogglePlayPause();
        else if (input.NativeKeyCode == 0x53 || input.KeyName is "S" or "s")
            Stop();
        else if (input.NativeKeyCode == 0x4D || input.KeyName is "M" or "m")
            ToggleMute();
        else if (input.NativeKeyCode == 0x26 || input.KeyName is "Up" or "ArrowUp")
            AdjustVolume(0.1);
        else if (input.NativeKeyCode == 0x28 || input.KeyName is "Down" or "ArrowDown")
            AdjustVolume(-0.1);
        else if (!_session!.CanSeek)
            return false;
        else if (input.NativeKeyCode == 0x25 || input.KeyName is "Left" or "ArrowLeft")
            SeekRelative(TimeSpan.FromSeconds(-5));
        else if (input.NativeKeyCode == 0x27 || input.KeyName is "Right" or "ArrowRight")
            SeekRelative(TimeSpan.FromSeconds(5));
        else
            return false;

        return true;
    }

    protected override void RenderCore(UiRenderContext context)
    {
        context.RenderList.FillRect(Bounds, ControlBarBackground);
        context.RenderList.FillRect(_stageRect, BroadplayPalette.ImageMat);
        context.RenderList.StrokeRect(_stageRect, BroadplayPalette.ViewBorder, 1);
        if (_kind == BroadplayMediaKind.Audio)
            RenderAudioStage(context);
        else
            RenderVideoStage(context);

        BroadplayPlaybackSnapshot? snapshot = _session?.GetSnapshot();
        SyncControls(snapshot?.Position ?? TimeSpan.Zero, snapshot?.Duration ?? TimeSpan.Zero);
        _play.Render(context);
        _seek.Render(context);
        _mute.Render(context);
        _volume.Render(context);
        _timeLabel.Render(context);
    }

    private void SyncControls(TimeSpan position, TimeSpan duration)
    {
        _syncingControls = true;
        try
        {
            bool canPlay = CanControlPlayback;
            _play.IsEnabled = canPlay;
            _mute.IsEnabled = _volume.IsEnabled = canPlay && _session!.CanSetVolume;
            _seek.IsEnabled = canPlay && _session!.CanSeek;
            _play.Text = _play.AccessibleName = _session?.State == BroadplayPlaybackState.Playing ? "Pause" : "Play";
            _mute.Text = _mute.AccessibleName = _session?.IsMuted == true ? "Unmute" : "Mute";
            _seek.Maximum = Math.Max(1, duration.TotalSeconds);
            if (Session?.CapturedElement != _seek)
                _seek.Value = position.TotalSeconds;
            if (Session?.CapturedElement != _volume)
                _volume.Value = (_session?.Volume ?? 1) * 100;
            _timeLabel.Text = FormatTime(position) + " / " +
                (duration > TimeSpan.Zero ? FormatTime(duration) : "—");
        }
        finally
        {
            _syncingControls = false;
        }
    }
    private void RenderAudioStage(UiRenderContext context)
    {
        double pad = 32;
        double innerWidth = Math.Max(0, _stageRect.Width - (pad * 2));
        if (innerWidth <= 0 || _stageRect.Height <= 40)
            return;

        _headline.Text = string.IsNullOrEmpty(_mediaTitle) ? "Audio" : _mediaTitle;
        BRect titleRect = new(_stageRect.Left + pad, _stageRect.Top + 28, innerWidth, 28);
        _headline.Measure(titleRect.Size);
        _headline.Arrange(titleRect);
        _headline.Render(context);

        _details.Text = BuildDetailsText();
        BRect detailsRect = new(_stageRect.Left + pad, titleRect.Bottom + 6, innerWidth, 22);
        _details.Measure(detailsRect.Size);
        _details.Arrange(detailsRect);
        _details.Render(context);

        // Waveform visualization
        double waveTop = detailsRect.Bottom + 24;
        double waveHeight = _stageRect.Bottom - waveTop - 16;
        if (waveHeight < 16)
            return;
        BRect waveArea = new(_stageRect.Left + pad, waveTop, innerWidth, waveHeight);

        float[]? peaks = _session?.Info.WaveformPeaks;
        if (peaks is not { Length: > 0 })
        {
            double size = Math.Min(96, Math.Min(waveArea.Width, waveArea.Height));
            BroadplayIcons.AppMark(context.RenderList,
                new BRect(waveArea.Left + (waveArea.Width - size) / 2, waveArea.Top + (waveArea.Height - size) / 2, size, size),
                GlyphColor, BroadplayPalette.Accent, ControlBarBackground);
            return;
        }
        int barCount = peaks.Length;
        double slotWidth = waveArea.Width / barCount;
        double barWidth = Math.Max(2, slotWidth - 2);
        double centerY = waveArea.Top + (waveArea.Height / 2);

        TimeSpan duration = _session?.Info.Duration ?? TimeSpan.Zero;
        TimeSpan position = _session?.Position ?? TimeSpan.Zero;
        double progress = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0.0, 1.0)
            : 0.0;

        for (int i = 0; i < barCount; i++)
        {
            double normalized = Math.Max(0.08, peaks[i]);

            double h = Math.Max(4, normalized * (waveArea.Height - 12));
            double bx = waveArea.Left + (i * slotWidth);
            double by = centerY - (h / 2);
            bool played = (double)i / barCount <= progress;
            context.RenderList.FillRect(new BRect(bx, by, barWidth, h), played ? BroadplayPalette.Accent : WaveformUnplayed);
        }
    }

    private void RenderVideoStage(UiRenderContext context)
    {
        // When a native video surface is actively presenting, leave the stage clear behind it;
        // otherwise show the title, metadata, and playback status centered on the matte.
        bool liveVideoActive = _session is { Info.HasLivePlayback: true } &&
            _session.State is BroadplayPlaybackState.Playing or BroadplayPlaybackState.Paused or BroadplayPlaybackState.Ended;
        if (liveVideoActive)
            return;

        double pad = 32;
        double innerWidth = Math.Max(0, _stageRect.Width - (pad * 2));
        if (innerWidth <= 0 || _stageRect.Height <= 60)
            return;

        double centerY = _stageRect.Top + (_stageRect.Height / 2) - 28;
        _headline.Text = string.IsNullOrEmpty(_mediaTitle) ? "Video" : _mediaTitle;
        BRect titleRect = new(_stageRect.Left + pad, centerY, innerWidth, 28);
        _headline.Measure(titleRect.Size);
        _headline.Arrange(titleRect);
        _headline.Render(context);

        _details.Text = BuildDetailsText();
        BRect detailsRect = new(_stageRect.Left + pad, titleRect.Bottom + 8, innerWidth, 22);
        _details.Measure(detailsRect.Size);
        _details.Arrange(detailsRect);
        _details.Render(context);
    }

    private string BuildDetailsText()
    {
        if (_session is null)
            return "No media loaded";

        BroadplayMediaInfo info = _session.Info;
        string state = _session.State.ToString();
        if (!string.IsNullOrWhiteSpace(_session.ErrorMessage))
            return $"{info.FormatName} · {state}: {_session.ErrorMessage}";

        if (info.Kind == BroadplayMediaKind.Video)
        {
            string dims = info.VideoWidth > 0 && info.VideoHeight > 0
                ? $" · {info.VideoWidth}×{info.VideoHeight}"
                : string.Empty;
            string mode = info.HasLivePlayback ? string.Empty : " · Media information only";
            return $"{info.FormatName}{dims} · {state}{mode}";
        }

        string sampleInfo = info.SampleRate > 0
            ? string.Format(CultureInfo.InvariantCulture, " · {0:N0} Hz · {1} ch", info.SampleRate, info.Channels)
            : string.Empty;
        string bits = info.BitsPerSample > 0 ? $" · {info.BitsPerSample}-bit" : string.Empty;
        return $"{info.FormatName}{sampleInfo}{bits} · {state}";
    }

    private void NotifyChanged()
    {
        Invalidate(UiInvalidationKind.Render);
        _stateChanged();
    }

}
