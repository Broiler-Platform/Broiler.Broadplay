// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.WebAssembly;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Input.Text;
using Broiler.UI;

namespace Broiler.Broadplay.WebAssembly;

[SupportedOSPlatform("browser")]
internal static partial class BrowserBroadplayApp
{
    private const string CanvasSelector = "#broadplay-canvas";
    private static readonly InputDeviceId PointerDevice = InputDeviceId.FromOpaqueValue("browser-primary-pointer");
    private static readonly InputDeviceId KeyboardDevice = InputDeviceId.FromOpaqueValue("browser-keyboard");
    private static readonly InputDeviceId TextDevice = InputDeviceId.FromOpaqueValue("browser-text");

    private static BrowserCanvasRenderer? _renderer;
    private static BrowserCanvasUiHost? _host;
    private static BrowserUiDispatcher? _dispatcher;
    private static BroadplayApp? _app;
    private static long _sequence;
    private static MouseButtons _buttons;

    internal static async Task StartAsync()
    {
        Environment.SetEnvironmentVariable("BROILER_IMAGE_DECODE_THREADS", "1");
        Environment.SetEnvironmentVariable("BROILER_RASTER_THREADS", "1");

        Broiler.Graphics.Imaging.BImageCodecs.Use(new Broiler.Media.MediaCodecCatalog(
            Broiler.Media.Image.Managed.ManagedImageCodecs.CreateCodecs()));

        await BrowserCanvasRenderer.LoadModuleAsync("../broiler.graphics.webassembly.js");

        var renderer = new BrowserCanvasRenderer();
        renderer.Initialize(CanvasSelector);
        _renderer = renderer;

        _dispatcher = new BrowserUiDispatcher(BrowserInterop.ScheduleFrame);
        _host = new BrowserCanvasUiHost(renderer);

        _app = new BroadplayApp(
            _host,
            requestClose: static () => { },
            openUrl: url => BrowserInterop.OpenUrl(url.AbsoluteUri),
            requestOpenFile: RequestOpenFile,
            requestOpenFolder: RequestOpenFolder,
            postToUi: action =>
            {
                if (_dispatcher is not { } dispatcher)
                    return false;
                dispatcher.Post(action);
                return true;
            },
            collectionJson: BrowserInterop.ReadCollection(),
            persistCollection: BrowserInterop.WriteCollection);

        _app.RenderFrame();
        BrowserInterop.Ready(_host.ViewportSize.Width, _host.ViewportSize.Height);
    }

    private static void RequestOpenFile()
    {
        string accept = BroadplayFileFormats.CreateDefault().CreateAcceptExtensions();
        BrowserInterop.RequestOpenFile(accept + ",.m3u,.m3u8");
    }

    private static void RequestOpenFolder()
    {
        BrowserInterop.RequestOpenFolder(BroadplayFileFormats.CreateDefault().CreateAcceptExtensions());
    }

    internal static void Resize(double width, double height, double dpr) =>
        _dispatcher?.Post(() =>
        {
            if (_host is not null && _host.Resize(width, height, dpr))
            {
                _app?.RootWindow.Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
            }
        });

    internal static void RenderFrame()
    {
        _dispatcher?.Drain();
        _app?.RenderFrame();
        var caret = _host?.CurrentCaret;
        BrowserInterop.PublishFrame(
            _host?.FrameIndex ?? 0,
            caret is not null,
            caret?.Bounds.X ?? 0,
            caret?.Bounds.Y ?? 0,
            caret?.Bounds.Width ?? 0,
            caret?.Bounds.Height ?? 0,
            caret?.CaretIndex ?? 0,
            caret?.SelectionStart ?? 0,
            caret?.SelectionLength ?? 0,
            _app?.LastAction ?? string.Empty,
            false);
    }

    internal static void PointerMove(double x, double y, int buttons, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseMove(new MouseMoveEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons)));
        });

    internal static void PointerButton(double x, double y, int buttons, int domButton, bool down, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseButton(new MouseButtonEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons,
                MapButton(domButton),
                down ? MouseButtonTransition.Down : MouseButtonTransition.Up)));
        });

    internal static void PointerWheel(double x, double y, int buttons, bool horizontal, double deltaNotches, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            _buttons = (MouseButtons)buttons;
            _app?.Dispatch(UiInputEvent.FromMouseWheel(new MouseWheelEvent(
                Header(PointerDevice, timestampMilliseconds),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                _buttons,
                horizontal ? MouseWheelAxis.Horizontal : MouseWheelAxis.Vertical,
                deltaNotches)));
        });

    internal static void KeyboardKey(string keyName, bool down, int modifiers, int nativeKeyCode, bool repeat, int location, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            var modifierState = (KeyboardModifierState)modifiers;
            var input = new KeyboardKeyEvent(
                Header(KeyboardDevice, timestampMilliseconds),
                Broiler.Input.Keyboard.KeyboardKey.FromName(keyName),
                down ? KeyboardKeyTransition.Down : KeyboardKeyTransition.Up,
                modifierState,
                nativeKeyCode,
                ScanCode: 0,
                RepeatCount: repeat ? 2 : 1,
                IsExtended: location != 0,
                WasDown: repeat,
                Location: Enum.IsDefined((KeyboardKeyLocation)location) ? (KeyboardKeyLocation)location : KeyboardKeyLocation.Standard);

            _app?.Dispatch(UiInputEvent.FromKeyboardKey(input));
        });

    internal static void TextInput(string text, double timestampMilliseconds) =>
        _dispatcher?.Post(() =>
        {
            if (string.IsNullOrEmpty(text))
                return;

            _app?.Dispatch(UiInputEvent.FromTextInput(new TextInputEvent(
                Header(TextDevice, timestampMilliseconds),
                text)));
        });

    internal static void TextComposition(string text, int state, double timestamp) =>
        _dispatcher?.Post(() =>
        {
            if (_app is null) return;
            _app.Dispatch(UiInputEvent.FromTextComposition(new TextCompositionEvent(
                Header(TextDevice, timestamp), text, (TextCompositionState)state, 0, 0)));
        });

    internal static void CancelPointer(double timestamp) =>
        _dispatcher?.Post(() =>
        {
            if (_app is null) return;
            var outside = InputPoint.ClientDeviceIndependentPixels(-1, -1);
            _app.Dispatch(UiInputEvent.FromMouseMove(new MouseMoveEvent(Header(PointerDevice, timestamp), outside, _buttons)));
            for (int button = 0; button < 5; button++)
            {
                if ((_buttons & (MouseButtons)(button == 1 ? 4 : button == 2 ? 2 : 1 << button)) == 0) continue;
                _app.Dispatch(UiInputEvent.FromMouseButton(new MouseButtonEvent(Header(PointerDevice, timestamp), outside, MouseButtons.None, MapButton(button), MouseButtonTransition.Up)));
            }
            _buttons = MouseButtons.None;
            if (_app.Session.CapturedElement is UiElement captured) _app.Session.ReleaseInputCapture(captured);
        });

    internal static string ClipboardEvent(string operation, string text)
    {
        _dispatcher?.Drain();
        if (_host is null || _app is null) return string.Empty;
        _host.BeginClipboardEvent(operation == "paste" ? text : null);
        try
        {
            return _host.EndClipboardEvent();
        }
        finally
        {
            _host.EndClipboardEvent();
            BrowserInterop.ScheduleFrame();
        }
    }

    internal static void LoadMedia(string[] fileNames, string[] base64Datas) =>
        _dispatcher?.Post(() =>
        {
            try
            {
                if (_app is null) return;
                var paths = new System.Collections.Generic.List<string>();
                string selection = Path.Combine(Path.GetTempPath(), "broadplay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(selection);
                for (int i = 0; i < fileNames.Length && i < base64Datas.Length; i++)
                {
                    string name = Path.GetFileName(fileNames[i]);
                    if (name is "" or "." or "..")
                        continue;
                    string path = Path.Combine(selection, name);
                    if (File.Exists(path))
                    {
                        string duplicate = Path.Combine(selection, Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(duplicate);
                        path = Path.Combine(duplicate, name);
                    }
                    File.WriteAllBytes(path, Convert.FromBase64String(base64Datas[i]));
                    paths.Add(path);
                }
                _app.Intake(paths, QueueIntake.PlayNow);
                BrowserInterop.ReportOperation("Loaded", _app.LastAction);
            }
            catch (Exception ex)
            {
                BrowserInterop.ReportOperation("Failed", $"Failed to load selected media: {ex.Message}");
            }
        });

    internal static void CancelReselect() => _dispatcher?.Post(() => _app?.CancelReselect());

    internal static void LoadFolder(string folderName, string[] fileNames, string[] base64Datas) =>
        _dispatcher?.Post(() =>
        {
            try
            {
                if (_app is null) return;
                string nameOfFolder = Path.GetFileName(folderName);
                if (nameOfFolder is "" or "." or "..")
                    nameOfFolder = "Selected folder";
                string folderPath = Path.Combine(Path.GetTempPath(), "broadplay-" + Guid.NewGuid().ToString("N"), nameOfFolder);
                Directory.CreateDirectory(folderPath);

                for (int i = 0; i < fileNames.Length && i < base64Datas.Length; i++)
                {
                    byte[] bytes = Convert.FromBase64String(base64Datas[i]);
                    string name = Path.GetFileName(fileNames[i]);
                    if (name is "" or "." or "..")
                        continue;
                    string filePath = Path.Combine(folderPath, name);
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(filePath, bytes);
                }

                _app.OpenFolder(folderPath);
                BrowserInterop.ReportOperation("Loaded", $"Opened folder {folderName} ({fileNames.Length} files)");
            }
            catch (Exception ex)
            {
                BrowserInterop.ReportOperation("Failed", $"Failed to open folder {folderName}: {ex.Message}");
            }
        });

    internal static void Dispose()
    {
        _dispatcher = null;
        _app?.Dispose();
        _app = null;
        _host?.Dispose();
        _host = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    private static InputEventHeader Header(InputDeviceId device, double timestampMilliseconds) =>
        new(device, new InputTimestamp((long)Math.Max(0, timestampMilliseconds * 1000), 1_000_000, "browser-performance"), ++_sequence);

    private static MouseButton MapButton(int domButton) => domButton switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        3 => MouseButton.X1,
        4 => MouseButton.X2,
        _ => MouseButton.None,
    };
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserBroadplayExports
{
    [JSExport]
    internal static void ResizeUi(double logicalWidth, double logicalHeight, double dpr) =>
        BrowserBroadplayApp.Resize(logicalWidth, logicalHeight, dpr);

    [JSExport]
    internal static void RenderUiFrame() => BrowserBroadplayApp.RenderFrame();

    [JSExport]
    internal static void UiPointerMove(double x, double y, int buttons, double timestampMilliseconds) =>
        BrowserBroadplayApp.PointerMove(x, y, buttons, timestampMilliseconds);

    [JSExport]
    internal static void UiPointerButton(double x, double y, int buttons, int button, bool down, double timestampMilliseconds) =>
        BrowserBroadplayApp.PointerButton(x, y, buttons, button, down, timestampMilliseconds);

    [JSExport]
    internal static void UiPointerWheel(double x, double y, int buttons, bool horizontal, double deltaNotches, double timestampMilliseconds) =>
        BrowserBroadplayApp.PointerWheel(x, y, buttons, horizontal, deltaNotches, timestampMilliseconds);

    [JSExport]
    internal static void UiKeyboardKey(string keyName, bool down, int modifiers, int nativeKeyCode, bool repeat, int location, double timestampMilliseconds) =>
        BrowserBroadplayApp.KeyboardKey(keyName, down, modifiers, nativeKeyCode, repeat, location, timestampMilliseconds);

    [JSExport]
    internal static void UiTextInput(string text, double timestampMilliseconds) =>
        BrowserBroadplayApp.TextInput(text, timestampMilliseconds);

    [JSExport]
    internal static void UiTextComposition(string text, int state, double timestamp) =>
        BrowserBroadplayApp.TextComposition(text, state, timestamp);

    [JSExport]
    internal static void UiCancelPointer(double timestamp) =>
        BrowserBroadplayApp.CancelPointer(timestamp);

    [JSExport]
    internal static string UiClipboardEvent(string operation, string text) =>
        BrowserBroadplayApp.ClipboardEvent(operation, text);

    [JSExport]
    internal static void LoadMedia(string[] fileNames, string[] base64Datas) =>
        BrowserBroadplayApp.LoadMedia(fileNames, base64Datas);

    [JSExport]
    internal static void CancelReselect() => BrowserBroadplayApp.CancelReselect();

    [JSExport]
    internal static void LoadFolder(string folderName, string[] fileNames, string[] base64Datas) =>
        BrowserBroadplayApp.LoadFolder(folderName, fileNames, base64Datas);

    [JSExport]
    internal static void Dispose() => BrowserBroadplayApp.Dispose();
}
