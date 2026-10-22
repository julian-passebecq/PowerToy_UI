namespace JUtility.Core.Files;

/// <summary>
/// Watches the chosen folders (top level only) and keeps the bounded tray up to date.
/// Cost model: one FileSystemWatcher per folder (the OS notifies; nothing polls). Events are filtered by name
/// before any disk access, so an in-progress .crdownload costs a string check. A single one-shot timer runs only
/// while a file is still being written; with nothing pending there is no timer and no background work.
/// <see cref="Changed"/> is raised on a thread-pool thread.
/// </summary>
public sealed class FileTrayService : IDisposable
{
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly object _gate = new();
    private readonly FileTrayList _list;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private int _processing;
    private bool _started, _disposed, _rescanDue;
    private long _generation;

    private sealed record Pending(string Path, WatchedFolder Folder, int Attempt, DateTimeOffset Due, long Generation);

    public FileTrayService(IEnumerable<WatchedFolder> folders, int capacity, IEnumerable<string>? dismissedKeys = null, TimeProvider? time = null)
    {
        Folders = folders.ToList().AsReadOnly();
        _list = new FileTrayList(capacity, dismissedKeys);
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<WatchedFolder> Folders { get; }

    public bool IsStarted { get { lock (_gate) return _started; } }

    public IReadOnlyList<FileTrayEntry> Items { get { lock (_gate) return _list.Items; } }

    public FileTrayEntry? Latest { get { lock (_gate) return _list.Latest; } }

    public int PendingCount { get { lock (_gate) return _pending.Count; } }

    /// <summary>Folders that are missing or could not be watched, as readable messages.</summary>
    public IReadOnlyList<string> Problems { get { lock (_gate) return _problems.ToArray(); } }

    public IReadOnlyCollection<string> DismissedKeys { get { lock (_gate) return _list.DismissedKeys; } }

    /// <summary>Starts the watchers, then scans each folder once (watchers first, so nothing that lands in between is missed).</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            foreach (WatchedFolder folder in Folders)
            {
                if (!Directory.Exists(folder.Path))
                {
                    _problems.Add($"{folder.Label}: folder not found ({folder.Path}).");
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        InternalBufferSize = 16 * 1024,
                    };
                    watcher.Created += (_, e) => OnWritten(folder, e.FullPath);
                    watcher.Changed += (_, e) => OnWritten(folder, e.FullPath);
                    watcher.Renamed += (_, e) => OnRenamed(folder, e.OldFullPath, e.FullPath);
                    watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
                    watcher.Error += (_, e) => OnError(folder, e.GetException());
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    _problems.Add($"{folder.Label}: cannot watch this folder ({ex.Message}).");
                }
            }
        }

        bool changed = Scan();
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads the folders (Refresh button, or after the OS reported a watcher overflow).</summary>
    public void Rescan()
    {
        lock (_gate)
        {
            if (_disposed || !_started) return;
        }

        bool changed = Scan();
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Dismiss(string path) => Mutate(list => list.Dismiss(path));

    public bool Remove(string path) => Mutate(list => list.Remove(path));

    public bool Relocate(string oldPath, string newPath, string sourceLabel) => Mutate(list => list.Relocate(oldPath, newPath, sourceLabel));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
            _pending.Clear();
        }

        _timer.Dispose();
    }

    /// <summary>Describes a ready file for the tray; null when it is not a tray candidate (any more).</summary>
    public static FileTrayEntry? Describe(string path, WatchedFolder folder, DateTimeOffset arrivedUtc)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || !FileTrayFilter.IsCandidate(info) || FileTrayFilter.KindOf(path) is not FileTrayKind kind) return null;
            return new FileTrayEntry(info.FullName, folder.Path, folder.Label, kind, info.Length, info.LastWriteTimeUtc, arrivedUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool Mutate(Func<FileTrayList, bool> change)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return false;
            changed = change(_list);
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private void OnWritten(WatchedFolder folder, string path)
    {
        if (!FileTrayFilter.IsCandidate(path)) return; // no disk access for partial downloads and other file types
        lock (_gate)
        {
            if (_disposed) return;
            string key = FileTrayFilter.Key(path);
            _pending[key] = new Pending(path, folder, 0, _time.GetUtcNow() + SettleDelay, ++_generation);
            ArmTimer();
        }
    }

    private void OnRenamed(WatchedFolder folder, string oldPath, string newPath)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            _pending.Remove(FileTrayFilter.Key(oldPath));
            changed = _list.Remove(oldPath);
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        OnWritten(folder, newPath); // .crdownload -> .pdf lands here
    }

    private void OnDeleted(string path)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            _pending.Remove(FileTrayFilter.Key(path));
            changed = _list.Remove(path);
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnError(WatchedFolder folder, Exception error)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (error is not InternalBufferOverflowException)
            {
                _problems.Add($"{folder.Label}: watching stopped ({error.Message}). Use Refresh after the folder is back.");
            }

            _rescanDue = true;
            _timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private void ArmTimer()
    {
        if (_pending.Count == 0)
        {
            if (!_rescanDue) _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan due = _pending.Values.Min(x => x.Due) - _time.GetUtcNow();
        _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer()
    {
        // One pass at a time; an event that re-arms the timer meanwhile is picked up when this pass re-arms.
        if (Interlocked.Exchange(ref _processing, 1) == 1) return;
        bool changed = false;
        try
        {
            bool rescan;
            List<(string Key, Pending Item)> due;
            lock (_gate)
            {
                if (_disposed) return;
                rescan = _rescanDue;
                _rescanDue = false;
                DateTimeOffset now = _time.GetUtcNow();
                due = _pending.Where(x => x.Value.Due <= now).Select(x => (x.Key, x.Value)).ToList();
            }

            if (rescan) changed |= Scan();
            foreach ((string key, Pending item) in due)
            {
                FileReadiness readiness = FileTrayReadiness.Probe(item.Path);
                FileTrayEntry? entry = readiness == FileReadiness.Ready ? Describe(item.Path, item.Folder, _time.GetUtcNow()) : null;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (!_pending.TryGetValue(key, out Pending? current) || current.Generation != item.Generation) continue; // a newer event wins
                    switch (readiness)
                    {
                        case FileReadiness.Ready:
                            _pending.Remove(key);
                            if (entry is not null) changed |= _list.Upsert(entry);
                            break;
                        case FileReadiness.Empty:
                        case FileReadiness.Locked:
                            TimeSpan? delay = FileTrayReadiness.RetryDelay(item.Attempt + 1);
                            if (delay is null) _pending.Remove(key);
                            else _pending[key] = item with { Attempt = item.Attempt + 1, Due = _time.GetUtcNow() + delay.Value };
                            break;
                        default:
                            _pending.Remove(key);
                            changed |= _list.Remove(item.Path);
                            break;
                    }
                }
            }
        }
        finally
        {
            // Clear the flag before re-arming, so a tick that arrives now is processed rather than skipped.
            Volatile.Write(ref _processing, 0);
            lock (_gate)
            {
                if (!_disposed) ArmTimer();
            }
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Top-level listing of each folder; locked or still-empty files become pending instead of shown.</summary>
    private bool Scan()
    {
        var found = new List<(FileTrayEntry Entry, WatchedFolder Folder)>();
        int capacity;
        lock (_gate) capacity = _list.Capacity;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary,
        };
        foreach (WatchedFolder folder in Folders)
        {
            try
            {
                if (!Directory.Exists(folder.Path)) continue;
                found.AddRange(new DirectoryInfo(folder.Path)
                    .EnumerateFiles("*", options)
                    .Where(x => x.Length > 0 && FileTrayFilter.IsCandidate(x))
                    .Select(x => (Entry: new FileTrayEntry(x.FullName, folder.Path, folder.Label, FileTrayFilter.KindOf(x.FullName)!.Value, x.Length, x.LastWriteTimeUtc,
                        x.CreationTimeUtc > x.LastWriteTimeUtc ? x.CreationTimeUtc : x.LastWriteTimeUtc), Folder: folder))
                    .OrderBy(x => x.Entry, FileTrayList.Order)
                    .Take(capacity));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate) _problems.Add($"{folder.Label}: cannot list this folder ({ex.Message}).");
            }
        }

        var newest = found.OrderBy(x => x.Entry, FileTrayList.Order).Take(capacity).ToList();
        var ready = newest.Select(x => (x.Entry, x.Folder, Readiness: FileTrayReadiness.Probe(x.Entry.Path))).ToList();
        bool changed = false;
        lock (_gate)
        {
            if (_disposed) return false;
            changed |= _list.Prune(File.Exists);
            foreach (var (entry, folder, readiness) in ready)
            {
                if (readiness == FileReadiness.Ready)
                {
                    changed |= _list.Upsert(entry);
                }
                else if (readiness is FileReadiness.Locked or FileReadiness.Empty && !_pending.ContainsKey(entry.Key))
                {
                    _pending[entry.Key] = new Pending(entry.Path, folder, 0, _time.GetUtcNow() + FileTrayReadiness.RetryDelay(0)!.Value, ++_generation);
                }
            }

            ArmTimer();
        }

        return changed;
    }
}
