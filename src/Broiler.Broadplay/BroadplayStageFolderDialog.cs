// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Button.Stage;
using Broiler.UI.Dialog.Stage;
using Broiler.UI.Edit.Stage;
using Broiler.UI.Label.Stage;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Stage;
using Broiler.UI.Stage;

namespace Broiler.Broadplay;

/// <summary>
/// In-stage ten-foot folder browser hosted inside a <see cref="StageDialog"/> modal scrim sheet,
/// navigable by D-pad / arrows / <c>Enter</c> / <c>Escape</c> without spawning a desktop OS window.
/// </summary>
internal sealed class BroadplayStageFolderDialog : UiElement
{
    private readonly StageDialog _dialog;
    private readonly Dictionary<string, string> _placePaths = [];
    private readonly StageLabel _placesHeader = new() { Text = "Places", Role = StageTextRole.Caption, IsMuted = true };
    private readonly StageLabel _foldersHeader = new() { Text = "Subfolders", Role = StageTextRole.Caption, IsMuted = true };
    private readonly StageLabel _statusLabel = new() { Role = StageTextRole.Caption, IsMuted = true };
    private readonly StageButton _upButton = new() { Text = "Up", PreferredSize = new BSize(104, 56) };
    private readonly StageButton _openButton = new() { Text = "Open folder", IsDefault = true, PreferredSize = new BSize(190, 56) };
    private readonly StageButton _cancelButton = new() { Text = "Cancel", IsCancel = true, PreferredSize = new BSize(140, 56) };
    private bool _navigating;

    internal BroadplayStageFolderDialog(StageDialog dialog, string initialDirectory)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        PathEdit = new StageEdit { PlaceholderText = "Folder path" };
        Places = new StageListView { ItemHeight = 56 };
        Folders = new StageListView { ItemHeight = 56 };

        foreach (UiElement child in new UiElement[]
        {
            _upButton,
            PathEdit,
            _placesHeader,
            Places,
            _foldersHeader,
            Folders,
            _statusLabel,
            _openButton,
            _cancelButton,
        })
        {
            AddChild(child);
        }

        PopulatePlaces();

        _upButton.Clicked += (_, _) => Navigate(Directory.GetParent(CurrentDirectory)?.FullName ?? CurrentDirectory);
        PathEdit.Submitted += (_, _) => Navigate(PathEdit.Text);
        Places.ItemActivated += (_, e) =>
        {
            if (!_navigating && _placePaths.TryGetValue(e.Item.Id, out string? target))
                Navigate(target);
        };
        Places.SelectionChanged += (_, e) =>
        {
            if (!_navigating && e.NewItemId is not null && _placePaths.TryGetValue(e.NewItemId, out string? target))
                Navigate(target);
        };
        Folders.ItemActivated += (_, e) =>
        {
            if (!_navigating)
                Navigate(e.Item.Id);
        };
        _openButton.Clicked += (_, _) => AcceptFolder();
        _cancelButton.Clicked += (_, _) => _dialog.Cancel();

        Navigate(initialDirectory);
    }

    internal StageEdit PathEdit { get; }
    internal StageListView Places { get; }
    internal StageListView Folders { get; }
    internal StageButton OpenFolderButton => _openButton;
    internal string CurrentDirectory { get; private set; } = string.Empty;

    internal void FocusInitial()
    {
        if (Folders.Items.Count > 0)
            Folders.Focus();
        else
            _openButton.Focus();
    }

    internal bool AcceptFolder()
    {
        if (!Navigate(PathEdit.Text))
            return false;
        return _dialog.Accept(CurrentDirectory);
    }

    internal bool Navigate(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            UiListItem[] items = Directory.GetDirectories(fullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(static dir => new UiListItem(dir, Path.GetFileName(dir)))
                .ToArray();

            _navigating = true;
            try
            {
                Folders.SetItems(items);
                if (items.Length > 0)
                    Folders.SelectIndex(0);
                CurrentDirectory = fullPath;
                PathEdit.Text = fullPath;
                _upButton.IsEnabled = Directory.GetParent(fullPath) is not null;
                _statusLabel.Text = items.Length == 0
                    ? "No subfolders. Select Open folder to browse media here."
                    : "Press Enter on a subfolder to enter it, or select Open folder.";
            }
            finally
            {
                _navigating = false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _statusLabel.Text = "Could not open folder: " + ex.Message;
            return false;
        }
    }

    protected override BSize MeasureCore(BSize availableSize)
    {
        foreach (UiElement child in Children)
            child.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(BRect rect)
    {
        double width = Math.Max(0, rect.Width);
        _upButton.Arrange(new BRect(rect.Left, rect.Top, 104, 56));
        PathEdit.Arrange(new BRect(rect.Left + 116, rect.Top, Math.Max(0, width - 116), 56));

        double listTop = rect.Top + 72;
        double headerH = 26;
        double footerTop = Math.Max(listTop + 80, rect.Bottom - 56);
        double statusTop = Math.Max(listTop + 56, footerTop - 34);
        double listH = Math.Max(0, statusTop - listTop - headerH - 8);

        double placesW = Math.Min(250, Math.Max(160, width * 0.30));
        double gap = 16;
        double foldersLeft = rect.Left + placesW + gap;
        double foldersW = Math.Max(0, width - placesW - gap);

        _placesHeader.Arrange(new BRect(rect.Left, listTop, placesW, headerH));
        Places.Arrange(new BRect(rect.Left, listTop + headerH, placesW, listH));

        _foldersHeader.Arrange(new BRect(foldersLeft, listTop, foldersW, headerH));
        Folders.Arrange(new BRect(foldersLeft, listTop + headerH, foldersW, listH));

        _statusLabel.Arrange(new BRect(rect.Left, statusTop, width, 28));
        _openButton.Arrange(new BRect(Math.Max(rect.Left, rect.Right - 342), footerTop, 190, 56));
        _cancelButton.Arrange(new BRect(Math.Max(rect.Left, rect.Right - 140), footerTop, 140, 56));
    }

    private void PopulatePlaces()
    {
        List<UiListItem> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        _placePaths.Clear();

        AddPlace(items, seen, "Home", SafeSpecialFolder(Environment.SpecialFolder.UserProfile));
        AddPlace(items, seen, "Videos", SafeSpecialFolder(Environment.SpecialFolder.MyVideos));
        AddPlace(items, seen, "Music", SafeSpecialFolder(Environment.SpecialFolder.MyMusic));
        AddPlace(items, seen, "Desktop", SafeSpecialFolder(Environment.SpecialFolder.DesktopDirectory));
        AddPlace(items, seen, "Working folder", Environment.CurrentDirectory);

        try
        {
            foreach (string drive in Directory.GetLogicalDrives())
                AddPlace(items, seen, "Drive " + drive.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), drive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        Places.SetItems(items);
    }

    private void AddPlace(List<UiListItem> items, HashSet<string> seen, string label, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return;

        try
        {
            string normalized = Path.GetFullPath(rawPath);
            if (!Directory.Exists(normalized) || !seen.Add(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                return;

            string id = "stage-place:" + items.Count.ToString(CultureInfo.InvariantCulture);
            _placePaths[id] = normalized;
            items.Add(new UiListItem(id, label));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private static string? SafeSpecialFolder(Environment.SpecialFolder folder)
    {
        try
        {
            string value = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
