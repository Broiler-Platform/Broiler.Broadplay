// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayCollectionTests
{
    [Fact]
    public void DuplicateQueueEntriesKeepIndependentIdentity()
    {
        var queue = new BroadplayQueue();
        QueueEntry first = QueueEntry.Create("Track 2.mp3");
        QueueEntry duplicate = QueueEntry.Create(first.Source);
        QueueEntry inserted = QueueEntry.Create("Track 10.mp3");
        queue.Add([first, duplicate], QueueIntake.Append);
        queue.Select(duplicate.Id);
        queue.Add([inserted], QueueIntake.PlayNext);
        Assert.Equal(new[] { first.Id, duplicate.Id, inserted.Id }, queue.Entries.Select(entry => entry.Id));
        Assert.True(queue.Move(duplicate.Id, -1));
        queue.Remove(first.Id);
        Assert.Equal(duplicate.Id, queue.Current);
        Assert.Equal(inserted, queue.Next(duplicate.Id, false, []));
        queue.Repeat = QueueRepeat.One;
        Assert.Equal(duplicate, queue.Next(duplicate.Id, true, []));
        queue.Repeat = QueueRepeat.All;
        queue.Shuffle = true;
        Assert.Null(queue.Next(duplicate.Id, false, [duplicate.Id, inserted.Id]));
    }

    [Fact]
    public void PlaylistRoundTripsUnicodeDuplicatesAndRemoteEntriesWithoutOpeningThem()
    {
        string directory = Directory.CreateTempSubdirectory("broadplay-playlist-").FullName;
        try
        {
            string local = Path.Combine(directory, "Lied α 2.wav");
            string path = Path.Combine(directory, "test.m3u8");
            const string remote = "https://example.invalid/song.mp3";
            LocalPlaylist.Export(path, [QueueEntry.Create(local), QueueEntry.Create(remote, true), QueueEntry.Create(local)]);
            Assert.Collection(LocalPlaylist.Import(path),
                item => { Assert.Equal(local, item.Source); Assert.False(item.Remote); },
                item => { Assert.Equal(remote, item.Source); Assert.True(item.Remote); },
                item => Assert.Equal(local, item.Source));
            File.WriteAllText(path, "#EXTM3U\n#EXT-X-TARGETDURATION:6\nclip.ts");
            Assert.Throws<InvalidDataException>(() => LocalPlaylist.Import(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void QueueRestoresWithoutOpeningFilesAndCorruptStateRecovers()
    {
        string directory = Directory.CreateTempSubdirectory("broadplay-state-").FullName;
        try
        {
            string path = Path.Combine(directory, "collection.json");
            string media = Path.Combine(directory, "missing file.mp3");
            using var ui = new BroadplayUiHost(() => new BSize(900, 600), () => 1, () => { }, _ => { });
            using (var app = new BroadplayApp(ui, () => { }, collectionPath: path))
            {
                app.Intake([media, media], QueueIntake.Append);
                Assert.Equal(2, app.Queue.Entries.Count);
            }
            using (var app = new BroadplayApp(ui, () => { }, collectionPath: path))
            {
                Assert.Equal(new[] { media, media }, app.Queue.Entries.Select(item => item.Source));
                Assert.Null(app.MediaSession);
                Assert.True(app.Menu.CommandDispatcher!.TryExecute("queue.show"));
                app.RenderFrame();
            }
            File.WriteAllText(path, "{incomplete");
            Assert.Empty(CollectionState.Read(path).Queue);
            Assert.False(CollectionState.Parse("{\"Version\":999,\"SaveProgress\":true}").SaveProgress);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void NaturalSortingOrdersNumericTrackSuffixes()
    {
        string[] paths = ["Track 10", "Track 2", "Track 1"];
        Array.Sort(paths, NaturalMediaOrder.Instance);
        Assert.Equal(new[] { "Track 1", "Track 2", "Track 10" }, paths);
    }

    [Fact]
    public void IndexRefreshReportsAddsAndRemovalsAndHonorsCancellation()
    {
        string root = Directory.CreateTempSubdirectory("broadplay-index-").FullName;
        try
        {
            string nested = Directory.CreateDirectory(Path.Combine(root, "album")).FullName;
            string old = Path.Combine(nested, "old.mp3");
            File.WriteAllText(old, "candidate");
            File.WriteAllText(Path.Combine(root, "readme.txt"), "ignored");
            Assert.Equal(new[] { old }, BroadplayApp.ScanCollection([root], CancellationToken.None));

            File.Delete(old);
            string added = Path.Combine(root, "new.wav");
            File.WriteAllText(added, "candidate");
            Assert.Equal(new[] { added }, BroadplayApp.ScanCollection([root], CancellationToken.None));
            Assert.Throws<OperationCanceledException>(() => BroadplayApp.ScanCollection([root], new CancellationToken(true)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LinuxLauncherPreservesMultiplePathsInArgumentOrder()
    {
        LinuxBroadplayOptions options = LinuxBroadplayOptions.Parse(["--window", "/media/track 2.wav", "/media/ä.mp3"]);
        Assert.Equal("/media/track 2.wav", options.InitialPath);
        Assert.Equal(new[] { "/media/ä.mp3" }, options.AdditionalPaths);
    }
}
