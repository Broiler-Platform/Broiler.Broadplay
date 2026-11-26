// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.IO;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Dialog.Stage;
using Broiler.UI.Stage;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayStageTests : IDisposable
{
    private readonly string _tempDir;

    public BroadplayStageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "broadplay-stage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void StartupFlags_F11_And_MenuCommand_ToggleStageModeFromAnyView()
    {
        var (winStage, winPaths) = BroadplayWindow.ParseWindowsStartupArgs(["--stage", "clip.mp4"]);
        Assert.True(winStage);
        Assert.Equal(["clip.mp4"], winPaths);

        using var host = CreateUiHost();
        using var initialStageApp = new BroadplayApp(host, static () => { }, initialStageMode: true);
        initialStageApp.RenderFrame();
        Assert.True(initialStageApp.IsStageMode);
        Assert.Equal(UiFocusVisibilityPolicy.Always, initialStageApp.Session.FocusVisibilityPolicy);
        Assert.Equal(UiVisibility.Visible, initialStageApp.StageShell.Visibility);

        using var app = new BroadplayApp(host, static () => { });
        app.RenderFrame();
        Assert.False(app.IsStageMode);
        Assert.Equal(UiFocusVisibilityPolicy.Auto, app.Session.FocusVisibilityPolicy);

        // Enter Stage mode from Empty view via F11
        app.Dispatch(KeyDown(0x7A, "F11"));
        app.RenderFrame();
        Assert.True(app.IsStageMode);
        Assert.Same(app.StageShell.OpenMediaButton, app.Session.FocusedElement);

        // Exit Stage mode via F11
        app.Dispatch(KeyDown(0x7A, "F11"));
        app.RenderFrame();
        Assert.False(app.IsStageMode);

        // Enter & exit via View > Stage mode command
        Assert.True(app.Menu.CommandDispatcher!.TryExecute("view.stage"));
        Assert.True(app.IsStageMode);
        Assert.True(app.Menu.CommandDispatcher.TryExecute("view.theater"));
        Assert.False(app.IsStageMode);
    }

    [Fact]
    public void StageGallery_DPadNavigatesTiles_OpensMediaInStage_AndBacksOutHierarchically()
    {
        string trackA = Path.Combine(_tempDir, "01-alpha.wav");
        string trackB = Path.Combine(_tempDir, "02-bravo.wav");
        File.WriteAllBytes(trackA, CreateWavBytes());
        File.WriteAllBytes(trackB, CreateWavBytes());

        using var host = CreateUiHost();
        using var app = new BroadplayApp(host, static () => { }, initialStageMode: true);
        Assert.True(app.OpenFolder(_tempDir));
        app.RenderFrame();

        Assert.True(app.IsStageMode);
        Assert.Equal(BroadplayViewKind.Gallery, app.CurrentView);
        Assert.Equal(2, app.StageShell.TileGrid.Items.Count);
        Assert.Same(app.StageShell.TileGrid, app.Session.FocusedElement);
        Assert.Equal(0, app.StageShell.TileGrid.SelectedIndex);

        // D-pad Right moves to second tile
        app.Dispatch(KeyDown(0x27, "Right"));
        Assert.Equal(1, app.StageShell.TileGrid.SelectedIndex);

        // D-pad Up from top row moves spatially to header action bar, and Down returns to tile grid
        app.Dispatch(KeyDown(0x26, "Up"));
        Assert.NotSame(app.StageShell.TileGrid, app.Session.FocusedElement);
        app.Dispatch(KeyDown(0x28, "Down"));
        Assert.Same(app.StageShell.TileGrid, app.Session.FocusedElement);

        // Activate selected tile -> enters Audio playback while staying in Stage mode
        app.Dispatch(KeyDown(0x0D, "Enter"));
        app.RenderFrame();
        Assert.True(app.IsStageMode);
        Assert.Equal(BroadplayViewKind.Audio, app.CurrentView);
        Assert.Equal(UiVisibility.Visible, app.StageShell.MediaStage.Visibility);

        // First Escape returns from playback to the Stage folder browser
        app.Dispatch(KeyDown(0x1B, "Escape"));
        app.RenderFrame();
        Assert.True(app.IsStageMode);
        Assert.Equal(BroadplayViewKind.Gallery, app.CurrentView);
        Assert.Same(app.StageShell.TileGrid, app.Session.FocusedElement);

        // Second Escape exits Stage mode back to desktop view
        app.Dispatch(KeyDown(0x1B, "Escape"));
        app.RenderFrame();
        Assert.False(app.IsStageMode);
    }

    [Fact]
    public void StageFolderDialog_StaysInCanvasEvenOnWindowHost_AndOpensSelectedFolder()
    {
        string subDir = Path.Combine(_tempDir, "Movies");
        Directory.CreateDirectory(subDir);
        File.WriteAllBytes(Path.Combine(subDir, "feature.wav"), CreateWavBytes());

        using var host = new CountingWindowHost();
        using var app = new BroadplayApp(host, static () => { }, initialStageMode: true);
        Assert.True(app.OpenFolder(_tempDir));
        app.RenderFrame();

        app.StageShell.OpenFolderButton.Click();
        app.RenderFrame();

        // StageDialog must stay in-canvas (Manual break-out) even when IUiWindowHost is offered
        Assert.Equal(0, host.BreakOutCount);
        Assert.Equal(2, app.RootWindow.OwnedWindows.Count);
        var stageDialog = Assert.IsType<StageDialog>(app.RootWindow.OwnedWindows[1]);
        var picker = Assert.IsType<BroadplayStageFolderDialog>(stageDialog.Children[0]);

        // Enter Movies subfolder and accept
        Assert.Same(picker.Folders, app.Session.FocusedElement);
        app.Dispatch(KeyDown(0x0D, "Enter"));
        Assert.Equal(Path.GetFullPath(subDir), Path.GetFullPath(picker.CurrentDirectory));

        picker.OpenFolderButton.Click();
        app.RenderFrame();

        Assert.Single(app.RootWindow.OwnedWindows);
        Assert.Single(app.StageShell.TileGrid.Items);
        Assert.Equal("feature.wav", app.StageShell.TileGrid.Items[0].Title);
    }

    [Fact]
    public void StagePlaybackOsd_AutoHidesAfterIdle_AndRevealsOnUserActivity()
    {
        var clock = new ManualUiClock();
        using var mediaHost = new LiveVideoStubHost();
        using var host = CreateUiHost();
        using var app = new BroadplayApp(host, static () => { }, mediaHost: mediaHost, initialStageMode: true, clock: clock);

        byte[] mp4Header = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0];
        Assert.True(app.Open("cinema.mp4", mp4Header));
        mediaHost.LastSession!.Play();
        app.RenderFrame();

        Assert.True(app.StageShell.MediaStage.IsChromeVisible);
        Assert.True(app.WantsTick);

        // Advance past CinemaDark.IdleConcealDelay (3.5s)
        clock.Advance(TimeSpan.FromSeconds(4));
        app.AnimationTick();
        app.RenderFrame();
        Assert.False(app.StageShell.MediaStage.IsChromeVisible);

        // Any D-pad / keyboard press wakes the OSD immediately
        app.Dispatch(KeyDown(0x27, "Right"));
        app.RenderFrame();
        Assert.True(app.StageShell.MediaStage.IsChromeVisible);

        // Pausing pins the OSD visible even after idle timeout
        mediaHost.LastSession.Pause();
        clock.Advance(TimeSpan.FromSeconds(5));
        app.AnimationTick();
        Assert.True(app.StageShell.MediaStage.IsChromeVisible);
    }

    private static BroadplayUiHost CreateUiHost() =>
        new(() => new BSize(1280, 720), () => 1.0, static () => { }, static _ => { });

    private static UiInputEvent KeyDown(int vk, string keyName) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            new InputEventHeader(InputDeviceId.FromOpaqueValue("kbd"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "test"), 1),
            KeyboardKey.FromName(keyName),
            KeyboardKeyTransition.Down,
            KeyboardModifierState.None,
            vk,
            0,
            0,
            false,
            false,
            Source: InputEventSource.Synthetic));

    private static byte[] CreateWavBytes()
    {
        const int sampleRate = 8000;
        const int frames = 800;
        byte[] wav = new byte[44 + (frames * 2)];
        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(36 + (frames * 2)));
        "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
        "fmt "u8.CopyTo(wav.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), sampleRate * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), 16);
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), frames * 2);
        return wav;
    }

    private sealed class ManualUiClock : IUiClock
    {
        public UiTimestamp Now { get; private set; }

        public void Advance(TimeSpan delta) => Now = new UiTimestamp(Now.Elapsed + delta);
    }

    private sealed class CountingWindowHost : BroadplayUiHost, IUiWindowHost
    {
        public CountingWindowHost()
            : base(() => new BSize(1280, 720), () => 1.0, static () => { }, static _ => { })
        {
        }

        public int BreakOutCount { get; private set; }

        public IUiHostWindow CreateHostWindow(UiHostWindowRequest request)
        {
            BreakOutCount++;
            throw new InvalidOperationException("Stage dialogs must not break out into desktop host windows.");
        }
    }

    private sealed class LiveVideoStubHost : IBroadplayMediaHost
    {
        public LiveVideoStubSession? LastSession { get; private set; }
        public bool SupportsAudio => true;
        public bool SupportsVideo => true;

        public bool TryOpen(string path, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error)
        {
            error = null;
            LastSession = new LiveVideoStubSession(kind);
            session = LastSession;
            return true;
        }

        public bool TryOpenBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error) =>
            TryOpen(name, kind, out session, out error);

        public void Dispose() { }
    }

    private sealed class LiveVideoStubSession(BroadplayMediaKind kind) : IBroadplayMediaSession
    {
        public BroadplayMediaInfo Info { get; } = new(kind, "MP4", TimeSpan.FromMinutes(2), HasLivePlayback: true, VideoWidth: 1920, VideoHeight: 1080);
        public BroadplayPlaybackState State { get; private set; } = BroadplayPlaybackState.Ready;
        public bool ReportsStateChanges => true;
        public bool CanSeek => true;
        public bool CanSetVolume => true;
        public TimeSpan Position { get; private set; } = TimeSpan.FromSeconds(10);
        public double Volume { get; set; } = 0.8;
        public bool IsMuted { get; set; }
        public string? ErrorMessage => null;
        public BRect Viewport { get; private set; }
        public bool VideoVisible { get; private set; }
        public event EventHandler? StateChanged;

        public void Play()
        {
            State = BroadplayPlaybackState.Playing;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            State = BroadplayPlaybackState.Paused;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            State = BroadplayPlaybackState.Stopped;
            Position = TimeSpan.Zero;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position) => Position = position;

        public void SetVideoViewport(BRect clientRect, bool visible)
        {
            Viewport = clientRect;
            VideoVisible = visible;
        }

        public void Dispose() { }
    }
}
