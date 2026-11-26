// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Stage;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.FileDialog;
using Broiler.UI.FileDialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Menu;
using Broiler.UI.Menu.Standard;
using Broiler.UI.Stage;
using Broiler.UI.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Tooltip.Standard;
using Broiler.UI.Window;
using Broiler.UI.Window.Standard;

namespace Broiler.Broadplay;

internal sealed partial class BroadplayApp : IDisposable
{
    private const int SignatureLength = 32;
    private const int VirtualKeyO = 0x4F;
    private static readonly BSize FileDialogPreferredSize = new(740, 430);

    private readonly BroadplayUiHost _host;
    private readonly Action _requestClose;
    private readonly Action<Uri>? _openUrl;
    private readonly Action? _requestOpenFile;
    private readonly Action? _requestOpenFolder;
    private readonly Action<bool>? _setTicking;
    private readonly Func<Action, bool>? _postToUi;
    private readonly Func<bool, bool>? _setFullscreen;
    private PendingOpen? _pendingOpen;
    private string? _openingName;
    private string? _retryPath;
    private double _volume = 1;
    private bool _muted;
    private readonly UiSession _session;
    private readonly StandardWindow _rootWindow;
    private readonly StandardMenu _menu;
    private readonly StandardToolbar _toolbar;
    private readonly StandardLabel _title;
    private readonly StandardLabel _status;
    private readonly StandardLabel _empty = new()
    {
        Text = "Choose an audio or video file to get started.",
        Foreground = BroadplayPalette.Muted,
        Font = new BFontStyle("Segoe UI", 15),
        Trimming = UiTextTrimming.CharacterEllipsis,
    };
    private readonly BroadplayGallery _gallery;
    private readonly BroadplayMediaView _audioView;
    private readonly BroadplayMediaView _videoView;
    private readonly BroadplayContent _content;
    private readonly BroadplayStageShell _stageShell;
    private readonly StandardTooltip _tooltip;
    private readonly StandardTooltipController _tooltips;
    private readonly IBroadplayMediaHost _mediaHost;
    private readonly bool _ownsMediaHost;
    private readonly UiFileDialogFilter[] _openFilters = BroadplayFileFormats.CreateDefault().CreateOpenFilters();
    private IBroadplayMediaSession? _mediaSession;
    private StandardButton? _openButton, _openFolderButton, _closeButton, _galleryButton, _upFolderButton, _aboutButton;
    private UiMenuItem? _closeMenuItem;
    private bool _isTicking;
    private bool _theaterMode;
    private string? _galleryPath;
    private string? _currentPath;
    private string _lastDirectory = Environment.CurrentDirectory;
    private string _lastAction = "Ready";

    public BroadplayApp(BroadplayUiHost host, Action requestClose, Action<Uri>? openUrl = null,
        Action<bool>? setTicking = null, Action? requestOpenFile = null,
        Action? requestOpenFolder = null, IBroadplayMediaHost? mediaHost = null,
        Func<Action, bool>? postToUi = null, Func<bool, bool>? setFullscreen = null,
        string? collectionPath = null, string? collectionJson = null, Action<string>? persistCollection = null,
        bool initialStageMode = false, IUiClock? clock = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _openUrl = openUrl;
        _setTicking = setTicking;
        _postToUi = postToUi;
        _setFullscreen = setFullscreen;
        _requestOpenFile = requestOpenFile;
        _requestOpenFolder = requestOpenFolder;
        _ownsMediaHost = mediaHost is null;
        _mediaHost = mediaHost ?? new BroadplayManagedMediaHost();
        StandardUiSessionBuilder sessionBuilder = new StandardUiSessionBuilder()
            .WithDispatcher(new ImmediateUiDispatcher());
        if (clock is not null)
            sessionBuilder.WithClock(clock);
        _session = sessionBuilder.Build(_host);
        _gallery = new BroadplayGallery(_host, entry =>
        {
            if (entry.IsDirectory)
                OpenFolder(entry.Path);
            else
                Intake([entry.Path], QueueIntake.PlayNow);
        }) { Visibility = UiVisibility.Collapsed };
        _audioView = new BroadplayMediaView(BroadplayMediaKind.Audio, OnMediaStateChanged)
        {
            ToolTipText = "Audio player",
            Visibility = UiVisibility.Collapsed,
        };
        _videoView = new BroadplayMediaView(BroadplayMediaKind.Video, OnMediaStateChanged)
        {
            ToolTipText = "Video player",
            Visibility = UiVisibility.Collapsed,
        };

        LoadCollection(collectionPath, collectionJson, persistCollection);
        _menu = CreateMenu();
        _toolbar = CreateToolbar();
        _title = new StandardLabel
        {
            ToolTipText = "Media title",
            Text = "Open media",
            Font = new BFontStyle("Segoe UI", 20, BFontWeight.SemiBold),
            Foreground = BroadplayPalette.Title,
            Trimming = UiTextTrimming.CharacterEllipsis,
        };
        _status = new StandardLabel
        {
            ToolTipText = "Playback status",
            Text = "Ready",
            Font = new BFontStyle("Segoe UI", 13),
            Foreground = BroadplayPalette.Muted,
            Trimming = UiTextTrimming.CharacterEllipsis,
        };

        _rootWindow = new StandardWindow
        {
            Title = "Broiler Broadplay",
            Background = BroadplayPalette.Canvas,
            BorderColor = BroadplayPalette.WindowBorder,
            ActiveBorderColor = BroadplayPalette.Accent,
            BorderThickness = 1,
        };
        _tooltip = new StandardTooltip
        {
            Font = new BFontStyle("Segoe UI", 12),
            Background = BroadplayPalette.MenuPopup,
            Foreground = BroadplayPalette.Title,
            BorderColor = BroadplayPalette.WindowBorder,
        };
        _tooltips = new StandardTooltipController(_tooltip);

        _content = new BroadplayContent(_menu, _toolbar, _title, _empty, _gallery, _audioView, _videoView, _status);
        _stageShell = new BroadplayStageShell(
            _session,
            _host,
            openMedia: ShowOpenDialog,
            openFolder: ShowFolderDialog,
            upFolder: OpenParentFolder,
            returnToGallery: ReturnToGallery,
            resumePlayer: () =>
            {
                if (_mediaSession is not null)
                    ShowView(_mediaSession.Info.Kind == BroadplayMediaKind.Audio ? BroadplayViewKind.Audio : BroadplayViewKind.Video);
            },
            exitStage: () => SetTheaterMode(false),
            activateEntry: (path, isDirectory) =>
            {
                if (isDirectory)
                    OpenFolder(path);
                else
                    Intake([path], QueueIntake.PlayNow);
            },
            togglePlayPause: () => ActiveMediaView?.TogglePlayPause(),
            stop: () => ActiveMediaView?.Stop(),
            previousTrack: PreviousQueue,
            nextTrack: NextQueue,
            seekAbsolute: SeekAbsolute,
            toggleMute: () => ActiveMediaView?.ToggleMute(),
            setVolume: SetVolume)
        {
            Visibility = UiVisibility.Collapsed,
        };

        _rootWindow.AddChild(_content);
        _rootWindow.AddChild(_stageShell);
        _rootWindow.OpenOwnedWindow(_tooltip, new BRect(0, 0, 1, 1));
        _tooltip.Deactivate();
        _session.AddRoot(_rootWindow);
        _session.SetFocus(_openButton);
        _menu.ItemInvoked += (_, _) => RefreshUi();
        RefreshUi();
        if (initialStageMode)
            SetTheaterMode(true);
    }
    internal static BPixelBuffer CreateAppIcon(int size) =>
        BroadplayIcons.RenderAppIcon(size, BroadplayPalette.Page, BroadplayPalette.Accent, BroadplayPalette.Canvas);

    public UiSession Session => _session;

    internal StandardWindow RootWindow => _rootWindow;

    internal StandardMenu Menu => _menu;
    internal string LastAction => _lastAction;
    internal BroadplayViewKind CurrentView => _content.CurrentView;
    internal BroadplayGallery Gallery => _gallery;
    internal BroadplayStageShell StageShell => _stageShell;
    internal bool IsStageMode => _theaterMode;
    internal StandardToolbar Toolbar => _toolbar;
    internal StandardButton? OpenButton => _openButton;
    internal StandardButton? OpenFolderButton => _openFolderButton;
    internal StandardButton? CloseButton => _closeButton;
    internal StandardButton? GalleryButton => _galleryButton;
    internal StandardButton? UpFolderButton => _upFolderButton;
    internal StandardButton? AboutButton => _aboutButton;

    public void SetStageMode(bool enabled) => SetTheaterMode(enabled);

    public BRenderList RenderFrame()
    {
        CheckCollectionWork();
        SyncStageShell();
        BRenderList list = _session.RenderFrame();
        SyncVideoViewport();
        return list;
    }
    internal IBroadplayMediaSession? MediaSession => _mediaSession;

    internal BroadplayMediaView? ActiveMediaView => _content.CurrentView switch
    {
        BroadplayViewKind.Audio => _audioView,
        BroadplayViewKind.Video => _videoView,
        _ => null,
    };

    private bool CanControlPlayback => ActiveMediaView?.CanControlPlayback == true;
    private bool CanSeek => CanControlPlayback && _mediaSession!.CanSeek;
    private bool CanSetVolume => CanControlPlayback && _mediaSession!.CanSetVolume;

    public void Dispatch(UiInputEvent input)
    {
        if (_rootWindow.OwnedWindows.Count == 1 && HandleOpenShortcut(input))
            return;

        if (_rootWindow.OwnedWindows.Count == 1 &&
            input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down &&
            input.KeyModifiers == KeyboardModifierState.None &&
            IsKey(input, 0x7A, "F11"))
        {
            SetTheaterMode(!_theaterMode);
            return;
        }

        if (_theaterMode)
        {
            DispatchStageInput(input);
            return;
        }

        if (_session.DispatchInput(input))
            _host.RequestInvalidate();
        UpdateTooltip(input);
    }

    private void DispatchStageInput(UiInputEvent input)
    {
        if (input.Kind is UiInputEventKind.PointerMove or UiInputEventKind.PointerButton or UiInputEventKind.PointerWheel ||
            (input.Kind == UiInputEventKind.KeyboardKey && input.KeyTransition == KeyboardKeyTransition.Down))
        {
            _stageShell.RecordUserActivity();
            SyncTicking();
        }

        if (_session.DispatchInput(input))
        {
            _host.RequestInvalidate();
            return;
        }

        if (!UiNavigationMapper.TryMap(input, out UiNavigationAction action))
            return;

        bool hasModal = _rootWindow.OwnedWindows.Count > 1;
        if (UiNavigationMapper.TryGetDirection(action, out UiNavigationDirection direction))
        {
            UiElement scope = _session.ModalElement ?? _stageShell;
            if (_stageShell.Navigator.MoveFocus(direction, scope))
                _host.RequestInvalidate();
            return;
        }

        if (hasModal)
            return;

        switch (action)
        {
            case UiNavigationAction.Back:
                if (CurrentView is BroadplayViewKind.Audio or BroadplayViewKind.Video && _galleryPath is not null)
                    ReturnToGallery();
                else
                    SetTheaterMode(false);
                break;
            case UiNavigationAction.PlayPause:
                ActiveMediaView?.TogglePlayPause();
                break;
            case UiNavigationAction.Stop:
                ActiveMediaView?.Stop();
                break;
            case UiNavigationAction.NextTrack:
                NextQueue();
                break;
            case UiNavigationAction.PreviousTrack:
                PreviousQueue();
                break;
            case UiNavigationAction.SeekForward:
                ActiveMediaView?.SeekRelative(TimeSpan.FromSeconds(5));
                break;
            case UiNavigationAction.SeekBackward:
                ActiveMediaView?.SeekRelative(TimeSpan.FromSeconds(-5));
                break;
            case UiNavigationAction.VolumeUp:
                ActiveMediaView?.AdjustVolume(0.1);
                break;
            case UiNavigationAction.VolumeDown:
                ActiveMediaView?.AdjustVolume(-0.1);
                break;
            case UiNavigationAction.Mute:
                ActiveMediaView?.ToggleMute();
                break;
        }
    }

    private void SetTheaterMode(bool enabled)
    {
        if (_theaterMode == enabled)
            return;
        if (_setFullscreen is not null && !_setFullscreen(enabled))
            return;

        _theaterMode = enabled;
        _tooltips.Dismiss();
        _rootWindow.BorderThickness = enabled ? 0 : 1;

        if (enabled)
        {
            StageThemeController.Apply(_session, StageThemeTokens.CinemaDark);
            _session.FocusVisibilityPolicy = UiFocusVisibilityPolicy.Always;
            _content.Visibility = UiVisibility.Collapsed;
            _stageShell.Visibility = UiVisibility.Visible;
            RefreshStageTiles();
            SyncStageShell();
            _stageShell.FocusDefault();
        }
        else
        {
            _stageShell.ClearThumbnails();
            _stageShell.Visibility = UiVisibility.Collapsed;
            _content.Visibility = UiVisibility.Visible;
            _content.SetTheaterMode(false);
            _session.FocusVisibilityPolicy = UiFocusVisibilityPolicy.Auto;

            if (ActiveMediaView is { } mediaView)
                mediaView.FocusTransport();
            else if (CurrentView == BroadplayViewKind.Gallery)
                _session.SetFocus(_gallery);
            else
                _session.SetFocus(_openButton);
        }

        SyncTicking();
        _host.RequestInvalidate();
    }

    public void AnimationTick()
    {
        CheckCollectionWork();
        bool invalidate = _tooltips.Tick();

        if (_theaterMode)
            SyncStageShell();

        if (_theaterMode && _stageShell.TickIdle())
            invalidate = true;

        if (NeedsPlaybackTick)
        {
            _status.Text = BuildStatus();
            if (_theaterMode)
            {
                _stageShell.MediaStage.Invalidate(UiInvalidationKind.Render);
            }
            else
            {
                ActiveMediaView?.Invalidate(UiInvalidationKind.Render);
            }
            invalidate = true;
        }

        if (invalidate)
            _host.RequestInvalidate();

        SyncTicking();
    }

    private bool NeedsPlaybackTick => _mediaSession is { Info.HasLivePlayback: true } session &&
        (!session.ReportsStateChanges || session.State is BroadplayPlaybackState.Playing or BroadplayPlaybackState.Seeking);

    public bool WantsTick => _tooltips.IsWaiting || (_theaterMode && _stageShell.WantsIdleTick) || NeedsPlaybackTick || _indexedScan is not null || !_collectionCallbacks.IsEmpty;

    private void UpdateTooltip(UiInputEvent input)
    {
        bool changed = input.Kind == UiInputEventKind.PointerMove
            ? _tooltips.PointerMoved(input.Position)
            : _tooltips.Dismiss();

        SyncTicking();
        if (changed)
            _host.RequestInvalidate();
    }

    private void SyncTicking()
    {
        bool wanted = WantsTick;
        if (wanted == _isTicking)
            return;

        _isTicking = wanted;
        _setTicking?.Invoke(wanted);
    }

    public void Invalidate() => _host.RequestInvalidate();

    public bool Open(string name, byte[] bytes) => Open(name, (ReadOnlySpan<byte>)bytes);

    public bool Open(string name, ReadOnlySpan<byte> bytes)
    {
        CancelPendingOpen();
        if (bytes.IsEmpty)
            return Refuse(name, "The file is empty.");
        return TryIdentify(name, bytes, out BroadplayMediaKind kind)
            ? OpenMediaBytes(name, bytes.ToArray(), kind)
            : Refuse(name, "Choose an audio or video file. Use Plate for documents and images.");
    }

    public bool OpenFile(string path)
    {
        _requestedQueueItem = null;
        return OpenFileCore(path);
    }

    private bool OpenFileCore(string path)
    {
        CancelPendingOpen();
        _retryPath = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (_postToUi is not null && _mediaHost is IBroadplayAsyncMediaHost asyncHost)
            {
                BroadplayMediaFormats.TryDetectKindFromExtension(fullPath, out BroadplayMediaKind hint);
                return BeginOpen(asyncHost, new BroadplayMediaSource.LocalFile(fullPath, hint));
            }
            byte[] signature = ReadSignature(fullPath, out bool empty);
            if (empty)
                return Refuse(path, "The file is empty.");
            return TryIdentify(fullPath, signature, out BroadplayMediaKind kind)
                ? OpenMedia(fullPath, kind)
                : Refuse(path, "Choose an audio or video file. Use Plate for documents and images.");
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            return Refuse(path, ex.Message);
        }
    }

    internal static bool TryIdentify(string name, ReadOnlySpan<byte> bytes, out BroadplayMediaKind kind)
    {
        if (BroadplayMediaFormats.TryDetectKindFromSignature(bytes, name, out kind))
            return true;
        return BroadplayImageFormats.ContentTypeForSignature(bytes) is null &&
            BroadplayMediaFormats.TryDetectKindFromExtension(name, out kind);
    }

    private bool Refuse(string name, string reason)
    {
        _lastAction = "Could not open " + Path.GetFileName(name) + ": " + reason;
        RefreshUi();
        return false;
    }
    public bool OpenFolder(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            BroadplayGalleryEntry[] entries = BroadplayGallery.ReadFolder(fullPath);
            // Commit only after listing succeeds, so a refused folder leaves the current view intact.
            _gallery.SetEntries(entries);
            _galleryPath = fullPath;
            _lastDirectory = fullPath;
            RefreshStageTiles();
            ReturnToGallery();
            return true;
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _lastAction = "Could not open folder: " + ex.Message;
            RefreshUi();
            return false;
        }
    }

    private void ReturnToGallery()
    {
        _requestedQueueItem = null;
        if (_galleryPath is null)
            return;
        CancelPendingOpen();
        ReleaseMedia();
        _currentPath = null;
        _title.Text = _galleryPath;
        ShowView(BroadplayViewKind.Gallery);
        _lastAction = "Opened folder " + _galleryPath;
        RefreshUi();
        if (_theaterMode)
            _stageShell.FocusDefault();
        else
            _session.SetFocus(_gallery);
    }

    private void OpenParentFolder()
    {
        if (_galleryPath is not null && Directory.GetParent(_galleryPath) is { } parent)
            OpenFolder(parent.FullName);
    }

    private void ShowFolderDialog()
    {
        if (_requestOpenFolder is not null)
        {
            _requestOpenFolder();
            return;
        }
        ShowFolderDialogInternal();
    }

    private void ShowFolderDialogInternal()
    {
        if (_theaterMode)
        {
            var stageSize = new BSize(880, 560);
            var stageDialog = new StageDialog
            {
                Title = "Open folder",
                PreferredSize = stageSize,
            };
            var stagePicker = new BroadplayStageFolderDialog(stageDialog, GetDialogDirectory());
            stageDialog.AddChild(stagePicker);
            StageThemeController.ApplyToSubtree(stageDialog, StageThemeTokens.CinemaDark);
            stageDialog.ResultCompleted += (_, e) =>
            {
                if (e.Result.Kind == UiDialogResultKind.Accepted)
                    OpenFolder(e.Result.Value ?? stagePicker.PathEdit.Text);
                else if (_theaterMode)
                    _stageShell.FocusDefault();
                RefreshUi();
            };
            stageDialog.ShowModal(_rootWindow, CenterInViewport(stageSize, minTop: 32));
            stagePicker.FocusInitial();
            RefreshUi();
            return;
        }

        var dialog = new StandardDialog { Title = "Open folder", PreferredSize = FileDialogPreferredSize };
        var picker = new BroadplayFolderDialog(dialog, GetDialogDirectory());
        dialog.AddChild(picker);
        dialog.ResultCompleted += (_, e) =>
        {
            if (e.Result.Kind == UiDialogResultKind.Accepted)
                OpenFolder(e.Result.Value ?? picker.PathEdit.Text);
        };
        dialog.ShowModal(_rootWindow, GetDialogPlacement());
        picker.Focus();
    }

    public void Dispose()
    {
        RecordProgress();
        SaveCollection();
        _indexScan?.Cancel();
        _indexScan?.Dispose();
        _stageShell.ClearThumbnails();
        CancelPendingOpen();
        ReleaseMedia();
        if (_ownsMediaHost)
            _mediaHost.Dispose();
        _session.Dispose();
    }
    private StandardMenu CreateMenu()
    {
        var dispatcher = new StandardCommandDispatcher();
        dispatcher.Add(new StandardCommand("file.open", ShowOpenDialog));
        dispatcher.Add(new StandardCommand("file.batch", () => ShowCollection(CollectionView.Library)));
        dispatcher.Add(new StandardCommand("queue.show", ShowCollection));
        dispatcher.Add(new StandardCommand("queue.next", NextQueue));
        dispatcher.Add(new StandardCommand("queue.previous", PreviousQueue));
        dispatcher.Add(new StandardCommand("file.retry", () => OpenFile((_retryPath ?? _currentPath)!),
            () => _pendingOpen is null && (_retryPath is not null ||
                (_currentPath is not null && _mediaSession?.State == BroadplayPlaybackState.Failed))));
        dispatcher.Add(new StandardCommand("file.open-folder", ShowFolderDialog));
        dispatcher.Add(new StandardCommand("file.close", CloseFile, () => _currentPath is not null || _galleryPath is not null || _pendingOpen is not null));
        dispatcher.Add(new StandardCommand("view.gallery", ReturnToGallery, () => _galleryPath is not null));
        dispatcher.Add(new StandardCommand("file.exit", _requestClose));
        dispatcher.Add(new StandardCommand("playback.toggle", () => ActiveMediaView?.TogglePlayPause(), () => CanControlPlayback));
        dispatcher.Add(new StandardCommand("playback.stop", () => ActiveMediaView?.Stop(), () => CanControlPlayback));
        dispatcher.Add(new StandardCommand("playback.back", () => ActiveMediaView?.SeekRelative(TimeSpan.FromSeconds(-5)), () => CanSeek));
        dispatcher.Add(new StandardCommand("playback.forward", () => ActiveMediaView?.SeekRelative(TimeSpan.FromSeconds(5)), () => CanSeek));
        dispatcher.Add(new StandardCommand("playback.mute", () => ActiveMediaView?.ToggleMute(), () => CanSetVolume));
        dispatcher.Add(new StandardCommand("playback.vol-up", () => ActiveMediaView?.AdjustVolume(0.1), () => CanSetVolume));
        dispatcher.Add(new StandardCommand("playback.vol-down", () => ActiveMediaView?.AdjustVolume(-0.1), () => CanSetVolume));
        dispatcher.Add(new StandardCommand("help.about", ShowAbout));

        var file = new UiMenuItem("file", "File") { AccessKey = 'F' };
        file.Children.Add(new UiMenuItem("open", "Open media...") { CommandName = "file.open", AccessKey = 'O' });
        file.Children.Add(new UiMenuItem("batch", "Select multiple files…") { CommandName = "file.batch" });
        file.Children.Add(new UiMenuItem("retry", "Retry media") { CommandName = "file.retry", AccessKey = 'R' });
        file.Children.Add(new UiMenuItem("open-folder", "Open folder...") { CommandName = "file.open-folder", AccessKey = 'F' });
        _closeMenuItem = new UiMenuItem("close", "Close") { CommandName = "file.close", AccessKey = 'C' };
        file.Children.Add(_closeMenuItem);
        file.Children.Add(new UiMenuItem("exit", "Exit") { CommandName = "file.exit", AccessKey = 'X' });

        var view = new UiMenuItem("view", "View") { AccessKey = 'V' };
        view.Children.Add(new UiMenuItem("gallery", "Back to media folder") { CommandName = "view.gallery", AccessKey = 'G' });

        dispatcher.Add(new StandardCommand("view.theater", () => SetTheaterMode(!_theaterMode)));
        dispatcher.Add(new StandardCommand("view.stage", () => SetTheaterMode(!_theaterMode)));
        view.Children.Add(new UiMenuItem("theater", "Stage mode") { CommandName = "view.theater", Accelerator = "F11" });
        var playback = new UiMenuItem("playback", "Playback") { AccessKey = 'P' };
        playback.Children.Add(new UiMenuItem("play-pause", "Play / Pause") { CommandName = "playback.toggle", AccessKey = 'P', Accelerator = "Space" });
        playback.Children.Add(new UiMenuItem("stop", "Stop") { CommandName = "playback.stop", AccessKey = 'S', Accelerator = "S" });
        playback.Children.Add(new UiMenuItem("seek-back", "Seek back 5s") { CommandName = "playback.back", AccessKey = 'B', Accelerator = "Left" });
        playback.Children.Add(new UiMenuItem("seek-forward", "Seek forward 5s") { CommandName = "playback.forward", AccessKey = 'F', Accelerator = "Right" });
        playback.Children.Add(new UiMenuItem("mute", "Mute / Unmute") { CommandName = "playback.mute", AccessKey = 'M', Accelerator = "M" });
        playback.Children.Add(new UiMenuItem("vol-up", "Volume up") { CommandName = "playback.vol-up", AccessKey = 'U', Accelerator = "Up" });
        playback.Children.Add(new UiMenuItem("vol-down", "Volume down") { CommandName = "playback.vol-down", AccessKey = 'D', Accelerator = "Down" });

        playback.Children.Add(new UiMenuItem("queue", "Queue and collection…") { CommandName = "queue.show" });
        playback.Children.Add(new UiMenuItem("previous", "Previous item") { CommandName = "queue.previous" });
        playback.Children.Add(new UiMenuItem("next", "Next item") { CommandName = "queue.next" });
        var help = new UiMenuItem("help", "Help") { AccessKey = 'H' };
        help.Children.Add(new UiMenuItem("about", "About Broiler Broadplay") { CommandName = "help.about", AccessKey = 'A' });

        var menu = new StandardMenu
        {
            PresentationMode = UiMenuPresentationMode.MenuBar,
            PreferredSize = new BSize(360, 30),
            MenuBarHeight = 30,
            ItemHeight = 28,
            PopupWidth = 210,
            Font = new BFontStyle("Segoe UI", 14),
            Background = BroadplayPalette.MenuSurface,
            PopupBackground = BroadplayPalette.MenuPopup,
            Foreground = BroadplayPalette.Title,
            BorderColor = BroadplayPalette.ViewBorder,
            SelectedBackground = BroadplayPalette.MenuSelected,
            CommandDispatcher = dispatcher,
        };
        menu.SetItems([file, view, playback, help]);
        return menu;
    }
    private const double IconButtonExtent = 30;
    private const double IconExtent = 16;

    private StandardToolbar CreateToolbar()
    {
        var toolbar = new StandardToolbar
        {
            ToolTipText = "Media actions",
            Title = "Media actions",
            PreferredSize = new BSize(0, 42),
            Orientation = UiToolbarOrientation.Horizontal,
            Padding = 5,
            Spacing = 3,
            GroupExtent = 9,
            Background = BroadplayPalette.ToolbarSurface,
            BorderColor = BroadplayPalette.MenuRule,
            SeparatorColor = BroadplayPalette.MenuRule,
            CornerRadius = 0,
            Foreground = BroadplayPalette.Title,
            PopupBackground = BroadplayPalette.MenuPopup,
            Font = new BFontStyle("Segoe UI", 15),
        };

        _openButton = ToolbarAction("Open media", "Open media... (Ctrl+O)", ShowOpenDialog, BroadplayIcons.OpenFile);
        _openFolderButton = ToolbarAction("Open folder", "Open folder...", ShowFolderDialog, BroadplayIcons.OpenFolder);
        _closeButton = ToolbarAction("Close", "Close file", CloseFile, BroadplayIcons.Close);

        _galleryButton = ToolbarAction("Gallery", "Back to media folder", ReturnToGallery, BroadplayIcons.Gallery);
        _galleryButton.Visibility = UiVisibility.Collapsed;

        _upFolderButton = ToolbarAction("Up", "Up folder", OpenParentFolder, BroadplayIcons.UpFolder);
        _upFolderButton.Visibility = UiVisibility.Collapsed;


        _aboutButton = ToolbarAction("About", "About Broiler Broadplay", ShowAbout, BroadplayIcons.About);

        toolbar.AddChild(_openButton);
        toolbar.AddChild(_openFolderButton);
        toolbar.AddChild(_closeButton);

        toolbar.AddChild(_galleryButton);
        toolbar.AddChild(_upFolderButton);


        toolbar.AddChild(_aboutButton);

        toolbar.SetBreakBefore(_galleryButton, UiToolbarBreak.Gap);
        return toolbar;
    }
    private StandardButton ToolbarAction(
        string text,
        string tip,
        Action action,
        Action<BRenderList, BRect, BColor>? icon)
    {
        var button = new StandardButton
        {
            Text = text,
            ToolTipText = tip,
            PreferredSize = new BSize(IconButtonExtent, IconButtonExtent),
            Font = new BFontStyle("Segoe UI", 13),
            PaddingX = 6,
            PaddingY = 5,
            IconPainter = icon,
            IconExtent = IconExtent,
            Background = BroadplayPalette.ToolbarButton,
            Foreground = BroadplayPalette.Title,
            BorderColor = BroadplayPalette.ToolbarButtonBorder,
            DisabledForeground = BroadplayPalette.Muted,
            SecondaryHoverBackground = BroadplayPalette.ToolbarButtonHover,
            SecondaryPressedBackground = BroadplayPalette.ToolbarButtonPressed,
            FocusRing = BroadplayPalette.Accent,
            CornerRadius = 5,
        };
        button.Clicked += (_, _) =>
        {
            action();
            RefreshUi();
        };
        return button;
    }
    private void ShowOpenDialog()
    {
        if (_requestOpenFile is not null)
        {
            _requestOpenFile();
            _lastAction = "Open file";
            RefreshUi();
            return;
        }
        ShowOpenDialogInternal();
    }

    private void ShowOpenDialogInternal()
    {
        var dialog = new StandardFileDialog
        {
            Mode = UiFileDialogMode.Open,
            CurrentDirectory = GetDialogDirectory(),
            FileName = _currentPath is null ? string.Empty : Path.GetFileName(_currentPath),
            PreferredSize = FileDialogPreferredSize,
        };
        if (_theaterMode)
            dialog.BreakOutMode = UiWindowBreakOutMode.Manual;
        dialog.SetFileTypeFilters(_openFilters);
        dialog.ResultCompleted += (_, e) =>
        {
            if (e.Result.Kind == UiDialogResultKind.Accepted && !string.IsNullOrWhiteSpace(e.Result.Value))
                Intake([e.Result.Value], QueueIntake.PlayNow);
            else if (_theaterMode)
                _stageShell.FocusDefault();
            RefreshUi();
        };

        dialog.ShowOpenModal(_rootWindow, GetDialogPlacement());
        _lastAction = "Open file";
        RefreshUi();
    }
    private bool OpenMedia(string fullPath, BroadplayMediaKind kind)
    {
        if (!_mediaHost.TryOpen(fullPath, kind, out IBroadplayMediaSession? session, out string? error) || session is null)
        {
            _lastAction = "Could not open " + Path.GetFileName(fullPath) + ": " +
                (string.IsNullOrWhiteSpace(error) ? "unsupported media stream." : error);
            RefreshUi();
            return false;
        }

        return AdoptMediaSession(fullPath, session);
    }

    private bool OpenMediaBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind)
    {
        if (_postToUi is not null && _mediaHost is IBroadplayAsyncMediaHost asyncHost)
            return BeginOpen(asyncHost, new BroadplayMediaSource.Bytes(name, bytes, kind));

        if (!_mediaHost.TryOpenBytes(name, bytes, kind, out IBroadplayMediaSession? session, out string? error) || session is null)
        {
            _lastAction = "Could not open " + Path.GetFileName(name) + ": " +
                (string.IsNullOrWhiteSpace(error) ? "unsupported media stream." : error);
            RefreshUi();
            return false;
        }

        return AdoptMediaSession(name, session);
    }

    private bool BeginOpen(IBroadplayAsyncMediaHost host, BroadplayMediaSource source)
    {
        var request = new PendingOpen();
        _pendingOpen = request;
        _openingName = Path.GetFileName(source.Name);
        RefreshUi();
        _ = CompleteOpenAsync(host, source, request);
        return true;
    }

    private async Task CompleteOpenAsync(IBroadplayAsyncMediaHost host, BroadplayMediaSource source,
        PendingOpen request)
    {
        BroadplayOpenResult result;
        try
        {
            result = await host.OpenAsync(source, request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            request.Complete(default);
            return;
        }
        catch (Exception ex)
        {
            result = new BroadplayOpenResult(null, ex.Message);
        }

        if (!request.Complete(result))
            return;

        if (!_postToUi!(() =>
        {
            BroadplayOpenResult completed = request.TakeResult();
            if (_pendingOpen != request)
            {
                completed.Session?.Dispose();
                return;
            }

            _pendingOpen = null;
            _openingName = null;
            if (completed.Session is { State: not (BroadplayPlaybackState.Failed or BroadplayPlaybackState.Loading) } candidate)
                AdoptMediaSession(source.Name, candidate);
            else
            {
                _retryPath = (source as BroadplayMediaSource.LocalFile)?.Path;
                string? error = completed.Error ?? completed.Session?.ErrorMessage;
                completed.Session?.Dispose();
                Refuse(source.Name, error ?? "Unsupported stream. Check the installed codecs or choose another file.");
                QueueOpenFailed();
            }
        }))
            request.TakeResult().Session?.Dispose();
    }

    private void CancelPendingOpen()
    {
        PendingOpen? request = _pendingOpen;
        _pendingOpen = null;
        _openingName = null;
        request?.Cancel();
    }

    private sealed class PendingOpen
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private Task _cancelTask = Task.CompletedTask;
        private bool _completed;
        private bool _cancelled;
        private BroadplayOpenResult _result;

        public CancellationToken Token { get; }

        public PendingOpen() => Token = _cancellation.Token;

        public bool Complete(BroadplayOpenResult result)
        {
            lock (_gate)
            {
                _completed = true;
                if (_cancelTask.IsCompletedSuccessfully)
                    _cancellation.Dispose();
                else
                    _ = DisposeCancellationAsync(_cancelTask);
                if (!_cancelled)
                {
                    _result = result;
                    return true;
                }
            }

            result.Session?.Dispose();
            return false;
        }

        private async Task DisposeCancellationAsync(Task cancellation)
        {
            try { await cancellation.ConfigureAwait(false); }
            finally { _cancellation.Dispose(); }
        }

        public BroadplayOpenResult TakeResult()
        {
            lock (_gate)
            {
                BroadplayOpenResult result = _result;
                _result = default;
                return result;
            }
        }

        public void Cancel()
        {
            BroadplayOpenResult result;
            lock (_gate)
            {
                _cancelled = true;
                if (!_completed)
                    _cancelTask = _cancellation.CancelAsync();
                result = _result;
                _result = default;
            }
            result.Session?.Dispose();
        }
    }

    private bool AdoptMediaSession(string name, IBroadplayMediaSession session)
    {
        ReleaseMedia();
        _mediaSession = session;
        _handledTerminal = null;
        if (_requestedQueueItem is null)
            _queue.Detach();
        if (session.CanSetVolume)
        {
            session.Volume = _volume;
            session.IsMuted = _muted;
        }
        session.StateChanged += OnBackendStateChanged;

        BroadplayMediaView view = session.Info.Kind == BroadplayMediaKind.Audio ? _audioView : _videoView;
        BroadplayViewKind viewKind = session.Info.Kind == BroadplayMediaKind.Audio
            ? BroadplayViewKind.Audio
            : BroadplayViewKind.Video;

        view.Attach(session, Path.GetFileName(name));
        ShowView(viewKind);
        AdoptPath(name);
        _lastAction = "Opened " + Path.GetFileName(name);
        QueueOpened(session);
        RefreshUi();
        if (_theaterMode)
            _stageShell.FocusDefault();
        else
            view.FocusTransport();
        SyncTicking();
        return true;
    }

    private void ReleaseMedia()
    {
        if (_mediaSession is null)
            return;

        RecordProgress();
        _mediaSession.StateChanged -= OnBackendStateChanged;
        _mediaSession.SetVideoViewport(BRect.Empty, visible: false);
        _audioView.Attach(null, string.Empty);
        _videoView.Attach(null, string.Empty);
        _mediaSession.Dispose();
        _mediaSession = null;
        SyncTicking();
    }

    private void OnMediaStateChanged()
    {
        if (_mediaSession is { CanSetVolume: true } session)
        {
            _volume = session.Volume;
            _muted = session.IsMuted;
        }
        SyncTicking();
        RefreshUi();
    }

    internal bool IsPlayingVideo => _mediaSession is { Info.Kind: BroadplayMediaKind.Video,
        State: BroadplayPlaybackState.Playing or BroadplayPlaybackState.Seeking };

    internal void PauseForSystemChange(string reason)
    {
        if (_mediaSession is { State: BroadplayPlaybackState.Playing or BroadplayPlaybackState.Seeking } session)
        {
            session.Pause();
            _lastAction = reason;
            RefreshUi();
        }
    }

    private void OnBackendStateChanged(object? sender, EventArgs args)
    {
        _postToUi?.Invoke(() =>
        {
            if (ReferenceEquals(_mediaSession, sender))
            {
                OnMediaStateChanged();
                QueueStateChanged(_mediaSession!);
            }
        });
    }

    private void SyncVideoViewport()
    {
        if (_mediaSession is null || _mediaSession.Info.Kind != BroadplayMediaKind.Video)
            return;

        bool hasModalOverlay = _rootWindow.OwnedWindows.Count > 1 || (!_theaterMode && _menu.IsOpen);
        bool visible = _content.CurrentView == BroadplayViewKind.Video &&
            !hasModalOverlay &&
            _mediaSession.State is BroadplayPlaybackState.Ready or BroadplayPlaybackState.Playing or
                BroadplayPlaybackState.Paused or BroadplayPlaybackState.Seeking or BroadplayPlaybackState.Ended;

        BRect viewport = _theaterMode
            ? FitStageVideoViewport(_stageShell.VideoViewportBounds)
            : _videoView.VideoViewportBounds;
        _mediaSession.SetVideoViewport(viewport, visible);
    }

    private BRect FitStageVideoViewport(BRect stageRect)
    {
        if (stageRect.Width <= 4 || stageRect.Height <= 4)
            return BRect.Empty;

        double width = stageRect.Width;
        double height = stageRect.Height;
        if (_mediaSession?.Info is { VideoWidth: > 0, VideoHeight: > 0 } info)
        {
            double aspect = (double)info.VideoWidth / info.VideoHeight;
            width = Math.Min(width, height * aspect);
            height = Math.Min(height, width / aspect);
        }

        return new BRect(
            stageRect.Left + ((stageRect.Width - width) * 0.5),
            stageRect.Top + ((stageRect.Height - height) * 0.5),
            width,
            height);
    }

    private void SeekAbsolute(TimeSpan position)
    {
        if (!CanSeek || _mediaSession is null)
            return;

        TimeSpan clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (_mediaSession.Info.Duration > TimeSpan.Zero && clamped > _mediaSession.Info.Duration)
            clamped = _mediaSession.Info.Duration;

        _mediaSession.Seek(clamped);
        OnMediaStateChanged();
    }

    private void SetVolume(double volume)
    {
        if (!CanSetVolume || _mediaSession is null)
            return;

        _mediaSession.Volume = Math.Clamp(volume, 0.0, 1.0);
        if (_mediaSession.Volume > 0 && _mediaSession.IsMuted)
            _mediaSession.IsMuted = false;
        OnMediaStateChanged();
    }

    private double GetResumeFraction(string path)
    {
        if (!_positions.TryGetValue(path, out ResumePoint? point) || point.Seconds <= 0)
            return 0;

        if (_currentPath == path && _mediaSession is { Info.Duration.TotalSeconds: > 0 } active)
            return Math.Clamp(point.Seconds / active.Info.Duration.TotalSeconds, 0.05, 0.98);

        return 0.2;
    }

    private void RefreshStageTiles()
    {
        if (!_theaterMode)
            return;

        if (_galleryPath is not null)
            _stageShell.SetFolderTiles(_galleryPath, _gallery.Entries, GetResumeFraction);
        else
            _stageShell.SetRecentTiles(_recents, GetResumeFraction);
    }

    private void SyncStageShell()
    {
        if (!_theaterMode)
            return;

        _stageShell.SyncState(
            CurrentView,
            _mediaSession,
            _currentPath is null ? null : Path.GetFileName(_currentPath),
            BuildStatus(),
            _galleryPath,
            canStepQueue: _queue.Entries.Count > 1,
            hasModalOverlay: _rootWindow.OwnedWindows.Count > 1);
    }

    private void CloseFile()
    {
        _requestedQueueItem = null;
        CancelPendingOpen();
        _retryPath = null;
        _galleryPath = null;
        _gallery.SetEntries(Array.Empty<BroadplayGalleryEntry>());
        ReleaseMedia();
        _currentPath = null;
        _title.Text = "Open media";
        _lastAction = "Closed";
        ShowView(BroadplayViewKind.Empty);
        RefreshStageTiles();
        RefreshUi();
        if (_theaterMode)
            _stageShell.FocusDefault();
        else
            _session.SetFocus(_openButton);
    }

    private void ShowAbout()
    {
        if (_theaterMode)
            _stageShell.FocusDefault();
        else if (ActiveMediaView is { } media)
            media.FocusTransport();
        else if (CurrentView == BroadplayViewKind.Gallery)
            _session.SetFocus(_gallery);
        else
            _session.SetFocus(_openButton);
        BroadplayAbout.Show(_rootWindow, _host.ViewportSize, typeof(BroadplayApp).Assembly, _openUrl);
    }
    private bool HandleOpenShortcut(UiInputEvent input)
    {
        if (input.Kind != UiInputEventKind.KeyboardKey ||
            input.KeyTransition != KeyboardKeyTransition.Down ||
            (input.KeyModifiers & KeyboardModifierState.Control) == KeyboardModifierState.None)
        {
            return false;
        }

        if (!IsKey(input, VirtualKeyO, "O"))
            return false;

        ShowOpenDialog();
        return true;
    }
    private static bool IsKey(UiInputEvent input, int virtualKey, string name) =>
        input.NativeKeyCode == virtualKey ||
        string.Equals(
            input.KeyName,
            "VirtualKey:" + virtualKey.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal) ||
        string.Equals(input.KeyName, name, StringComparison.OrdinalIgnoreCase);
    private void AdoptPath(string fullPath)
    {
        _currentPath = fullPath;
        _lastDirectory = Path.GetDirectoryName(fullPath) ?? _lastDirectory;
        _title.Text = Path.GetFileName(fullPath);
    }
    private void ShowView(BroadplayViewKind view)
    {
        if (view != BroadplayViewKind.Gallery)
        {
            _gallery.ClearThumbnails();
            _stageShell.ClearThumbnails();
        }
        _content.Show(view);
    }
    private static byte[] ReadSignature(string fullPath, out bool isEmpty)
    {
        using FileStream file = File.OpenRead(fullPath);
        var buffer = new byte[SignatureLength];
        int read = file.ReadAtLeast(buffer, SignatureLength, throwOnEndOfStream: false);
        isEmpty = read == 0;
        return read == SignatureLength ? buffer : buffer[..read];
    }
    private static bool IsFileOperationException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidDataException or OverflowException or IndexOutOfRangeException;

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=5DE95E
    // Broiler-Human:        PENDING
    private BRect GetDialogPlacement() => CenterInViewport(FileDialogPreferredSize, minTop: 42);
    private BRect CenterInViewport(BSize preferred, double minTop)
    {
        BSize viewport = _host.ViewportSize;
        double x = Math.Max(12, (viewport.Width - preferred.Width) / 2);
        double y = Math.Max(minTop, (viewport.Height - preferred.Height) / 2);
        return new BRect(
            x,
            y,
            Math.Min(preferred.Width, Math.Max(280, viewport.Width - 24)),
            Math.Min(preferred.Height, Math.Max(180, viewport.Height - (minTop + 22))));
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=698B87
    // Broiler-Falsified-If: a directory that no longer exists is handed to the file dialog as its starting directory
    // Broiler-Human:        PENDING
    private string GetDialogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            string? directory = Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                return directory;
        }

        return Directory.Exists(_lastDirectory) ? _lastDirectory : Environment.CurrentDirectory;
    }


    private void RefreshUi()
    {
        bool canClose = _currentPath is not null || _galleryPath is not null || _pendingOpen is not null;
        bool inGallery = CurrentView == BroadplayViewKind.Gallery;
        if (_closeMenuItem is not null) _closeMenuItem.IsEnabled = canClose;
        if (_closeButton is not null) _closeButton.IsEnabled = canClose;
        if (_galleryButton is not null)
            _galleryButton.Visibility = _galleryPath is not null && !inGallery ? UiVisibility.Visible : UiVisibility.Collapsed;
        if (_upFolderButton is not null)
        {
            _upFolderButton.Visibility = inGallery ? UiVisibility.Visible : UiVisibility.Collapsed;
            _upFolderButton.IsEnabled = _galleryPath is not null && Directory.GetParent(_galleryPath) is not null;
        }
        _status.Text = BuildStatus();
        SyncStageShell();
        _host.RequestInvalidate();
    }

    private string BuildStatus()
    {
        if (_openingName is not null)
            return $"Opening {_openingName}…";
        if (CurrentView == BroadplayViewKind.Gallery)
            return $"{_gallery.Entries.Count} items · {_lastAction}";
        if (_mediaSession is null || _lastAction.StartsWith("Could not", StringComparison.Ordinal))
            return _lastAction;
        if (!_mediaSession.Info.HasLivePlayback)
            return "Media information only · Playback is unavailable on this host.";
        if (_mediaSession.State == BroadplayPlaybackState.Paused && _lastAction.StartsWith("Paused:", StringComparison.Ordinal))
            return _lastAction;
        return _mediaSession.ErrorMessage ?? _mediaSession.State switch
        {
            BroadplayPlaybackState.Loading => "Opening media…",
            BroadplayPlaybackState.Ready => "Ready",
            BroadplayPlaybackState.Paused => "Paused",
            BroadplayPlaybackState.Ended => "Playback finished",
            BroadplayPlaybackState.Failed => "Playback failed. Choose another file.",
            _ => _mediaSession.State.ToString(),
        };
    }

    private sealed class BroadplayContent : UiElement
    {
        private readonly StandardMenu _menu;
        private readonly StandardToolbar _toolbar;
        private readonly StandardLabel _title;
        private readonly StandardLabel _status;
        private readonly UiElement[] _views;
        private bool _theater;

        public BroadplayContent(StandardMenu menu, StandardToolbar toolbar, StandardLabel title,
            StandardLabel empty, BroadplayGallery gallery, BroadplayMediaView audio, BroadplayMediaView video,
            StandardLabel status)
        {
            _menu = menu;
            _toolbar = toolbar;
            _title = title;
            _status = status;
            _views = [empty, gallery, audio, video];
            AddChild(menu);
            AddChild(toolbar);
            AddChild(title);
            foreach (UiElement view in _views)
                AddChild(view);
            AddChild(status);
        }

        public BroadplayViewKind CurrentView { get; private set; }
        private UiElement ActiveView => _views[(int)CurrentView];

        public void Show(BroadplayViewKind view)
        {
            ActiveView.Visibility = UiVisibility.Collapsed;
            CurrentView = view;
            ActiveView.Visibility = UiVisibility.Visible;
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }

        public void SetTheaterMode(bool enabled)
        {
            _theater = enabled;
            _menu.Visibility = _toolbar.Visibility = _title.Visibility = _status.Visibility =
                enabled ? UiVisibility.Collapsed : UiVisibility.Visible;
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }

        protected override BSize MeasureCore(BSize availableSize)
        {
            var size = new BSize(double.IsFinite(availableSize.Width) ? availableSize.Width : 760,
                double.IsFinite(availableSize.Height) ? availableSize.Height : 520);
            _menu.Measure(new BSize(size.Width, _menu.MenuBarHeight));
            _toolbar.Measure(new BSize(size.Width, _toolbar.PreferredSize.Height));
            _title.Measure(new BSize(size.Width, 28));
            _status.Measure(new BSize(size.Width, 24));
            ActiveView.Measure(size);
            return size;
        }

        protected override void ArrangeCore(BRect rect)
        {
            if (_theater)
            {
                ActiveView.Arrange(rect);
                return;
            }
            double toolbarTop = rect.Top + _menu.MenuBarHeight;
            double titleTop = toolbarTop + _toolbar.PreferredSize.Height + 8;
            double margin = rect.Width < 600 ? 8 : 16;
            double left = rect.Left + margin;
            double width = Math.Max(0, rect.Width - 2 * margin);
            double statusTop = Math.Max(titleTop + 28, rect.Bottom - 28);
            _menu.Arrange(new BRect(rect.Left, rect.Top, rect.Width, _menu.MenuBarHeight));
            _toolbar.Arrange(new BRect(rect.Left, toolbarTop, rect.Width, _toolbar.PreferredSize.Height));
            _title.Arrange(new BRect(left, titleTop, width, 28));
            ActiveView.Arrange(new BRect(left, titleTop + 28, width, statusTop - titleTop - 28));
            _status.Arrange(new BRect(left, statusTop, width, 24));
        }

        protected override void RenderCore(UiRenderContext context)
        {
            context.RenderList.FillRect(Bounds, BroadplayPalette.Canvas);
            if (!_theater)
            {
                _menu.Render(context);
                _toolbar.Render(context);
                _title.Render(context);
            }
            ActiveView.Render(context);
            if (!_theater)
                _status.Render(context);
        }
    }
}

internal enum BroadplayViewKind
{
    Empty,
    Gallery,
    Audio,
    Video,
}
