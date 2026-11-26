// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Broadplay;

internal enum QueueIntake { PlayNow, PlayNext, Append }
internal enum QueueRepeat { Off, One, All }

internal sealed record QueueEntry(Guid Id, string Source, bool Remote = false)
{
    public static QueueEntry Create(string source, bool remote = false) => new(Guid.NewGuid(), source, remote);
}

internal sealed class BroadplayQueue
{
    private readonly List<QueueEntry> _entries = [];
    private readonly List<Guid> _history = [];
    private readonly HashSet<Guid> _visited = [];
    public IReadOnlyList<QueueEntry> Entries => _entries;
    public Guid? Current { get; private set; }
    public QueueRepeat Repeat { get; set; }
    public bool Shuffle { get; set; }

    public void Add(IEnumerable<QueueEntry> entries, QueueIntake mode)
    {
        int afterCurrent = Index(Current) + 1;
        _entries.InsertRange(mode == QueueIntake.PlayNext && afterCurrent > 0 ? afterCurrent : _entries.Count, entries);
    }

    public void Select(Guid id)
    {
        if (Index(id) < 0 || Current == id)
            return;
        if (Shuffle && _history.Count > 0 && _history[^1] == id)
            _history.RemoveAt(_history.Count - 1);
        else if (Current is { } last)
            _history.Add(last);
        Current = id;
        _visited.Add(id);
    }

    public bool Move(Guid id, int offset)
    {
        int old = Index(id);
        int next = old + offset;
        if (old < 0 || next < 0 || next >= _entries.Count)
            return false;
        QueueEntry item = _entries[old];
        _entries.RemoveAt(old);
        _entries.Insert(next, item);
        return true;
    }

    public void Remove(Guid id)
    {
        int index = Index(id);
        if (index < 0)
            return;
        _entries.RemoveAt(index);
        _history.RemoveAll(item => item == id);
        _visited.Remove(id);
        if (Current == id)
            Current = null;
    }

    public void Replace(Guid id, string path)
    {
        int index = Index(id);
        if (index >= 0)
            _entries[index] = _entries[index] with { Source = path, Remote = false };
    }

    public void Clear()
    {
        _entries.Clear();
        _history.Clear();
        _visited.Clear();
        Current = null;
    }

    public void Detach() => Current = null;

    public QueueEntry? Previous()
    {
        if (Shuffle && _history.Count > 0)
        {
            Guid id = _history[^1];
            return _entries.Find(item => item.Id == id && !item.Remote);
        }
        int index = Index(Current);
        for (int i = index - 1; i >= 0; i--)
            if (!_entries[i].Remote)
                return _entries[i];
        return null;
    }

    public QueueEntry? Next(Guid? from, bool ended, HashSet<Guid> attempted)
    {
        bool Available(QueueEntry item) => !item.Remote && !attempted.Contains(item.Id);
        int index = Index(from);
        if (ended && Repeat == QueueRepeat.One && index >= 0 && Available(_entries[index]))
            return _entries[index];
        if (Shuffle)
        {
            QueueEntry[] options = _entries.Where(item => Available(item) && !_visited.Contains(item.Id) && item.Id != from).ToArray();
            if (options.Length == 0 && Repeat == QueueRepeat.All)
            {
                _visited.Clear();
                options = _entries.Where(Available).ToArray();
            }
            return options.Length > 0 ? options[Random.Shared.Next(options.Length)] : null;
        }
        for (int i = index + 1; i < _entries.Count; i++)
            if (Available(_entries[i]))
                return _entries[i];
        if (Repeat == QueueRepeat.All)
            for (int i = 0; i <= index; i++)
                if (Available(_entries[i]))
                    return _entries[i];
        return null;
    }

    private int Index(Guid? id) => id is null ? -1 : _entries.FindIndex(item => item.Id == id);
}
