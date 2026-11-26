// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Resources;
using Broiler.UI;
using Broiler.UI.Button.Stage;
using Broiler.UI.Label.Stage;
using Broiler.UI.MediaStage;
using Broiler.UI.MediaStage.Stage;
using Broiler.UI.MediaTransport.Stage;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Stage;
using Broiler.UI.Stage;
using Broiler.UI.TileGrid;
using Broiler.UI.TileGrid.Stage;

namespace Broiler.Broadplay;

/// <summary>
/// Broadplay's ten-foot / Theater <c>Stage</c> shell composed over <see cref="StageMediaStage"/>,
/// <see cref="StageMediaTransport"/>, <see cref="StageTileGrid"/>, <see cref="StageSpatialNavigator"/>,
/// and <see cref="StageIdleController"/>.
/// </summary>
internal sealed class BroadplayStageShell : UiElement
{
    private readonly BroadplayUiHost _host;
    private readonly StageSpatialNavigator _navigator;
    private readonly StageIdleController _idleController;

    // Browse workspace (Empty / Recent / Folder gallery)
    private readonly StagePanel _browseRoot;
    private readonly StageLabel _browseHeadline;
    private readonly StageLabel _browseStatus;
    private readonly StagePanel _browseActions;
    private readonly StageButton _browseOpenMedia;
    private readonly StageButton _browseOpenFolder;
    private readonly StageButton _browseUpFolder;
    private readonly StageButton _browseNowPlaying;
    private readonly StageButton _browseExitStage;
    private readonly StageTileGrid _tileGrid;

    // Playback workspace (Audio / Video stage + auto-hide OSD)
    private readonly StageMediaStage _mediaStage;
    private readonly StagePanel _playerHeaderActions;
    private readonly StageButton _playerBackToFolder;
    private readonly StageButton _playerOpenMedia;
    private readonly StageButton _playerOpenFolder;
    private readonly StageButton _playerExitStage;
    private readonly StageMediaTransport _transport;

    private readonly Dictionary<int, BImageHandle> _thumbnails = [];
    private readonly BroadplayThumbnailCache _thumbnailCache = new(BroadplayGallery.CreateThumbnail);
    private CancellationTokenSource _thumbnailCts = new();
    private Task<(int Index, byte[]? Bytes)[]>? _thumbnailLoadTask;
    private string[] _tilePaths = [];
    private bool[] _tileIsDirectory = [];
    private bool _showingPlayer;

    public BroadplayStageShell(
        UiSession session,
        BroadplayUiHost host,
        Action openMedia,
        Action openFolder,
        Action upFolder,
        Action returnToGallery,
        Action resumePlayer,
        Action exitStage,
        Action<string, bool> activateEntry,
        Action togglePlayPause,
        Action stop,
        Action previousTrack,
        Action nextTrack,
        Action<TimeSpan> seekAbsolute,
        Action toggleMute,
        Action<double> setVolume)
    {
        ArgumentNullException.ThrowIfNull(session);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _navigator = new StageSpatialNavigator(session);
        _idleController = new StageIdleController(session.Clock, StageThemeTokens.CinemaDark.IdleConcealDelay);

        // Browse workspace
        _browseHeadline = new StageLabel { Role = StageTextRole.Title, Text = "Broiler Broadplay" };
        _browseStatus = new StageLabel { Role = StageTextRole.Caption, IsMuted = true, Text = "Ready" };

        _browseOpenMedia = new StageButton { Text = "Open media", IsDefault = true, IconPainter = StageGlyphs.DrawPlay };
        _browseOpenFolder = new StageButton { Text = "Open folder", IconPainter = StageGlyphs.DrawFolder };
        _browseUpFolder = new StageButton { Text = "Up", IconPainter = StageGlyphs.DrawBackArrow, Visibility = UiVisibility.Collapsed };
        _browseNowPlaying = new StageButton { Text = "Now playing", IconPainter = StageGlyphs.DrawAudioWave, Visibility = UiVisibility.Collapsed };
        _browseExitStage = new StageButton { Text = "Desktop view", IconPainter = StageGlyphs.DrawFullscreen };

        _browseActions = new StagePanel
        {
            StackOrientation = UiStackOrientation.Horizontal,
            Spacing = 14,
        };
        foreach (UiElement button in new UiElement[] { _browseOpenMedia, _browseOpenFolder, _browseUpFolder, _browseNowPlaying, _browseExitStage })
            _browseActions.AddChild(button);

        _tileGrid = new StageTileGrid
        {
            TileSize = new BSize(244, 206),
            TileGap = 20,
            EmptyStateText = "Select Open media or Open folder to begin.",
        };

        var browseHeader = new StagePanel
        {
            StackOrientation = UiStackOrientation.Vertical,
            Spacing = 8,
            Padding = 24,
        };
        browseHeader.AddChild(_browseHeadline);
        browseHeader.AddChild(_browseStatus);
        browseHeader.AddChild(_browseActions);

        _browseRoot = new StagePanel
        {
            LayoutMode = UiPanelLayoutMode.Dock,
            Padding = 12,
        };
        _browseRoot.AddChild(browseHeader);
        _browseRoot.SetDock(browseHeader, UiDock.Top);
        _browseRoot.AddChild(_tileGrid);

        // Playback workspace
        _playerBackToFolder = new StageButton { Text = "Folder", IconPainter = StageGlyphs.DrawBackArrow, Visibility = UiVisibility.Collapsed };
        _playerOpenMedia = new StageButton { Text = "Open", IconPainter = StageGlyphs.DrawPlay };
        _playerOpenFolder = new StageButton { Text = "Browse", IconPainter = StageGlyphs.DrawFolder };
        _playerExitStage = new StageButton { Text = "Exit", ShowText = false, AccessibleName = "Exit stage mode", IconPainter = StageGlyphs.DrawFullscreen, PreferredSize = new BSize(56, 56) };

        _playerHeaderActions = new StagePanel
        {
            StackOrientation = UiStackOrientation.Horizontal,
            Spacing = 12,
        };
        foreach (UiElement child in new UiElement[] { _playerBackToFolder, _playerOpenMedia, _playerOpenFolder, _playerExitStage })
            _playerHeaderActions.AddChild(child);

        _transport = new StageMediaTransport
        {
            ShowFullscreenButton = true,
            IsFullscreen = true,
        };

        _mediaStage = new StageMediaStage
        {
            HeaderActions = _playerHeaderActions,
            TransportOverlay = _transport,
            Visibility = UiVisibility.Collapsed,
        };
        _idleController.VisibilityChanged += (_, _) =>
        {
            _mediaStage.IsChromeVisible = _idleController.IsChromeVisible;
            _host.RequestInvalidate();
        };

        AddChild(_browseRoot);
        AddChild(_mediaStage);

        // Wire callbacks
        _browseOpenMedia.Clicked += (_, _) => openMedia();
        _browseOpenFolder.Clicked += (_, _) => openFolder();
        _browseUpFolder.Clicked += (_, _) => upFolder();
        _browseNowPlaying.Clicked += (_, _) => resumePlayer();
        _browseExitStage.Clicked += (_, _) => exitStage();

        _playerBackToFolder.Clicked += (_, _) => returnToGallery();
        _playerOpenMedia.Clicked += (_, _) => openMedia();
        _playerOpenFolder.Clicked += (_, _) => openFolder();
        _playerExitStage.Clicked += (_, _) => exitStage();

        _tileGrid.ItemActivated += (_, e) =>
        {
            if ((uint)e.Index < (uint)_tilePaths.Length)
                activateEntry(_tilePaths[e.Index], _tileIsDirectory[e.Index]);
        };
        _tileGrid.ViewportWindowChanged += (_, _) => _host.RequestInvalidate();

        _transport.PlayPauseRequested += (_, _) => togglePlayPause();
        _transport.StopRequested += (_, _) => stop();
        _transport.PreviousRequested += (_, _) => previousTrack();
        _transport.NextRequested += (_, _) => nextTrack();
        _transport.SeekRequested += (_, e) => seekAbsolute(e.Position);
        _transport.MuteToggleRequested += (_, _) => toggleMute();
        _transport.VolumeChangeRequested += (_, e) => setVolume(e.Volume);
        _transport.FullscreenToggleRequested += (_, _) => exitStage();
    }

    internal StageSpatialNavigator Navigator => _navigator;
    internal StageIdleController IdleController => _idleController;
    internal StageTileGrid TileGrid => _tileGrid;
    internal StageMediaStage MediaStage => _mediaStage;
    internal StageMediaTransport Transport => _transport;
    internal StageButton OpenMediaButton => _browseOpenMedia;
    internal StageButton OpenFolderButton => _browseOpenFolder;

    internal BRect VideoViewportBounds => _showingPlayer ? _mediaStage.VideoViewportBounds : BRect.Empty;

    internal bool WantsIdleTick => Visibility == UiVisibility.Visible && _showingPlayer && _idleController.IsWaitingForIdle;

    internal void RecordUserActivity()
    {
        if (_showingPlayer && _idleController.RecordActivity())
            _mediaStage.IsChromeVisible = true;
    }

    internal bool TickIdle() => _showingPlayer && _idleController.Tick();

    internal void ClearThumbnails()
    {
        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailLoadTask = null;
        foreach (BImageHandle handle in _thumbnails.Values)
            _host.ReleaseImage(handle);
        _thumbnails.Clear();
    }

    internal void SetFolderTiles(
        string folderPath,
        IReadOnlyList<BroadplayGalleryEntry> entries,
        Func<string, double> getResumeFraction)
    {
        ClearThumbnails();
        _tilePaths = new string[entries.Count];
        _tileIsDirectory = new bool[entries.Count];
        var items = new UiTileItem[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            BroadplayGalleryEntry entry = entries[i];
            _tilePaths[i] = entry.Path;
            _tileIsDirectory[i] = entry.IsDirectory;

            string ext = Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant();
            UiTileKind kind = entry.IsDirectory
                ? UiTileKind.Folder
                : (BroadplayMediaFormats.HasAudioExtension(entry.Name) ? UiTileKind.Audio : UiTileKind.Video);
            string badge = entry.IsDirectory ? "FOLDER" : (ext.Length > 0 ? ext : "MEDIA");
            string subtitle = entry.IsDirectory ? "Open folder" : (kind == UiTileKind.Audio ? "Audio" : "Video");
            double resume = entry.IsDirectory ? 0 : getResumeFraction(entry.Path);

            items[i] = new UiTileItem(
                entry.Path,
                entry.Name,
                subtitle,
                badge,
                kind,
                BImageHandle.Invalid,
                resume);
        }

        _tileGrid.EmptyStateText = "No audio or video files in this folder.";
        _tileGrid.SetItems(items);
        _browseHeadline.Text = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } leaf
            ? leaf
            : folderPath;
    }

    internal void SetRecentTiles(IReadOnlyList<string> recents, Func<string, double> getResumeFraction)
    {
        ClearThumbnails();
        string[] existing = recents.Where(File.Exists).ToArray();
        _tilePaths = existing;
        _tileIsDirectory = new bool[existing.Length];
        var items = new UiTileItem[existing.Length];

        for (int i = 0; i < existing.Length; i++)
        {
            string path = existing[i];
            string name = Path.GetFileName(path);
            string ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
            UiTileKind kind = BroadplayMediaFormats.HasAudioExtension(name) ? UiTileKind.Audio : UiTileKind.Video;
            items[i] = new UiTileItem(
                path,
                name,
                "Recent media",
                ext.Length > 0 ? ext : "MEDIA",
                kind,
                BImageHandle.Invalid,
                getResumeFraction(path));
        }

        _tileGrid.EmptyStateText = "Select Open media or Open folder to start watching or listening.";
        _tileGrid.SetItems(items);
        _browseHeadline.Text = existing.Length > 0 ? "Continue & Recent" : "Broiler Broadplay";
    }

    internal void SyncState(
        BroadplayViewKind currentView,
        IBroadplayMediaSession? session,
        string? mediaTitle,
        string statusText,
        string? galleryPath,
        bool canStepQueue,
        bool hasModalOverlay)
    {
        bool showPlayer = currentView is BroadplayViewKind.Audio or BroadplayViewKind.Video;
        bool viewSwitched = _showingPlayer != showPlayer;
        _showingPlayer = showPlayer;

        _browseRoot.Visibility = showPlayer ? UiVisibility.Collapsed : UiVisibility.Visible;
        _mediaStage.Visibility = showPlayer ? UiVisibility.Visible : UiVisibility.Collapsed;

        bool hasParentFolder = galleryPath is not null && Directory.GetParent(galleryPath) is not null;
        _browseUpFolder.Visibility = galleryPath is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        _browseUpFolder.IsEnabled = hasParentFolder;
        _browseNowPlaying.Visibility = !showPlayer && session is not null ? UiVisibility.Visible : UiVisibility.Collapsed;
        _browseStatus.Text = statusText;

        if (!showPlayer)
            return;

        _playerBackToFolder.Visibility = galleryPath is not null ? UiVisibility.Visible : UiVisibility.Collapsed;

        BroadplayPlaybackSnapshot? snapshot = session?.GetSnapshot();
        bool live = session is { Info.HasLivePlayback: true };
        BroadplayPlaybackState state = snapshot?.State ?? session?.State ?? BroadplayPlaybackState.Failed;
        bool canControl = live && state is not (BroadplayPlaybackState.Loading or BroadplayPlaybackState.Failed);
        bool playing = state == BroadplayPlaybackState.Playing;

        _mediaStage.PresentationKind = currentView == BroadplayViewKind.Audio
            ? UiMediaPresentationKind.Audio
            : UiMediaPresentationKind.Video;
        _mediaStage.Headline = string.IsNullOrWhiteSpace(mediaTitle) ? "Media" : mediaTitle!;
        _mediaStage.MetadataText = statusText;
        _mediaStage.WaveformPeaks = session?.Info.WaveformPeaks ?? [];

        TimeSpan duration = snapshot?.Duration ?? session?.Info.Duration ?? TimeSpan.Zero;
        TimeSpan position = snapshot?.Position ?? session?.Position ?? TimeSpan.Zero;
        _mediaStage.PlaybackProgress = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
            : 0;
        _mediaStage.IsLiveVideoSurfaceActive = currentView == BroadplayViewKind.Video &&
            live &&
            state is BroadplayPlaybackState.Playing or BroadplayPlaybackState.Paused or BroadplayPlaybackState.Ended;

        _transport.CanPlayPause = canControl;
        _transport.CanStop = canControl;
        _transport.CanSeek = canControl && session!.CanSeek && duration > TimeSpan.Zero;
        _transport.CanStepTrack = canStepQueue;
        _transport.CanAdjustVolume = canControl && session!.CanSetVolume;
        _transport.IsPlaying = playing;
        _transport.IsMuted = session?.IsMuted ?? false;
        _transport.Volume = session?.Volume ?? 1.0;
        _transport.Duration = duration;
        _transport.Position = position;

        // Pin OSD visible whenever media is not actively playing, while user is scrubbing, or while a modal is open.
        _idleController.IsPinned = !playing || _transport.IsUserScrubbing || hasModalOverlay;
        if (viewSwitched)
            _idleController.Reveal();
        _mediaStage.IsChromeVisible = _idleController.IsChromeVisible;
    }

    internal void FocusDefault()
    {
        if (_showingPlayer)
        {
            _idleController.Reveal();
            _mediaStage.IsChromeVisible = true;
            _transport.FocusPrimaryAction();
            return;
        }

        if (_tileGrid.Items.Count > 0)
            _tileGrid.Focus();
        else
            _browseOpenMedia.Focus();
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        BSize resolved = new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 1280,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 720);
        if (_showingPlayer)
            _mediaStage.Measure(resolved);
        else
            _browseRoot.Measure(resolved);
        return resolved;
    }

    protected override void ArrangeCore(BRect finalRect)
    {
        if (_showingPlayer)
            _mediaStage.Arrange(finalRect);
        else
            _browseRoot.Arrange(finalRect);
    }

    protected override void RenderCore(UiRenderContext context)
    {
        StageThemeTokens theme = StageControlPaint.GetTheme(this);
        context.RenderList.FillRect(Bounds, theme.Backdrop);

        if (!_showingPlayer)
        {
            PumpVisibleThumbnails();
            _browseRoot.Render(context);
            return;
        }

        _mediaStage.Render(context);
    }

    private void PumpVisibleThumbnails()
    {
        if (_thumbnailLoadTask is { IsCompletedSuccessfully: true })
        {
            foreach ((int index, byte[]? bytes) in _thumbnailLoadTask.Result)
            {
                BImageHandle handle = bytes is null ? BImageHandle.Invalid : _host.CreateImage(bytes);
                _thumbnails[index] = handle;
                if (handle.IsValid)
                    _tileGrid.UpdateArtwork(index, handle);
            }
            _thumbnailLoadTask = null;
        }

        var (first, last) = _tileGrid.GetVisibleIndexRange();
        foreach (int index in _thumbnails.Keys.Where(i => i < first || i >= last).ToArray())
        {
            _host.ReleaseImage(_thumbnails[index]);
            _thumbnails.Remove(index);
            _tileGrid.UpdateArtwork(index, BImageHandle.Invalid);
        }

        if (_thumbnailLoadTask is null && last > first)
        {
            var pending = Enumerable.Range(first, last - first)
                .Where(i => (uint)i < (uint)_tilePaths.Length && !_thumbnails.ContainsKey(i) && !_tileIsDirectory[i])
                .Select(i => (Index: i, Path: _tilePaths[i]))
                .ToArray();

            if (pending.Length > 0)
            {
                CancellationToken token = _thumbnailCts.Token;
                _thumbnailLoadTask = Task.Run(() =>
                    pending.Select(item => (item.Index, Bytes: _thumbnailCache.GetOrCreate(item.Path, token))).ToArray());
                _ = _thumbnailLoadTask.ContinueWith(
                    _ =>
                    {
                        if (!token.IsCancellationRequested)
                            _host.RequestInvalidate();
                    },
                    TaskScheduler.Default);
            }
        }
    }
}
