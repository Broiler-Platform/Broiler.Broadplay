// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   21
// Annotated:        21/21
// Exempt:           4
// Human-reviewed:   0/21
// IP risk:          Low
// Security risk:    Critical
// Criteria:         18/8
// Resource impact:  7/10 max
// Unverified:       21
//
// GENERATED - DO NOT EDIT MANUALLY

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Hosting.Windows.Accessibility;
using Broiler.Input.Keyboard;
using Broiler.Native.Windows;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Broadplay;

/// <summary>Win32/Direct2D host for the platform-neutral viewer.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Critical; Resources=7; Fingerprint=1FD40B
// Broiler-Falsified-If: a paste requested before OnCreated has bound the clipboard reaches WindowsClipboard instead of answering null
// Broiler-Human:        PENDING
[SupportedOSPlatform("windows7.0")]
internal sealed partial class BroadplayWindow : Direct2DWindow
{
    private const int AppIconPixels = 64;

    private readonly BroadplayWindowsUiHost _host;
    private readonly WindowsMediaHost _mediaHost;
    private readonly BroadplayApp _app;
    private WindowsAutomationBridge? _automation;
    private WindowsAudioOutputMonitor? _audioOutput;
    private readonly string[] _initialPaths;
    private readonly bool _initialStageMode;
    private WindowNative.RECT _windowedBounds;
    private int _windowedStyle;
    private bool _wasMaximized;
    private bool _displayRequired;

    // Created in OnCreated: the Win32 clipboard is addressed by window handle,
    // and there is no handle until the window exists.
    private WindowsClipboard? _clipboard;
    private BroadplayWindowIcon? _icon;

#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _legacyInput = new("broiler-broadplay");
#pragma warning restore CS0618

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=2; Fingerprint=53CC84
    // Broiler-Falsified-If: the viewer's host is built without the clipboard accessors, so Ctrl+V in the Open dialog finds nothing while the system clipboard holds text
    // Broiler-Human:        PENDING
    public BroadplayWindow(params string[] initialPaths)
        : base(new BWindowOptions
        {
            Title = "Broiler Broadplay",
            ClientWidth = 1120,
            ClientHeight = 780,
            ClearColor = BroadplayPalette.Canvas,
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
        })
    {
        // The Windows host, not the plain one: it offers IUiWindowHost, so the Open dialog
        // breaks out into its own OS window instead of rendering inside this one.
        _host = new BroadplayWindowsUiHost(
            () => ClientSize,
            () => DpiScale,
            Invalidate,
            static _ => { },
            () => this,
            ReadClipboardText,
            WriteClipboardText,
            getRenderer: () => Renderer,
            setFullscreen: SetFullscreen,
            setKeepScreenAwake: SetDisplayRequired);
        _mediaHost = new WindowsMediaHost(() => NativeHandle, () => DpiScale, PostToUiThread);
        _app = new BroadplayApp(_host, CloseNativeWindow, setTicking: SetTicking, mediaHost: _mediaHost,
            postToUi: PostToUiThread, setFullscreen: _host.TrySetFullscreen, collectionPath: CollectionState.DesktopPath);
        (_initialStageMode, _initialPaths) = ParseWindowsStartupArgs(initialPaths);
    }

    internal static (bool StageMode, string[] Paths) ParseWindowsStartupArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
            return (false, []);

        bool stageMode = false;
        var paths = new System.Collections.Generic.List<string>(args.Length);
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--stage", StringComparison.OrdinalIgnoreCase))
                stageMode = true;
            else if (!string.IsNullOrWhiteSpace(arg))
                paths.Add(arg);
        }

        return (stageMode, paths.ToArray());
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=3B311F
    // Broiler-Falsified-If: the clipboard is bound to a zero window handle, so SetClipboardData fails after EmptyClipboard has already cleared the system clipboard
    // Broiler-Human:        PENDING
    protected override void OnCreated()
    {
        _clipboard = new WindowsClipboard(NativeHandle);
        _automation = new WindowsAutomationBridge(RenderNativeHandle, _app.Session, _app.RootWindow, () => DpiScale);
        _audioOutput = WindowsAudioOutputMonitor.TryStart(() => PostToUiThread(() =>
            _app.PauseForSystemChange("Paused: Windows changed the audio output. Check the output and press Play.")));

        // The taskbar, Alt+Tab, caption and its system menu show the window's icon,
        // so the icon the build embeds (icon-96.ico) is handed to the window.
        // When running under a host without the embedded resource, the fallback
        // icon from Broadplay's assets is used.
        _icon = BroadplayWindowIcon.TryAttach(NativeHandle);
        if (_icon is null)
            SetIcon(BroadplayApp.CreateAppIcon(AppIconPixels));
        DragAcceptFiles(NativeHandle, 1);
        if (_initialStageMode)
            _app.SetStageMode(true);
        if (_initialPaths.Length == 1 && System.IO.Directory.Exists(_initialPaths[0]))
            _app.OpenFolder(_initialPaths[0]);
        else if (_initialPaths.Length > 0)
            _app.Intake(_initialPaths, QueueIntake.PlayNow);
    }

    /// <summary>
    /// Null rather than empty when there is no clipboard to read: the host
    /// treats that as "this machine offers none", which is what the window
    /// reports before it exists and if the OS refuses the clipboard.
    /// </summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=4; Fingerprint=ABF7C4
    // Broiler-Falsified-If: a paste requested before OnCreated has bound the clipboard reaches WindowsClipboard instead of answering null
    // Broiler-Human:        PENDING
    private string? ReadClipboardText() =>
        _clipboard is not null && _clipboard.TryGetText(out string text) ? text : null;

    // Broiler-AI:           Origin=AI; IP=None; Security=Critical; Resources=4; Fingerprint=53E442
    // Broiler-Falsified-If: a copy requested before OnCreated has bound the clipboard throws NullReferenceException instead of being dropped
    // Broiler-Human:        PENDING
    private void WriteClipboardText(string text) => _clipboard?.SetText(text);

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=322E78
    // Broiler-Human:        PENDING
    private const int TooltipTickMilliseconds = 33;

    private void SetTicking(bool ticking)
    {
        if (ticking)
            StartAnimationTimer(TooltipTickMilliseconds);
        else
            StopAnimationTimer();
    }

    protected override void OnAnimationTick()
    {
        _app.AnimationTick();
        SyncDisplayRequest();
    }

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        SyncDisplayRequest();
        return _app.RenderFrame();
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=C778FE
    // Broiler-Falsified-If: a resize is not followed by an invalidation, so the viewer keeps drawing the layout of the previous client size
    // Broiler-Human:        PENDING
    protected override void OnResized(BSize clientSize, double dpiScale) => _app.Invalidate();

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=591E77
    // Broiler-Falsified-If: a mouse button press in the main window reaches BroadplayApp.Dispatch as anything other than a pointer-button event, so the menu and toolbar never respond
    // Broiler-Human:        PENDING
    protected override void OnPointerDown(BPointerEventArgs e) =>
        Dispatch(_legacyInput.FromPointerButton(e));

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=7448EF
    // Broiler-Falsified-If: pointer motion in the main window is delivered as a button event, so moving over the toolbar presses its buttons
    // Broiler-Human:        PENDING
    protected override void OnPointerMove(BPointerEventArgs e) =>
        Dispatch(_legacyInput.FromPointerMove(e));

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=C0302C
    // Broiler-Falsified-If: a button release in the main window is delivered as a pointer move, so a pressed toolbar button never completes its click
    // Broiler-Human:        PENDING
    protected override void OnPointerUp(BPointerEventArgs e) =>
        Dispatch(_legacyInput.FromPointerButton(e));

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=4D9946
    // Broiler-Falsified-If: a Ctrl+wheel notch reaches BroadplayApp.Dispatch without its notch count, so it neither scrolls the document nor steps the zoom
    // Broiler-Human:        PENDING
    protected override void OnMouseWheel(BMouseWheelEventArgs e) =>
        Dispatch(_legacyInput.FromMouseWheel(e));

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=EBC72C
    // Broiler-Falsified-If: a key press is converted with KeyboardKeyTransition.Up, so Ctrl+O and the Ctrl+plus zoom shortcut never fire
    // Broiler-Human:        PENDING
    protected override void OnKeyDown(BKeyEventArgs e) =>
        Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Down));

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=E0727F
    // Broiler-Falsified-If: a key release is converted with KeyboardKeyTransition.Down, so one Ctrl+plus keystroke steps the zoom twice
    // Broiler-Human:        PENDING
    protected override void OnKeyUp(BKeyEventArgs e) =>
        Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Up));

    // Forwarded even though nothing here is editable: the Open dialog's name box is.
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=499FE8
    // Broiler-Falsified-If: a character typed in the main window is dropped instead of reaching BroadplayApp.Dispatch as text input
    // Broiler-Human:        PENDING
    protected override void OnTextInput(BTextInputEventArgs e) =>
        Dispatch(_legacyInput.FromText(e));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=2; Fingerprint=AE072C
    // Broiler-Falsified-If: the host is disposed before the app, so a dialog still open at shutdown is torn down by the host's sweep rather than by the session that presented it
    // Broiler-Human:        PENDING
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The app first: disposing its session closes any dialog that is still open, which
            // tears down the host window it broke out into, and releases the image the viewer
            // had uploaded. The host then only has to sweep up whatever survived that.
            _icon?.Dispose();
            _icon = null;
            _automation?.Dispose();
            _audioOutput?.Dispose();
            SetDisplayRequired(false);
            _app.Dispose();
            _mediaHost.Dispose();
            _host.Dispose();
        }

        base.Dispose(disposing);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=CE27BE
    // Broiler-Falsified-If: a close requested while the window is open destroys it inside the command that asked, instead of after that input has been dispatched
    // Broiler-Human:        PENDING
    private void CloseNativeWindow()
    {
        if (!PostToUiThread(CloseNativeWindowNow))
            CloseNativeWindowNow();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=698F16
    // Broiler-Falsified-If: DestroyWindow is called with a zero or already-destroyed window handle
    // Broiler-Human:        PENDING
    private void CloseNativeWindowNow()
    {
        if (NativeHandle != IntPtr.Zero)
            _ = WindowNative.DestroyWindow(NativeHandle);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=7; Fingerprint=6A9571
    // Broiler-Human:        PENDING
    private void Dispatch(UiInputEvent input) => _app.Dispatch(input);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=BC3127
    // Broiler-Human:        PENDING
    protected override void OnNativeWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        base.OnNativeWindowMessage(hwnd, message, wParam, lParam);
        const uint wmCancelMode = 0x001F;
        const uint wmCaptureChanged = 0x0215;
        if ((hwnd == NativeHandle || hwnd == RenderNativeHandle) &&
            message is wmCancelMode or wmCaptureChanged && _app.Session.CapturedElement is { } captured)
            _app.Session.ReleaseInputCapture(captured);

        if (hwnd == NativeHandle)
        {
            if (message == 0x0233)
                AcceptDrop(wParam);
            WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
            const uint wmPowerBroadcast = 0x0218;
            const int powerSuspend = 4;
            if (message == wmPowerBroadcast && wParam.ToInt64() == powerSuspend)
            {
                _app.PauseForSystemChange("Paused: Windows suspended. Press Play to resume.");
                SetDisplayRequired(false);
            }
            else if (message == 0x0005 && wParam.ToInt64() == 1)
            {
                SetDisplayRequired(false);
            }
            // Transfer Win32 focus from the top-level frame to the render child window
            // on activation and focus so keystrokes are never dropped.
            if ((message == 0x0006 && wParam.ToInt64() != 0) || message == 0x0007)
            {
                if (RenderNativeHandle != IntPtr.Zero)
                    _ = WindowNative.SetFocus(RenderNativeHandle);
            }
        }
    }

    private unsafe void AcceptDrop(nint drop)
    {
        try
        {
            uint count = Math.Min(1024, DragQueryFile(drop, uint.MaxValue, null, 0));
            var paths = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(drop, i, null, 0);
                var buffer = new char[length + 1];
                fixed (char* chars = buffer)
                    paths[i] = new string(chars, 0, (int)DragQueryFile(drop, i, chars, length + 1));
            }
            _app.Intake(paths, QueueIntake.PlayNow);
        }
        finally
        {
            DragFinish(drop);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void DragAcceptFiles(nint hwnd, int accept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    private static unsafe partial uint DragQueryFile(nint drop, uint index, char* path, uint length);

    [LibraryImport("shell32.dll")]
    private static partial void DragFinish(nint drop);

    private bool SetFullscreen(bool enabled)
    {
        nint hwnd = NativeHandle;
        if (hwnd == 0)
            return false;

        const int styleIndex = -16;
        const uint frameChangedNoActivateNoZOrder = 0x0034;
        if (enabled)
        {
            nint monitor = WindowNative.MonitorFromWindow(hwnd, 2);
            var info = new WindowNative.MONITORINFO { CbSize = (uint)Marshal.SizeOf<WindowNative.MONITORINFO>() };
            if (monitor == 0 || !WindowNative.GetMonitorInfo(monitor, ref info))
                return false;
            _wasMaximized = WindowNative.IsZoomed(hwnd);
            if (_wasMaximized)
                WindowNative.ShowWindow(hwnd, 9);
            if (!WindowNative.GetWindowRect(hwnd, out _windowedBounds))
                return false;
            _windowedStyle = WindowNative.GetWindowLong32(hwnd, styleIndex);
            _ = WindowNative.SetWindowLong32(hwnd, styleIndex, _windowedStyle & ~(int)WindowNative.WsOverlappedWindow);
            if (!Move(info.RcMonitor))
            {
                _ = WindowNative.SetWindowLong32(hwnd, styleIndex, _windowedStyle);
                _ = Move(_windowedBounds);
                if (_wasMaximized)
                    WindowNative.ShowWindow(hwnd, 3);
                return false;
            }
        }
        else
        {
            _ = WindowNative.SetWindowLong32(hwnd, styleIndex, _windowedStyle);
            _ = Move(_windowedBounds);
            if (_wasMaximized)
                WindowNative.ShowWindow(hwnd, 3);
        }

        return true;

        bool Move(WindowNative.RECT rect) => WindowNative.SetWindowPos(hwnd, 0, rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top, frameChangedNoActivateNoZOrder);
    }

    private void SyncDisplayRequest() =>
        SetDisplayRequired(NativeHandle != 0 && !WindowNative.IsIconic(NativeHandle) && _app.IsPlayingVideo);

    private void SetDisplayRequired(bool required)
    {
        if (_displayRequired == required)
            return;
        const uint continuous = 0x80000000;
        const uint displayRequired = 0x00000002;
        if (SetThreadExecutionState(continuous | (required ? displayRequired : 0)) != 0)
            _displayRequired = required;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint executionState);
}
