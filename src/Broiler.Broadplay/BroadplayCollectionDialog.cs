// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;

namespace Broiler.Broadplay;

internal sealed partial class BroadplayApp
{
    private CollectionPanel? _collectionDialog;
    private enum CollectionView { Queue, Library, Playlists, Recent, Folders }

    private void ShowCollection() => ShowCollection(CollectionView.Queue);

    private void ShowCollection(CollectionView view)
    {
        if (_collectionDialog is not null)
            return;
        var dialog = new StandardDialog { Title = "Queue and local collection", PreferredSize = new BSize(800, 560) };
        var panel = new CollectionPanel(this, dialog);
        _collectionDialog = panel;
        dialog.AddChild(panel);
        dialog.ResultCompleted += (_, _) =>
        {
            _collectionDialog = null;
            panel.CancelMetadata();
        };
        dialog.ShowModal(_rootWindow, CenterInViewport(dialog.PreferredSize, 38));
        panel.FocusList();
        if (view == CollectionView.Library)
            panel.OpenLibrary(GetDialogDirectory());
        if (_indexedFolders.Count > 0)
            RefreshIndex();
    }

    private sealed class CollectionPanel : UiElement
    {
        private readonly BroadplayApp _app;
        private readonly StandardDialog _dialog;
        private readonly List<StandardButton> _tabs = [];
        private readonly List<StandardButton> _actions = [];
        private readonly StandardListView _list = new() { AccessibleName = "Media entries", SelectionMode = UiListSelectionMode.Multiple };
        private readonly StandardEdit _query = new() { AccessibleName = "Search entries", PlaceholderText = "Search entries" };
        private readonly StandardEdit _value = new() { AccessibleName = "Path or playlist name", PlaceholderText = "Path or playlist name" };
        private readonly StandardLabel _message = new();
        private readonly Dictionary<string, LocalMediaMetadata> _metadata = [];
        private CancellationTokenSource? _metadataScan;
        private CollectionView _view;
        private string[] _browsed = [];

        internal CollectionPanel(BroadplayApp app, StandardDialog dialog)
        {
            _app = app;
            _dialog = dialog;
            foreach (CollectionView view in Enum.GetValues<CollectionView>())
            {
                var button = new StandardButton { Text = view.ToString() };
                button.Clicked += (_, _) => SetView(view);
                AddChild(button);
                _tabs.Add(button);
            }
            AddChild(_query);
            AddChild(_value);
            AddChild(_list);
            AddChild(_message);
            _query.TextChanged += (_, _) => { CancelMetadata(); RefreshItems(); };
            var close = new StandardButton { Text = "Close", IsCancel = true };
            close.Clicked += (_, _) => dialog.Cancel();
            AddChild(close);
            _tabs.Add(close);
            SetView(CollectionView.Queue);
        }

        internal void FocusList() => _list.Focus();
        internal void OpenLibrary(string path)
        {
            _value.Text = path;
            BrowseFolder();
            SetView(CollectionView.Library);
        }
        internal void CancelMetadata()
        {
            _metadataScan?.Cancel();
            _metadataScan?.Dispose();
            _metadataScan = null;
        }

        private IEnumerable<string> SelectedPaths() => _list.SelectedItemIds;
        private QueueEntry? SelectedEntry() => Guid.TryParse(_list.SelectedItemId, out Guid id)
            ? _app._queue.Entries.FirstOrDefault(item => item.Id == id) : null;
        private NamedPlaylist? SelectedPlaylist() =>
            _app._playlists.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Name, _list.SelectedItemId));

        private void Action(string text, Action action)
        {
            var button = new StandardButton { Text = text };
            button.Clicked += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) when (IsFileOperationException(ex)) { _app.ReportCollection(ex.Message); }
                if (ReferenceEquals(_app._collectionDialog, this))
                    RefreshItems();
            };
            _actions.Add(button);
            AddChild(button);
        }

        private void SetView(CollectionView view)
        {
            CancelMetadata();
            _view = view;
            foreach (StandardButton old in _actions)
            {
                RemoveChild(old);
                old.Dispose();
            }
            _actions.Clear();
            _list.SetItems([]);
            switch (view)
            {
                case CollectionView.Queue:
                    Action("Play now", () => PlaySelected(restart: false));
                    Action("Previous", _app.PreviousQueue);
                    Action("Next", _app.NextQueue);
                    Action("Move up", () => Move(-1));
                    Action("Move down", () => Move(1));
                    Action("Remove", Remove);
                    Action("Clear queue", () => ChangeQueue(() =>
                    {
                        _app.CancelPendingOpen();
                        _app._requestedQueueItem = null;
                        _app._queue.Clear();
                        _app._queueErrors.Clear();
                    }));
                    Action("Repeat", () => ChangeQueue(() => _app._queue.Repeat = (QueueRepeat)(((int)_app._queue.Repeat + 1) % 3)));
                    Action("Shuffle", () => ChangeQueue(() => _app._queue.Shuffle = !_app._queue.Shuffle));
                    Action("Add path / playlist", () => _app.Intake([_value.Text], QueueIntake.Append));
                    Action("Export M3U8", () => LocalPlaylist.Export(_value.Text, _app._queue.Entries));
                    Action("Relink", Relink);
                    Action("Start over", () => PlaySelected(restart: true));
                    Action("Mark played", () => { if (SelectedEntry() is { } entry) ForgetProgress(entry.Source); });
                    break;
                case CollectionView.Library:
                case CollectionView.Recent:
                    Action("Play now", () =>
                    {
                        string[] paths = SelectedPaths().ToArray();
                        if (paths.Length == 0)
                            return;
                        _dialog.Cancel();
                        _app.Intake(paths, QueueIntake.PlayNow);
                    });
                    Action("Play next", () => _app.Intake(SelectedPaths(), QueueIntake.PlayNext));
                    Action("Add to queue", () => _app.Intake(SelectedPaths(), QueueIntake.Append));
                    if (view == CollectionView.Library)
                        Action("Browse folder", BrowseFolder);
                    else
                    {
                        Action("Toggle resume", ToggleResume);
                        Action("Clear history", ClearHistory);
                    }
                    break;
                case CollectionView.Playlists:
                    Action("Save queue as", SavePlaylist);
                    Action("Update from queue", UpdatePlaylist);
                    Action("Rename", RenamePlaylist);
                    Action("Delete list", () => ChangeQueue(() => { if (SelectedPlaylist() is { } list) _app._playlists.Remove(list); }));
                    Action("Load into queue", () => LoadPlaylist(replace: true));
                    Action("Append to queue", () => LoadPlaylist(replace: false));
                    break;
                case CollectionView.Folders:
                    Action("Index folder", AddFolder);
                    Action("Refresh index", _app.RefreshIndex);
                    Action("Cancel scan", CancelIndex);
                    Action("Remove folder", RemoveFolder);
                    break;
            }
            RefreshItems();
            Invalidate(UiInvalidationKind.Measure | UiInvalidationKind.Arrange | UiInvalidationKind.Render);
        }

        internal void RefreshItems()
        {
            if (IsDisposed)
                return;
            IEnumerable<UiListItem> rows = _view switch
            {
                CollectionView.Queue => _app._queue.Entries.Select(item => new UiListItem(item.Id.ToString(),
                    (item.Id == _app._queue.Current && _app._mediaSession is not null ? "Current · " : "") + (item.Remote ? item.Source : Path.GetFileName(item.Source)) +
                    (_app._queueErrors.TryGetValue(item.Id, out string? error) ? " · " + error :
                        item.Remote ? " · Remote entry" : !File.Exists(item.Source) ? " · Missing / reselect" : ""))),
                CollectionView.Library => _app._index.Concat(_browsed).Concat(_app._gallery.Entries.Where(item => !item.IsDirectory).Select(item => item.Path))
                    .Distinct().OrderBy(path => _metadata.GetValueOrDefault(path)?.Album ?? Path.GetFileName(path), NaturalMediaOrder.Instance)
                    .ThenBy(path => _metadata.GetValueOrDefault(path)?.Disc ?? 0)
                    .ThenBy(path => _metadata.GetValueOrDefault(path)?.Track ?? 0)
                    .ThenBy(path => Path.GetFileName(path), NaturalMediaOrder.Instance)
                    .Select(path => new UiListItem(path, _metadata.GetValueOrDefault(path)?.Label ?? Path.GetFileName(path))),
                CollectionView.Playlists => _app._playlists.Select(list => new UiListItem(list.Name, $"{list.Name} ({list.Sources.Length})")),
                CollectionView.Recent => _app._recents.Select(path => new UiListItem(path, Path.GetFileName(path))),
                _ => _app._indexedFolders.Select(path => new UiListItem(path, path)),
            };
            UiListItem[] candidates = rows.Take(10000).ToArray();
            UiListItem[] filtered = candidates.Where(row => row.Text.Contains(_query.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            _list.SetItems(filtered);
            _message.Text = _view switch
            {
                CollectionView.Queue => $"{_app._queue.Entries.Count} items · Repeat {_app._queue.Repeat} · Shuffle {(_app._queue.Shuffle ? "on" : "off")} · {_app._lastAction}",
                CollectionView.Recent => $"Resume {(_app._saveProgress ? "on" : "off")} · {_app._lastAction}",
                _ => _app._lastAction,
            };
            if (_view == CollectionView.Library && _metadataScan is null)
            {
                BeginMetadata(filtered.Concat(candidates).Select(row => row.Id).Distinct()
                    .Where(path => !_metadata.ContainsKey(path)).Take(Math.Min(100, 10000 - _metadata.Count)).ToArray());
            }
        }

        private void BeginMetadata(string[] paths)
        {
            if (paths.Length == 0)
                return;
            _metadataScan = new();
            CancellationToken token = _metadataScan.Token;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var metadata = new Dictionary<string, LocalMediaMetadata>();
                foreach (string path in paths)
                {
                    if (token.IsCancellationRequested)
                        return;
                    metadata[path] = LocalMediaMetadata.Read(path);
                }
                _app.PostCollectionWork(() =>
                {
                    if (token.IsCancellationRequested || _view != CollectionView.Library || IsDisposed)
                        return;
                    foreach ((string path, LocalMediaMetadata label) in metadata)
                        _metadata[path] = label;
                    CancelMetadata();
                    RefreshItems();
                });
            }, token);
        }

        private void ChangeQueue(Action change)
        {
            change();
            _app.SaveCollection();
        }

        private void PlaySelected(bool restart)
        {
            if (SelectedEntry() is not { Remote: false } entry)
                return;
            _dialog.Cancel();
            _app.StartQueue(entry, restart: restart);
        }

        private void Move(int offset)
        {
            if (SelectedEntry() is { } item)
                ChangeQueue(() => _app._queue.Move(item.Id, offset));
        }

        private void Remove()
        {
            Guid[] selected = _list.SelectedItemIds.Select(Guid.Parse).ToArray();
            ChangeQueue(() =>
            {
                foreach (Guid id in selected)
                {
                    _app._queue.Remove(id);
                    _app._queueErrors.Remove(id);
                    if (_app._requestedQueueItem?.Id == id)
                    {
                        _app.CancelPendingOpen();
                        _app._requestedQueueItem = null;
                    }
                }
            });
        }

        private void Relink()
        {
            if (SelectedEntry() is { } browserItem && _app._requestOpenFile is not null)
            {
                _app._reselectId = browserItem.Id;
                _app._requestOpenFile();
                return;
            }
            if (SelectedEntry() is not { } item || LocalPlaylist.IsRemote(_value.Text) || !File.Exists(_value.Text))
            {
                _app.ReportCollection("Select an entry and enter an existing local file path.");
                return;
            }
            string path = Path.GetFullPath(_value.Text);
            ChangeQueue(() =>
            {
                _app._queue.Replace(item.Id, path);
                _app._queueErrors.Remove(item.Id);
                if (_app._positions.TryGetValue(item.Source, out ResumePoint? saved) &&
                    IdentifyResumeSource(path) is { } details && saved.Size == details.Size)
                {
                    _app._positions.Remove(item.Source);
                    _app._positions[path] = new(path, saved.Seconds, details.Size, details.Modified);
                }
            });
        }

        private void ForgetProgress(string source)
        {
            ChangeQueue(() => _app._positions.Remove(source));
            if (_app._currentPath == source)
                _app._suppressProgressFor = _app._mediaSession;
        }

        private void ToggleResume() => ChangeQueue(() => _app._saveProgress = !_app._saveProgress);

        private void ClearHistory() => ChangeQueue(() =>
        {
            _app._positions.Clear();
            _app._recents.Clear();
            _app._suppressProgressFor = _app._mediaSession;
        });

        private void BrowseFolder()
        {
            string path = Path.GetFullPath(_value.Text);
            BroadplayGalleryEntry[] entries = BroadplayGallery.ReadFolder(path);
            _browsed = entries.Where(item => !item.IsDirectory).Select(item => item.Path).ToArray();
            CancelMetadata();
            _metadata.Clear();
        }

        private void AddFolder()
        {
            string path = Path.GetFullPath(_value.Text);
            if (LocalPlaylist.IsRemote(path) || !Directory.Exists(path) || _app._indexedFolders.Contains(path))
                return;
            ChangeQueue(() => _app._indexedFolders.Add(path));
            _app.RefreshIndex();
        }

        private void RemoveFolder()
        {
            if (_list.SelectedItemId is not { } path)
                return;
            ChangeQueue(() => _app._indexedFolders.Remove(path));
            _app._index.Clear();
            _app.RefreshIndex();
        }

        private void CancelIndex()
        {
            _app._indexScan?.Cancel();
            _app._indexedScan = null;
            _app.ReportCollection("Index scan cancelled.");
        }

        private bool PlaylistNameAvailable(string name) => name.Length > 0 &&
            !_app._playlists.Any(list => list.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        private string[] QueueSources() => _app._queue.Entries.Select(item => item.Source).ToArray();

        private void SavePlaylist()
        {
            string name = _value.Text.Trim();
            if (!PlaylistNameAvailable(name))
            {
                _app.ReportCollection("Enter a unique playlist name.");
                return;
            }
            ChangeQueue(() => _app._playlists.Add(new(name, QueueSources())));
        }

        private void UpdatePlaylist()
        {
            if (SelectedPlaylist() is { } list)
                ChangeQueue(() => _app._playlists[_app._playlists.IndexOf(list)] = list with { Sources = QueueSources() });
        }

        private void RenamePlaylist()
        {
            string name = _value.Text.Trim();
            if (SelectedPlaylist() is { } list && PlaylistNameAvailable(name))
                ChangeQueue(() => _app._playlists[_app._playlists.IndexOf(list)] = list with { Name = name });
        }

        private void LoadPlaylist(bool replace)
        {
            if (SelectedPlaylist() is not { } list)
                return;
            if (replace)
            {
                _app.CancelPendingOpen();
                _app._requestedQueueItem = null;
                _app._queue.Clear();
                _app._queueErrors.Clear();
            }
            _app.Intake(list.Sources, QueueIntake.Append);
        }

        protected override BSize MeasureCore(BSize availableSize) => new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 800,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 560);

        protected override void ArrangeCore(BRect bounds)
        {
            double left = bounds.Left + 8;
            double width = Math.Max(0, bounds.Width - 16);
            double y = bounds.Top + 8;
            y = Row(_tabs, left, y, width, 6);
            Place(_query, new BRect(left, y, width, 30));
            Place(_value, new BRect(left, y + 34, width, 30));
            y += 68;
            int columns = width >= 620 ? 5 : 4;
            double actionHeight = (int)Math.Ceiling((double)_actions.Count / columns) * 34;
            double listHeight = Math.Max(48, bounds.Bottom - y - actionHeight - 33);
            Place(_list, new BRect(left, y, width, listHeight));
            y = Row(_actions, left, y + listHeight + 4, width, columns);
            Place(_message, new BRect(left, y, width, 25));
        }

        private static double Row(IReadOnlyList<StandardButton> buttons, double x, double y, double width, int columns)
        {
            double cell = width / columns;
            for (int i = 0; i < buttons.Count; i++)
                Place(buttons[i], new BRect(x + (i % columns) * cell, y + (i / columns) * 34, Math.Max(0, cell - 3), 30));
            return y + (int)Math.Ceiling((double)buttons.Count / columns) * 34;
        }

        private static void Place(UiElement element, BRect box)
        {
            element.Measure(box.Size);
            element.Arrange(box);
        }
    }
}
