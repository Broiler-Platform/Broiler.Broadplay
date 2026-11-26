// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Broiler.Graphics.Geometry;
using Broiler.UI.AboutDialog.Standard;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayOpenLifecycleTests
{
    [Fact]
    public void QueueAdvancesOnceAndStopsAfterEachFailedCandidateHasBeenTried()
    {
        using var dispatcher = new BlockingCollection<Action>();
        using var host = new DeferredHost();
        using var ui = CreateUi();
        using var app = new BroadplayApp(ui, () => { }, mediaHost: host, postToUi: dispatcher.TryAdd);
        app.Queue.Repeat = QueueRepeat.All;
        app.Intake(["first.wav", "second.wav", "third.wav"], QueueIntake.PlayNow);
        var first = new TestSession();
        host.Opens["first"].Result.SetResult(new(first, null));
        DispatchNext(dispatcher); // adopt
        DispatchNext(dispatcher); // playing
        Assert.Equal(BroadplayPlaybackState.Playing, first.State);
        first.Stop();
        DispatchNext(dispatcher);
        Assert.Single(host.Opens);

        first.ChangeState(BroadplayPlaybackState.Ended);
        first.ChangeState(BroadplayPlaybackState.Ended);
        DispatchNext(dispatcher);
        DispatchNext(dispatcher);
        Assert.Equal(2, host.Opens.Count);
        host.Opens["second"].Result.SetResult(new(null, "second failed"));
        DispatchNext(dispatcher);
        host.Opens["third"].Result.SetResult(new(null, "third failed"));
        DispatchNext(dispatcher);
        Assert.Equal(3, host.Opens.Count);
        Assert.Same(first, app.MediaSession);
        Assert.Contains("third failed", app.LastAction);
    }

    [Fact]
    public void VideoFitsAvailableAreaAndHidesUnderTheModalDialog()
    {
        using var dispatcher = new BlockingCollection<Action>();
        using var host = new DeferredHost();
        using var ui = CreateUi();
        using var app = new BroadplayApp(ui, () => { }, mediaHost: host, postToUi: dispatcher.TryAdd);
        Assert.True(app.Open("clip.mp4", [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109]));
        var video = new TestSession
        {
            Info = new(BroadplayMediaKind.Video, "MP4", TimeSpan.FromSeconds(2), VideoWidth: 1920, VideoHeight: 1080),
        };
        host.Opens["clip"].Result.SetResult(new(video, null));
        DispatchNext(dispatcher);
        app.RenderFrame();
        Assert.True(video.VideoVisible);
        Assert.InRange(video.Viewport.Width / video.Viewport.Height, 1.77, 1.79);

        Assert.True(app.Menu.CommandDispatcher!.TryExecute("help.about"));
        var dialog = Assert.IsType<StandardAboutDialog>(app.Session.FocusedElement);
        app.RenderFrame();
        Assert.False(video.VideoVisible);
        Assert.True(dialog.Cancel());
        app.RenderFrame();
        Assert.True(video.VideoVisible);

        video.Play();
        DispatchNext(dispatcher);
        Assert.True(app.IsPlayingVideo);
        app.PauseForSystemChange("Paused: output changed.");
        DispatchNext(dispatcher);
        Assert.Same(video, app.MediaSession);
        Assert.Equal(BroadplayPlaybackState.Paused, video.State);
        Assert.False(app.IsPlayingVideo);
        Assert.Contains("output changed", app.LastAction);
    }

    [Fact]
    public void LatestReadyCandidateWinsAndFailedOpenLeavesCurrentSession()
    {
        using var dispatcher = new BlockingCollection<Action>();
        using var host = new DeferredHost();
        using var ui = CreateUi();
        using var app = new BroadplayApp(ui, () => { }, mediaHost: host, postToUi: Post);
        var original = new TestSession();
        Start("A");
        Finish("A", original);
        Assert.Same(original, app.MediaSession);

        Start("B");
        Start("C");
        Assert.True(host.Opens["B"].Cancellation.IsCancellationRequested);
        var newest = new TestSession();
        Finish("C", newest);
        Assert.Equal(1, original.DisposeCount);
        var stale = new TestSession();
        host.Opens["B"].Result.SetResult(new(stale, null));
        Assert.True(stale.Disposed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, stale.DisposeCount);
        Assert.Same(newest, app.MediaSession);

        Start("D");
        host.Opens["D"].Result.SetResult(new(null, "Missing decoder"));
        DispatchNext(dispatcher);
        Assert.Same(newest, app.MediaSession);
        Assert.Contains("Missing decoder", app.LastAction);

        Start("E");
        Assert.True(app.Menu.CommandDispatcher!.TryExecute("file.close"));
        var afterClose = new TestSession();
        host.Opens["E"].Result.SetResult(new(afterClose, null));
        Assert.True(afterClose.Disposed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(app.MediaSession);

        bool Post(Action action) => dispatcher.TryAdd(action);
        void Start(string name) => Assert.True(app.Open(name + ".wav", "RIFF....WAVE"u8));
        void Finish(string name, TestSession session)
        {
            host.Opens[name].Result.SetResult(new(session, null));
            DispatchNext(dispatcher);
        }
    }

    [Fact]
    public void PausedFailureIsDeliveredWithoutTicksAndOldNotificationCannotReplaceCurrentState()
    {
        using var dispatcher = new BlockingCollection<Action>();
        using var host = new DeferredHost();
        using var ui = CreateUi();
        using var app = new BroadplayApp(ui, () => { }, mediaHost: host, postToUi: dispatcher.TryAdd);
        Assert.True(app.Open("A.wav", "RIFF....WAVE"u8));
        var first = new TestSession();
        host.Opens["A"].Result.SetResult(new(first, null));
        DispatchNext(dispatcher);
        Assert.False(app.WantsTick);
        first.ChangeState(BroadplayPlaybackState.Failed);
        DispatchNext(dispatcher);
        Assert.False(app.Menu.CommandDispatcher!.TryExecute("playback.toggle"));

        Assert.True(app.Open("B.wav", "RIFF....WAVE"u8));
        var next = new TestSession();
        host.Opens["B"].Result.SetResult(new(next, null));
        Assert.True(dispatcher.TryTake(out Action? adoptNext, TimeSpan.FromSeconds(5)));
        first.ChangeState(BroadplayPlaybackState.Ended);
        adoptNext!();
        DispatchNext(dispatcher);
        Assert.Same(next, app.MediaSession);
        Assert.True(app.ActiveMediaView!.CanControlPlayback);
    }

    private static BroadplayUiHost CreateUi() =>
        new(() => new BSize(800, 600), () => 1, () => { }, _ => { });

    private static void DispatchNext(BlockingCollection<Action> dispatcher)
    {
        Assert.True(dispatcher.TryTake(out Action? action, TimeSpan.FromSeconds(5)));
        action!();
    }

    private sealed class DeferredHost : IBroadplayAsyncMediaHost
    {
        public Dictionary<string, (CancellationToken Cancellation, TaskCompletionSource<BroadplayOpenResult> Result)> Opens { get; } = [];
        public bool SupportsAudio => true;
        public bool SupportsVideo => false;
        public Task<BroadplayOpenResult> OpenAsync(BroadplayMediaSource source, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<BroadplayOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Opens.Add(Path.GetFileNameWithoutExtension(source.Name), (cancellationToken, result));
            return result.Task;
        }
        public bool TryOpen(string path, BroadplayMediaKind kind, out IBroadplayMediaSession? session, out string? error) =>
            throw new NotSupportedException();
        public bool TryOpenBytes(string name, ReadOnlyMemory<byte> bytes, BroadplayMediaKind kind,
            out IBroadplayMediaSession? session, out string? error) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class TestSession : IBroadplayMediaSession
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public ManualResetEventSlim Disposed { get; } = new();
        public BroadplayMediaInfo Info { get; init; } = new(BroadplayMediaKind.Audio, "WAVE", TimeSpan.FromSeconds(2));
        public BroadplayPlaybackState State { get; private set; } = BroadplayPlaybackState.Ready;
        public bool ReportsStateChanges => true;
        public TimeSpan Position => TimeSpan.Zero;
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public string? ErrorMessage => State == BroadplayPlaybackState.Failed ? "Output unavailable" : null;
        public event EventHandler? StateChanged;
        public void ChangeState(BroadplayPlaybackState state)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        public void Play() => ChangeState(BroadplayPlaybackState.Playing);
        public void Pause() => ChangeState(BroadplayPlaybackState.Paused);
        public void Stop() => ChangeState(BroadplayPlaybackState.Stopped);
        public void Seek(TimeSpan position) { }
        public BRect Viewport { get; private set; }
        public bool VideoVisible { get; private set; }
        public void SetVideoViewport(BRect clientRect, bool visible)
        {
            Viewport = clientRect;
            VideoVisible = visible;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            Disposed.Set();
        }
    }
}
