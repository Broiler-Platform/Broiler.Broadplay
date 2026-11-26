// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.UI.Dialog.Standard;

namespace Broiler.Broadplay;

internal sealed partial class BroadplayApp
{
    private readonly BroadplayQueue _queue = new();
    private readonly List<NamedPlaylist> _playlists = [];
    private readonly List<string> _indexedFolders = [];
    private readonly List<string> _recents = [];
    private readonly Dictionary<string, ResumePoint> _positions = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _attempted = [];
    private readonly Dictionary<Guid, string> _queueErrors = [];
    private readonly List<string> _index = [];
    private string? _collectionPath;
    private Action<string>? _persistCollection;
    private QueueEntry? _requestedQueueItem;
    private IBroadplayMediaSession? _handledTerminal;
    private IBroadplayMediaSession? _suppressProgressFor;
    private readonly ConcurrentQueue<Action> _collectionCallbacks = new();
    private bool _advanceOnFailure;
    private bool _saveProgress;
    private bool _startFromBeginning;
    private Guid? _reselectId;
    private CancellationTokenSource? _indexScan;
    private Task<string[]>? _indexedScan;
    private DateTime _nextProgressSave;

    internal BroadplayQueue Queue => _queue;

    private void LoadCollection(string? path, string? json, Action<string>? persist)
    {
        _collectionPath = path;
        _persistCollection = persist;
        CollectionState state = path is not null ? CollectionState.Read(path) : CollectionState.Parse(json);
        _queue.Add(state.Queue, QueueIntake.Append);
        _queue.Repeat = state.Repeat;
        _queue.Shuffle = state.Shuffle;
        _playlists.AddRange(state.Playlists);
        _indexedFolders.AddRange(state.IndexedFolders.Where(path => !string.IsNullOrWhiteSpace(path)));
        _recents.AddRange(state.Recents.Where(path => !string.IsNullOrWhiteSpace(path)).Take(40));
        foreach (ResumePoint point in state.Positions)
            _positions[point.Source] = point;
        _volume = state.Volume;
        _muted = state.Muted;
        _saveProgress = state.SaveProgress;
    }

    private void SaveCollection()
    {
        if (_collectionPath is null && _persistCollection is null)
            return;
        var state = new CollectionState
        {
            Queue = _queue.Entries.ToArray(), Playlists = _playlists.ToArray(), IndexedFolders = _indexedFolders.ToArray(),
            Recents = _recents.ToArray(), Positions = _positions.Values.Take(500).ToArray(),
            Volume = _volume, Muted = _muted, SaveProgress = _saveProgress, Repeat = _queue.Repeat, Shuffle = _queue.Shuffle,
        };
        try
        {
            if (_collectionPath is not null)
                state.Write(_collectionPath);
            else
                _persistCollection!(state.Serialize());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lastAction = "Could not save collection: " + ex.Message;
        }
    }

    internal void Intake(IEnumerable<string> paths, QueueIntake mode)
    {
        var items = new List<QueueEntry>();
        var problems = new List<string>();
        foreach (string source in paths)
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            try
            {
                if (LocalPlaylist.IsRemote(source))
                {
                    items.Add(QueueEntry.Create(source, remote: true));
                    continue;
                }
                string path = Path.GetFullPath(source);
                if (Directory.Exists(path))
                    items.AddRange(BroadplayGallery.ReadFolder(path).Where(item => !item.IsDirectory).Select(item => QueueEntry.Create(item.Path)));
                else if (LocalPlaylist.IsPlaylist(path))
                    items.AddRange(LocalPlaylist.Import(path));
                else
                    items.Add(QueueEntry.Create(path));
            }
            catch (Exception ex) when (IsFileOperationException(ex))
            {
                problems.Add(Path.GetFileName(source) + ": " + ex.Message);
            }
        }

        foreach (QueueEntry item in items)
        {
            if (item.Remote)
                _queueErrors[item.Id] = "Remote entry. Network playback is unavailable.";
            else if (!File.Exists(item.Source))
                _queueErrors[item.Id] = "Missing. Relink this item.";
            else if (!BroadplayMediaFormats.HasMediaExtension(item.Source))
            {
                try
                {
                    if (!TryIdentify(item.Source, ReadSignature(item.Source, out _), out _))
                        _queueErrors[item.Id] = "Unsupported file.";
                }
                catch (Exception ex) when (IsFileOperationException(ex)) { _queueErrors[item.Id] = ex.Message; }
            }
        }

        if (_reselectId is { } reselect && items.FirstOrDefault(item => !item.Remote && !_queueErrors.ContainsKey(item.Id)) is { } replacement)
        {
            _queue.Replace(reselect, replacement.Source);
            _queueErrors.Remove(reselect);
            items.Remove(replacement);
            mode = QueueIntake.Append;
        }
        _reselectId = null;
        _queue.Add(items, mode);
        SaveCollection();
        if (mode == QueueIntake.PlayNow && items.FirstOrDefault(item => !item.Remote) is { } first)
            StartQueue(first, skipFailures: true);
        if (problems.Count > 0)
            ReportCollection(string.Join(" · ", problems));
        else if (mode != QueueIntake.PlayNow || items.All(item => item.Remote))
            ReportCollection($"Added {items.Count} item(s) to the queue.");
    }

    internal void CancelReselect() => _reselectId = null;

    private void StartQueue(QueueEntry entry, bool skipFailures = false, bool restart = false)
    {
        _attempted.Clear();
        if (restart && _mediaSession is not null && _currentPath == entry.Source)
            _suppressProgressFor = _mediaSession;
        _advanceOnFailure = skipFailures;
        _startFromBeginning = restart;
        StartQueueCandidate(entry);
    }

    private void StartQueueCandidate(QueueEntry entry)
    {
        QueueEntry? next = entry;
        while (next is not null)
        {
            _attempted.Add(next.Id);
            _requestedQueueItem = next;
            if (OpenFileCore(next.Source))
                return;
            _queueErrors[next.Id] = _lastAction;
            _requestedQueueItem = null;
            next = _advanceOnFailure ? _queue.Next(next.Id, ended: false, _attempted) : null;
        }
        _advanceOnFailure = false;
    }

    private void QueueOpenFailed()
    {
        if (_requestedQueueItem is not { } item)
            return;
        _queueErrors[item.Id] = _lastAction;
        _collectionDialog?.RefreshItems();
        _requestedQueueItem = null;
        if (_advanceOnFailure && _queue.Next(item.Id, ended: false, _attempted) is { } next)
            StartQueueCandidate(next);
        else
            _advanceOnFailure = false;
    }

    private void QueueOpened(IBroadplayMediaSession session)
    {
        if (_requestedQueueItem is not { } item)
            return;
        _requestedQueueItem = null;
        _queue.Select(item.Id);
        _queueErrors.Remove(item.Id);
        _recents.Remove(item.Source);
        _recents.Insert(0, item.Source);
        if (_recents.Count > 40)
            _recents.RemoveAt(_recents.Count - 1);
        if (_startFromBeginning)
            _positions.Remove(item.Source);
        else if (_saveProgress && session.CanSeek && _positions.TryGetValue(item.Source, out ResumePoint? position) &&
            IdentifyResumeSource(item.Source) is { } file && file.Size == position.Size && file.Modified == position.ModifiedUtcTicks &&
            position.Seconds < session.Info.Duration.TotalSeconds - 3)
            session.Seek(TimeSpan.FromSeconds(position.Seconds));

        _startFromBeginning = false;
        _advanceOnFailure = false;
        if (session.Info.HasLivePlayback)
            session.Play();
        SaveCollection();
        _collectionDialog?.RefreshItems();
    }

    private void QueueStateChanged(IBroadplayMediaSession session)
    {
        if (session.State == BroadplayPlaybackState.Playing && ReferenceEquals(_handledTerminal, session))
            _handledTerminal = null;
        if (_queue.Current is not { } current || _requestedQueueItem is not null ||
            ReferenceEquals(_handledTerminal, session) ||
            session.State is not (BroadplayPlaybackState.Ended or BroadplayPlaybackState.Failed))
            return;
        _handledTerminal = session;
        bool ended = session.State == BroadplayPlaybackState.Ended;
        if (ended && _currentPath is not null)
            _positions.Remove(_currentPath);
        if (!ended)
            _queueErrors[current] = session.ErrorMessage ?? "Playback failed.";
        if (ended)
            _attempted.Clear();
        else
            _attempted.Add(current);
        if (_queue.Next(current, ended, _attempted) is { } next)
        {
            if (ended && next.Id != current)
                _attempted.Add(current);
            _advanceOnFailure = true;
            StartQueueCandidate(next);
        }
        SaveCollection();
        _collectionDialog?.RefreshItems();
    }

    private void NextQueue()
    {
        if (_queue.Next(_queue.Current, ended: false, []) is { } next)
            StartQueue(next, skipFailures: true);
    }

    private void PreviousQueue()
    {
        if (_mediaSession is { CanSeek: true } session && session.Position > TimeSpan.FromSeconds(3))
            session.Seek(TimeSpan.Zero);
        else if (_queue.Previous() is { } previous)
            StartQueue(previous);
    }

    private void RecordProgress()
    {
        if (!_saveProgress || _currentPath is null || _mediaSession is not { CanSeek: true } session ||
            ReferenceEquals(_suppressProgressFor, session) ||
            session.State is BroadplayPlaybackState.Ended or BroadplayPlaybackState.Failed or BroadplayPlaybackState.Stopped ||
            session.Position.TotalSeconds < 10 || session.Info.Duration - session.Position < TimeSpan.FromSeconds(3))
            return;
        if (IdentifyResumeSource(_currentPath) is { } file)
            _positions[_currentPath] = new(_currentPath, session.Position.TotalSeconds, file.Size, file.Modified);
    }

    private static (long Size, long Modified)? IdentifyResumeSource(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (IsFileOperationException(ex)) { return null; }
    }

    private void RefreshIndex()
    {
        _indexScan?.Cancel();
        _indexScan?.Dispose();
        _indexScan = new();
        CancellationToken token = _indexScan.Token;
        string[] roots = _indexedFolders.ToArray();
        _indexedScan = Task.Run(() => ScanCollection(roots, token), token);
        if (_postToUi is not null)
            _ = _indexedScan.ContinueWith(_ => PostCollectionWork(CheckCollectionWork), TaskScheduler.Default);
        SyncTicking();
    }

    internal static string[] ScanCollection(string[] roots, CancellationToken token)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 12 };
        foreach (string root in roots)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (BroadplayMediaFormats.HasMediaExtension(file))
                        files.Add(file);
                    if (files.Count >= 10000)
                        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            catch (Exception ex) when (IsFileOperationException(ex)) { }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void CheckCollectionWork()
    {
        while (_collectionCallbacks.TryDequeue(out Action? callback))
            callback();
        if (_indexedScan is { IsCompleted: true } scan)
        {
            _indexedScan = null;
            if (scan.IsCompletedSuccessfully)
            {
                _index.Clear();
                _index.AddRange(scan.Result);
                ReportCollection($"Indexed {_index.Count} media file(s).");
            }
            _collectionDialog?.RefreshItems();
        }
        if (_saveProgress && _mediaSession?.State == BroadplayPlaybackState.Playing &&
            DateTime.UtcNow >= _nextProgressSave)
        {
            _nextProgressSave = DateTime.UtcNow.AddSeconds(15);
            RecordProgress();
            SaveCollection();
        }
    }

    private void PostCollectionWork(Action action)
    {
        if (_postToUi is not null)
            _postToUi(action);
        else
            _collectionCallbacks.Enqueue(action);
    }

    private void ReportCollection(string status)
    {
        _lastAction = status;
        _collectionDialog?.RefreshItems();
        RefreshUi();
    }
}
