// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.cs

using Salandual.Core.Listing;
using Salandual.Core.Vfs;

using Salandual.Core.Resources;

namespace Salandual.Core.Panels;

[Flags]
public enum PanelChange
{
    None = 0,
    Focus = 1,
    Selection = 2,

    Items = 4,
    QuickSearch = 8,

    Sizes = 16,

    View = 32,

    Loading = 64,
}

public readonly record struct HistoryStep(string Path, string? Focus, long Order);

public readonly record struct SelectionSummary(int Files, int Folders, long Bytes)
{
    public int Count => Files + Folders;
}

public sealed partial class FilePanel : IDisposable
{
    public static readonly FileEntry UpEntry = new("..", 0, 0, FileAttributes.Directory);

    private readonly IFileSystemProvider _provider;
    private EntrySorter _sorter;
    private readonly Action<Action> _post;
    private FileEntry[] _items = [];
    private bool[] _selected = [];
    private IDisposable? _watch;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _refreshRunning;
    private int _loadingVersion;
    private bool _refreshPending;
    private bool? _extendState;
    private HashSet<string>? _rememberedSelection;
    private readonly Dictionary<string, long> _dirSizes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sizing = new(StringComparer.Ordinal);
    private CancellationTokenSource? _sizeCts;
    private readonly Dictionary<string, (string Name, int Row)> _screenRows = new(StringComparer.OrdinalIgnoreCase);
    private int? _focusScreenRow;
    private readonly List<HistoryStep> _back = [];
    private readonly List<HistoryStep> _forward = [];
    private int _historyLimit = MaxHistory;
    private bool _movingInHistory;
    private string? _pendingFocus;
    private HashSet<string>? _pendingSelect;

    public FilePanel(IFileSystemProvider provider, EntrySorter sorter, Action<Action> post)
    {
        _provider = provider;
        _sorter = sorter;
        _post = post;
    }

    public event EventHandler<PanelChange>? Changed;

    public event EventHandler<string>? Error;

    public event EventHandler<string>? ExecuteRequested;

    public string Path { get; private set; } = "";

    public IReadOnlyList<FileEntry> Items => _items;

    public int Count => _items.Length;

    public bool HasUpEntry { get; private set; }

    public int Focus { get; private set; }

    public SortSpec Sort { get; private set; } = SortSpec.Default;

    public bool ShowHidden { get; private set; }

    public PanelView View { get; private set; } = PanelView.Detailed;

    public bool SelectFoldersWithNumPlus { get; set; }

    public TimeSpan LastListTime { get; private set; }

    public string? QuickSearchText { get; private set; }

    public bool IsUp(int index) => HasUpEntry && index == 0;

    public bool IsSelected(int index) => (uint)index < (uint)_selected.Length && _selected[index];

    public FileEntry? FocusedEntry => Focus < _items.Length ? _items[Focus] : null;

    public SelectionSummary GetSelectionSummary()
    {
        int files = 0, folders = 0;
        long bytes = 0;
        for (var i = 0; i < _items.Length; i++)
        {
            if (!_selected[i])
            {
                continue;
            }

            if (_items[i].IsDirectory)
            {
                folders++;
            }
            else
            {
                files++;
                bytes += _items[i].Size;
            }

            if (_items[i].IsDirectory && _dirSizes.TryGetValue(_items[i].Name, out var size))
            {
                bytes += size;
            }
        }

        return new SelectionSummary(files, folders, bytes);
    }

    public IReadOnlyList<string> GetOperationNames()
    {
        var names = new List<string>();
        for (var i = 0; i < _items.Length; i++)
        {
            if (_selected[i])
            {
                names.Add(_items[i].Name);
            }
        }

        if (names.Count == 0 && FocusedEntry is { } f && !IsUp(Focus))
        {
            names.Add(f.Name);
        }

        return names;
    }

    public Task<bool> NavigateAsync(string path, string? focusName = null)
        => Diverted(path, focusName) ? Task.FromResult(true) : LoadAsync(path, focusName, keepSelection: false, reportErrors: true);

    public Func<string, string?, bool>? LeavingFolder { get; set; }

    private bool Diverted(string path, string? focusName)
        => LeavingFolder is { } leaving && Path.Length > 0 && Results is null
           && !string.Equals(path.TrimEnd('\\', '/'), Path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) && leaving(path, focusName);

    public void CancelLoading() => _loadCts?.Cancel();

    public long? LoadingSince { get; private set; }

    public string? LoadingPath { get; private set; }

    private void EndLoading(int version)
    {
        if (version == _loadingVersion && LoadingSince is not null)
        {
            LoadingSince = null;
            LoadingPath = null;
            Changed?.Invoke(this, PanelChange.Loading);
        }
    }

    public const int MaxHistory = 50;

    private static long _stepCount;

    public int HistoryLimit
    {
        get => _historyLimit;
        set
        {
            _historyLimit = Math.Max(1, value);
            Trim(_back);
            Trim(_forward);
        }
    }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public IReadOnlyList<HistoryStep> BackHistory => _back;

    public IReadOnlyList<HistoryStep> ForwardHistory => _forward;

    public event EventHandler? HistoryChanged;

    public Task<bool> GoBackAsync() => MoveInHistory(_back, _forward);

    public Task<bool> GoForwardAsync() => MoveInHistory(_forward, _back);

    public Task<bool> GoBackAsync(int steps) => Jump(_back, _forward, steps);

    public Task<bool> GoForwardAsync(int steps) => Jump(_forward, _back, steps);

    public void ClearHistory()
    {
        _back.Clear();
        _forward.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> Jump(List<HistoryStep> from, List<HistoryStep> to, int steps)
    {
        steps = Math.Clamp(steps, 1, Math.Max(1, from.Count));
        if (steps == 1)
        {
            return await MoveInHistory(from, to);
        }

        var passed = from.GetRange(from.Count - steps + 1, steps - 1);
        from.RemoveRange(from.Count - steps + 1, steps - 1);
        if (await MoveInHistory(from, to))
        {
            passed.Reverse();
            to.AddRange(passed);
            Trim(to);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        from.AddRange(passed);
        return false;
    }

    private void Trim(List<HistoryStep> steps)
    {
        if (steps.Count > _historyLimit)
        {
            steps.RemoveRange(0, steps.Count - _historyLimit);
        }
    }

    private static HistoryStep Step(string path, string? focus) => new(path, focus, Interlocked.Increment(ref _stepCount));

    private async Task<bool> MoveInHistory(List<HistoryStep> from, List<HistoryStep> to)
    {
        if (from.Count > 0 && Diverted(from[^1].Path, from[^1].Focus))
        {
            return true;
        }

        while (from.Count > 0)
        {
            var (path, focus, _) = from[^1];
            from.RemoveAt(from.Count - 1);
            var here = Step(Path, FocusedEntry?.Name);
            _movingInHistory = true;
            bool ok;
            try
            {
                ok = await LoadAsync(path, focus, keepSelection: false, reportErrors: from.Count == 0);
            }
            finally
            {
                _movingInHistory = false;
            }

            if (ok)
            {
                to.Add(here);
                Trim(to);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private void RecordHistory()
    {
        if (_movingInHistory || Path.Length == 0)
        {
            return;
        }

        _back.Add(Step(Path, FocusedEntry?.Name));
        Trim(_back);
        _forward.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowListing(DirectoryListing listing, bool watch = true)
    {
        _loadCts?.Cancel();
        ++_loadVersion;
        var visible = EntryFilter.Apply(listing.Entries, ShowHidden);
        if (!listing.KeepOrder)
        {
            _sorter.Sort(visible, Sort);
        }

        Apply(listing.Path, visible, focusName: null, keepSelection: false);
        LastListTime = listing.Elapsed;
        if (watch)
        {
            StartWatching(listing.Path);
        }
    }

    public void StartWatching()
    {
        if (_watch is null && Path.Length > 0 && Results is null)
        {
            StartWatching(Path);
            _ = RefreshAsync();
        }
    }

    public Task<bool> RefreshAsync()
    {
        if (IsGitList)
        {
            return RereadGitListAsync();
        }

        if (IsFlat)
        {
            return ShowFlatAsync();
        }

        if (Results is not null)
        {
            return RefreshResultsAsync(null);
        }

        if (Path.Length == 0)
        {
            return Task.FromResult(false);
        }

        return LoadAsync(Path, null, keepSelection: true, reportErrors: true, focusAsItEnds: true);
    }

    public Task<bool> RefreshAsync(string focusName, IEnumerable<string>? select = null)
    {
        if (IsGitList)
        {
            return RereadGitListAsync(focusName);
        }

        if (IsFlat)
        {
            return ShowFlatAsync();
        }

        if (Results is not null)
        {
            return RefreshResultsAsync(focusName);
        }

        _pendingFocus = focusName;
        _pendingSelect = select is null ? null : new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
        return LoadAsync(Path, focusName, keepSelection: true, reportErrors: true);
    }

    public async Task GoParentAsync()
    {
        if (Results is not null)
        {
            await LeaveResultsAsync();
            return;
        }

        if (_provider.GetParent(Path) is { } parent)
        {
            await NavigateAsync(parent, focusName: _provider.GetName(Path));
        }
    }

    public async Task GoRootAsync()
    {
        if (_provider.GetRoot(Path) is { } root && !string.Equals(root.TrimEnd('\\', '/'), Path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            await NavigateAsync(root);
        }
    }

    public async Task OpenFocusedAsync(bool foldersOnly = false)
    {
        if (FocusedEntry is not { } entry)
        {
            return;
        }

        if (IsGitList && IsHeading(Focus))
        {
            return;
        }

        if ((IsHistory || IsCommit || IsTree || IsTags) && GitItemOpened is { } open && await open(entry))
        {
            return;
        }

        if (IsUp(Focus))
        {
            await GoParentAsync();
        }
        else if (entry.IsDirectory)
        {
            await NavigateAsync(_provider.Combine(Path, entry.Name));
        }
        else if (!foldersOnly)
        {
            ExecuteRequested?.Invoke(this, _provider.Combine(Path, entry.Name));
        }
        else if (Results is null)
        {
            await NavigateAsync(_provider.Combine(Path, entry.Name));
        }
    }

    private async Task<bool> LoadAsync(string path, string? focusName, bool keepSelection, bool reportErrors, bool focusAsItEnds = false)
    {
        var isRefresh = keepSelection && path == Path;
        if (isRefresh && _refreshRunning)
        {
            _refreshPending = true;
            return true;
        }

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var version = ++_loadVersion;
        var sort = Sort;
        var showHidden = ShowHidden;
        _loadingVersion = version;
        LoadingSince = Environment.TickCount64;
        LoadingPath = path;
        Changed?.Invoke(this, PanelChange.Loading);
        _refreshRunning = isRefresh;
        try
        {
            var (listing, sorted) = await Task.Run(async () =>
            {
                var l = await _provider.ListAsync(path, cts.Token).ConfigureAwait(false);
                var visible = EntryFilter.Apply(l.Entries, showHidden);
                if (!l.KeepOrder)
                {
                    _sorter.Sort(visible, sort);
                }

                return (l, visible);
            }, cts.Token).WaitAsync(cts.Token);

            if (version != _loadVersion)
            {
                return false;
            }

            if (focusAsItEnds)
            {
                focusName = FocusedEntry?.Name;
            }

            var newFolder = listing.Path != Path || Results is not null;
            Apply(listing.Path, sorted, focusName, keepSelection && !newFolder);
            LastListTime = listing.Elapsed;
            if (newFolder)
            {
                StartWatching(listing.Path);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (version == _loadVersion && reportErrors)
            {
                Error?.Invoke(this, ex.Message);
            }

            return false;
        }
        finally
        {
            EndLoading(version);
            if (version == _loadVersion)
            {
                _refreshRunning = false;
                if (_refreshPending)
                {
                    _refreshPending = false;
                    _post(() => _ = RefreshAsync());
                }
            }
        }
    }

    private void Apply(string path, FileEntry[] sorted, string? focusName, bool keepSelection)
    {
        var quick = path == Path && IsQuickFiltering ? QuickSearchText : null;
        if (path != Path)
        {
            LeaveFolderFilters();
        }

        _listed = sorted;
        if (quick is null)
        {
            QuickSearchText = null;
        }

        sorted = Visible(sorted);
        HashSet<string>? keep = null;
        if (keepSelection)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                if (_selected[i])
                {
                    (keep ??= new HashSet<string>(StringComparer.Ordinal)).Add(_items[i].Name);
                }
            }
        }

        if (path == Path && _pendingFocus is { } pending && sorted.Any(e => string.Equals(e.Name, pending, StringComparison.OrdinalIgnoreCase)))
        {
            focusName = pending;
            if (_pendingSelect is { } select)
            {
                keep = new HashSet<string>(sorted.Where(e => select.Contains(e.Name)).Select(e => e.Name), StringComparer.Ordinal);
            }

            _pendingFocus = null;
            _pendingSelect = null;
        }

        var leaving = Results is not null;
        _flatCts?.Cancel();
        Results = null;
        _stat = null;
        _keepOrder = false;
        if (path != Path || leaving)
        {
            if (!leaving)
            {
                RememberSelection();
                RememberScreenRow();
                RecordHistory();
            }

            _pendingFocus = null;
            _pendingSelect = null;
            _sizeCts?.Cancel();
            _sizeCts = null;
            _dirSizes.Clear();
            _marks = null;
            _sizing.Clear();
        }

        var oldFocus = Focus;
        Path = path;
        HasUpEntry = View != PanelView.Columns && _provider.GetParent(path) is not null;
        _items = HasUpEntry ? [UpEntry, .. sorted] : sorted;
        _selected = new bool[_items.Length];
        if (keep is not null)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                _selected[i] = !IsUp(i) && keep.Contains(_items[i].Name);
            }
        }

        var found = focusName is null ? -1 : IndexOf(focusName);
        Focus = found >= 0 ? found : keepSelection ? Math.Clamp(oldFocus, 0, Math.Max(0, _items.Length - 1)) : 0;
        _focusScreenRow = found >= 0 && !keepSelection && _screenRows.TryGetValue(path, out var row) && string.Equals(row.Name, _items[found].Name, StringComparison.OrdinalIgnoreCase)
            ? row.Row
            : null;
        QuickSearchText = quick;
        _extendState = null;
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus | PanelChange.QuickSearch);
    }

    private int IndexOf(string name)
    {
        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            if (string.Equals(_items[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            if (string.Equals(_items[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void StartWatching(string path)
    {
        _watch?.Dispose();
        _watch = null;
        if ((_provider.Capabilities & ProviderCapabilities.Watch) == 0)
        {
            return;
        }

        try
        {
            _watch = _provider.Watch(path,
                change => _post(() =>
                {
                    if (change.Path == Path)
                    {
                        _ = RefreshAsync();
                    }
                }),
                error => _post(() => _ = RecoverAsync(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private async Task RecoverAsync(string path)
    {
        if (path != Path)
        {
            return;
        }

        var focus = FocusedEntry?.Name;
        for (var p = (string?)path; p is not null; p = _provider.GetParent(p))
        {
            if (await LoadAsync(p, p == path ? focus : null, keepSelection: p == path, reportErrors: false))
            {
                if (p != path)
                {
                    StartWatching(Path);
                }

                return;
            }
        }

        Error?.Invoke(this, Loc.Format(Strings.Panel_FolderGone, path));
    }

    public void ToggleSort(SortColumn column) => SetSort(Sort.Toggle(column));

    public void SetSort(SortSpec sort)
    {
        if (sort == Sort)
        {
            return;
        }

        Sort = sort;
        Resort();
    }

    public void SetSorter(EntrySorter sorter)
    {
        ArgumentNullException.ThrowIfNull(sorter);
        if (ReferenceEquals(sorter, _sorter))
        {
            return;
        }

        _sorter = sorter;
        Resort();
    }

    public void SetView(PanelView view)
    {
        view = PanelViews.Valid(view);
        if (view != View)
        {
            View = view;
            SetUpEntry(view != PanelView.Columns);
            Changed?.Invoke(this, PanelChange.View);
        }
    }

    public void SetShowHidden(bool show)
    {
        if (show == ShowHidden)
        {
            return;
        }

        ShowHidden = show;
        if (Path.Length > 0)
        {
            _ = RefreshAsync();
        }
    }

    private void Resort()
    {
        if (IsGitList)
        {
            return;
        }

        if (IsFlat)
        {
            SortFlat(_listed);
            Refilter();
            return;
        }

        if (_items.Length == 0)
        {
            return;
        }

        var start = HasUpEntry ? 1 : 0;
        var focusName = FocusedEntry?.Name;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < _items.Length; i++)
        {
            if (_selected[i])
            {
                selected.Add(_items[i].Name);
            }
        }

        var body = _items[start..];
        if (Results is not null)
        {
            _keepOrder = false;
            SortResults(body);
        }
        else
        {
            _sorter.Sort(body, Sort);
            if (!ReferenceEquals(_items, _listed))
            {
                _sorter.Sort(_listed, Sort);
            }
        }

        body.CopyTo(_items, start);
        for (var i = start; i < _items.Length; i++)
        {
            _selected[i] = selected.Contains(_items[i].Name);
        }

        Focus = IsUp(Focus) || focusName is null ? Focus : Math.Max(0, IndexOf(focusName));
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus);
    }

    public void SetFocus(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, _items.Length - 1));
        _extendState = null;
        if (index != Focus)
        {
            Focus = index;
            Changed?.Invoke(this, PanelChange.Focus);
        }
    }

    public void Toggle(int index)
    {
        if ((uint)index >= (uint)_items.Length || IsUp(index) || IsHeading(index))
        {
            return;
        }

        _selected[index] = !_selected[index];
        _extendState = null;
        Changed?.Invoke(this, PanelChange.Selection);
    }

    public void ExtendTo(int target, bool includeTarget)
    {
        if (_items.Length == 0)
        {
            return;
        }

        target = Math.Clamp(target, 0, _items.Length - 1);
        var state = _extendState ??= IsUp(Focus) ? true : !_selected[Focus];
        int from, to;
        if (target == Focus)
        {
            from = to = Focus;
        }
        else if (target > Focus)
        {
            from = Focus;
            to = includeTarget ? target : target - 1;
        }
        else
        {
            from = includeTarget ? target : target + 1;
            to = Focus;
        }

        for (var i = from; i <= to; i++)
        {
            if (!IsUp(i) && !IsHeading(i))
            {
                _selected[i] = state;
            }
        }

        Focus = target;
        Changed?.Invoke(this, PanelChange.Selection | PanelChange.Focus);
    }

    public void SelectWhere(Func<FileEntry, bool> predicate, bool select)
    {
        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            if (!IsHeading(i) && predicate(_items[i]))
            {
                _selected[i] = select;
            }
        }

        _extendState = null;
        Changed?.Invoke(this, PanelChange.Selection);
    }

    public void Invert(bool includeFolders)
    {
        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            if ((includeFolders && !IsHeading(i)) || !_items[i].IsDirectory)
            {
                _selected[i] = !_selected[i];
            }
        }

        _extendState = null;
        Changed?.Invoke(this, PanelChange.Selection);
    }

    public void SelectByMask(FileMask mask, bool select)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (!select)
        {
            RememberSelection();
        }

        SelectWhere(e => (SelectFoldersWithNumPlus || !e.IsDirectory) && mask.IsMatch(SplitResult(e.Name).Name), select);
    }

    public void RememberSelection()
    {
        HashSet<string>? names = null;
        for (var i = 0; i < _items.Length; i++)
        {
            if (_selected[i])
            {
                (names ??= new HashSet<string>(StringComparer.Ordinal)).Add(_items[i].Name);
            }
        }

        if (names is not null)
        {
            _rememberedSelection = names;
        }
    }

    public void RestoreSelection()
    {
        if (_rememberedSelection is not { } names)
        {
            return;
        }

        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            _selected[i] = !IsHeading(i) && names.Contains(_items[i].Name);
        }

        _extendState = null;
        Changed?.Invoke(this, PanelChange.Selection);
    }

    public bool CanRestoreSelection => _rememberedSelection is not null;

    public int TopIndex { get; set; }

    public int? TakeFocusScreenRow()
    {
        var row = _focusScreenRow;
        _focusScreenRow = null;
        return row;
    }

    private void RememberScreenRow()
    {
        if (Path.Length == 0 || FocusedEntry is not { } focused)
        {
            return;
        }

        if (_screenRows.Count >= 500)
        {
            _screenRows.Clear();
        }

        _screenRows[Path] = (focused.Name, Math.Max(0, Focus - TopIndex));
    }

    public bool TryGetDirectorySize(in FileEntry entry, out long size)
    {
        size = 0;
        return entry.IsDirectory && _dirSizes.TryGetValue(entry.Name, out size);
    }

    public bool IsSizing(in FileEntry entry) => entry.IsDirectory && _sizing.Contains(entry.Name);

    public void CalculateSize(int index)
    {
        if ((uint)index >= (uint)_items.Length || IsUp(index) || !_items[index].IsDirectory)
        {
            return;
        }

        var name = _items[index].Name;
        if (_dirSizes.ContainsKey(name) || !_sizing.Add(name))
        {
            return;
        }

        var cts = _sizeCts ??= new CancellationTokenSource();
        var path = _provider.Combine(Path, name);
        Changed?.Invoke(this, PanelChange.Sizes);
        _ = Task.Run(async () =>
        {
            long? size;
            try
            {
                size = await DirectorySize.ComputeAsync(_provider, path, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                size = null;
            }

            _post(() =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                _sizing.Remove(name);
                if (size is { } s)
                {
                    _dirSizes[name] = s;
                }

                Changed?.Invoke(this, PanelChange.Sizes);
            });
        });
    }

    public bool CancelSizing()
    {
        if (_sizing.Count == 0)
        {
            return false;
        }

        _sizeCts?.Cancel();
        _sizeCts = null;
        _sizing.Clear();
        Changed?.Invoke(this, PanelChange.Sizes);
        return true;
    }

    public int FindSelected(int start, int direction)
    {
        for (var i = start; i >= 0 && i < _items.Length; i += direction)
        {
            if (_selected[i])
            {
                return i;
            }
        }

        return -1;
    }

    public static IReadOnlySet<string> HandledCommands { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Focus.Previous", "Focus.Next", "Focus.PageUp", "Focus.PageDown", "Focus.First", "Focus.Last", "Focus.FirstFile",
        "Focus.PreviousSelected", "Focus.NextSelected", "Focus.FirstSelected", "Focus.LastSelected", "Focus.Left", "Focus.Right",
        "Selection.ExtendUp", "Selection.ExtendDown", "Selection.ExtendPageUp", "Selection.ExtendPageDown",
        "Selection.ExtendToFirst", "Selection.ExtendToLast", "Selection.ExtendLeft", "Selection.ExtendRight",
        "Selection.ToggleAndNext", "Selection.ToggleAndSize", "Selection.SelectAll", "Selection.UnselectAll",
        "Selection.InvertFiles", "Selection.InvertAll", "Selection.SameExtension", "Selection.UnselectSameExtension",
        "Selection.SameName", "Selection.UnselectSameName", "Selection.Restore",
        "Navigation.Execute", "Navigation.OpenFocused", "Navigation.Parent", "Navigation.Root", "Navigation.GoToFile", "History.Back", "History.Forward",
        "Panel.SortByName", "Panel.SortByExtension", "Panel.SortByTime", "Panel.SortBySize", "Panel.Refresh", "Panel.ToggleHidden",
        "Panel.FilterOff", "Panel.HideSelectedNames", "Panel.HideUnselectedNames", "Panel.ShowHiddenNames",
        "View.Mode1", "View.Mode2", "View.Mode3", "View.Mode4", "View.Mode5", "View.Mode6", "View.Mode7",
    };

    public bool Execute(string commandId, int pageRows)
    {
        var page = Math.Max(1, pageRows - 1);
        var extendState = _extendState;
        switch (commandId)
        {
            case "Focus.Previous": SetFocus(Focus - 1); break;
            case "Focus.Next": SetFocus(Focus + 1); break;
            case "Focus.PageUp": SetFocus(Focus - page); break;
            case "Focus.PageDown": SetFocus(Focus + page); break;
            case "Focus.First": SetFocus(0); break;
            case "Focus.Last": SetFocus(_items.Length - 1); break;
            case "Focus.FirstFile": SetFocus(Array.FindIndex(_items, e => !e.IsDirectory) is var f and >= 0 ? f : _items.Length - 1); break;
            case "Focus.PreviousSelected": GoSelected(FindSelected(Focus - 1, -1)); break;
            case "Focus.NextSelected": GoSelected(FindSelected(Focus + 1, 1)); break;
            case "Focus.FirstSelected": GoSelected(FindSelected(0, 1)); break;
            case "Focus.LastSelected": GoSelected(FindSelected(_items.Length - 1, -1)); break;
            case "Focus.Left" or "Focus.Right": break;

            case "Selection.ExtendUp": _extendState = extendState; ExtendTo(Focus - 1, false); return true;
            case "Selection.ExtendDown": _extendState = extendState; ExtendTo(Focus + 1, false); return true;
            case "Selection.ExtendPageUp": _extendState = extendState; ExtendTo(Focus - page, false); return true;
            case "Selection.ExtendPageDown": _extendState = extendState; ExtendTo(Focus + page, false); return true;
            case "Selection.ExtendToFirst": _extendState = extendState; ExtendTo(0, true); return true;
            case "Selection.ExtendToLast": _extendState = extendState; ExtendTo(_items.Length - 1, true); return true;
            case "Selection.ExtendLeft" or "Selection.ExtendRight": break;

            case "Selection.ToggleAndNext": Toggle(Focus); SetFocus(Focus + 1); break;
            case "Selection.ToggleAndSize":
                Toggle(Focus);
                if (IsSelected(Focus))
                {
                    CalculateSize(Focus);
                }

                SetFocus(Focus + 1);
                break;
            case "Selection.SelectAll": SelectWhere(_ => true, true); break;
            case "Selection.UnselectAll": RememberSelection(); SelectWhere(_ => true, false); break;
            case "Selection.Restore": RestoreSelection(); break;
            case "Selection.InvertFiles": Invert(SelectFoldersWithNumPlus); break;
            case "Selection.InvertAll": Invert(true); break;
            case "Selection.SameExtension": SameExtension(true); break;
            case "Selection.UnselectSameExtension": SameExtension(false); break;
            case "Selection.SameName": SameName(true); break;
            case "Selection.UnselectSameName": SameName(false); break;

            case "Navigation.Execute": _ = OpenFocusedAsync(); break;
            case "Navigation.OpenFocused": _ = OpenFocusedAsync(foldersOnly: true); break;
            case "Navigation.Parent": _ = GoParentAsync(); break;
            case "Navigation.Root": _ = GoRootAsync(); break;
            case "Navigation.GoToFile": _ = GoToResultAsync(); break;
            case "History.Back": _ = GoBackAsync(); break;
            case "History.Forward": _ = GoForwardAsync(); break;

            case "Panel.SortByName": ToggleSort(SortColumn.Name); break;
            case "Panel.SortByExtension": ToggleSort(SortColumn.Extension); break;
            case "Panel.SortByTime": ToggleSort(SortColumn.Time); break;
            case "Panel.SortBySize": ToggleSort(SortColumn.Size); break;
            case "Panel.Refresh": _ = RefreshAsync(); break;
            case "Panel.ToggleHidden": SetShowHidden(!ShowHidden); break;
            case "Panel.FilterOff": ClearFilter(); break;
            case "Panel.HideSelectedNames": HideNames(selected: true); break;
            case "Panel.HideUnselectedNames": HideNames(selected: false); break;
            case "Panel.ShowHiddenNames": ShowHiddenNames(); break;
            case "View.Mode1" or "View.Mode2" or "View.Mode3" or "View.Mode4" or "View.Mode5" or "View.Mode6" or "View.Mode7": SetView(PanelViews.FromCommand(commandId)!.Value); break;

            default:
                return false;
        }

        _extendState = null;
        return true;
    }

    private void GoSelected(int index)
    {
        if (index >= 0)
        {
            SetFocus(index);
        }
    }

    private void SameExtension(bool select)
    {
        if (FocusedEntry is not { IsDirectory: false } focused)
        {
            return;
        }

        var ext = focused.Extension.ToString();
        SelectWhere(e => !e.IsDirectory && e.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase), select);
    }

    private void SameName(bool select)
    {
        if (FocusedEntry is not { } focused || IsUp(Focus))
        {
            return;
        }

        var baseName = focused.BaseName.ToString();
        SelectWhere(e => !e.IsDirectory && e.BaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase), select);
    }

    private bool Matches(int index, string pattern)
    {
        if (IsUp(index) || IsHeading(index))
        {
            return false;
        }

        var name = Results is not null ? SplitResult(_items[index].Name).Name : _items[index].Name;
        return QuickFilterMode ? QuickFilterMatch(name, pattern) : QuickSearchPattern.IsMatch(name, pattern);
    }

    private int FindMatch(int start, int direction, string pattern)
    {
        for (var i = start; i >= 0 && i < _items.Length; i += direction)
        {
            if (Matches(i, pattern))
            {
                return i;
            }
        }

        return -1;
    }

    public bool QuickSearchType(char c)
    {
        if (QuickSearchText is null)
        {
            QuickFilterMode = QuickFilterFirst && (Results is null || IsFlat);
        }

        var pattern = (QuickSearchText ?? "") + c;
        var index = QuickSearchText is not null && Matches(Focus, pattern) ? Focus : FindMatch(0, 1, pattern);
        if (index < 0)
        {
            return false;
        }

        QuickSearchText = pattern;
        Focus = index;
        _extendState = null;
        if (QuickFilterMode)
        {
            Refilter(firstIfGone: true);
        }

        Changed?.Invoke(this, PanelChange.QuickSearch | PanelChange.Focus);
        return true;
    }

    public void QuickSearchBack()
    {
        if (QuickSearchText is null)
        {
            return;
        }

        QuickSearchText = QuickSearchText.Length > 1 ? QuickSearchText[..^1] : null;
        if (QuickFilterMode)
        {
            Refilter();
        }

        Changed?.Invoke(this, PanelChange.QuickSearch);
    }

    public void QuickSearchExtend()
    {
        if (QuickSearchText is not { } text || QuickFilterMode || QuickSearchPattern.HasWildcard(text) || FocusedEntry is not { } focused || focused.Name.Length <= text.Length)
        {
            return;
        }

        QuickSearchText = focused.Name[..(text.Length + 1)];
        Changed?.Invoke(this, PanelChange.QuickSearch);
    }

    public void EndQuickSearch(bool keepFilter = false)
    {
        if (QuickSearchText is not { } text)
        {
            return;
        }

        var filtering = IsQuickFiltering;
        QuickSearchText = null;
        _extendState = null;
        if (filtering)
        {
            if (keepFilter)
            {
                (_keptQuickFilters ??= []).Add(text);
            }

            Refilter();
        }

        Changed?.Invoke(this, PanelChange.QuickSearch);
    }

    public void QuickSearchMove(int steps, bool select = false, bool selectedOnly = false)
    {
        if (QuickSearchText is not { } pattern || steps == 0)
        {
            return;
        }

        bool Ok(int i) => Matches(i, pattern) && (!selectedOnly || _selected[i]);
        var direction = steps > 0 ? 1 : -1;
        var target = -1;
        if (steps is int.MinValue or int.MaxValue)
        {
            for (var i = steps > 0 ? _items.Length - 1 : 0; i >= 0 && i < _items.Length; i -= direction)
            {
                if (Ok(i))
                {
                    target = i;
                    break;
                }
            }
        }
        else
        {
            var remaining = Math.Abs(steps);
            for (var i = Focus + direction; i >= 0 && i < _items.Length && remaining > 0; i += direction)
            {
                if (Ok(i))
                {
                    target = i;
                    remaining--;
                }
            }
        }

        if (target < 0 || target == Focus)
        {
            return;
        }

        if (select && !IsUp(Focus))
        {
            var state = _extendState ??= !_selected[Focus];
            _selected[Focus] = state;
        }

        Focus = target;
        Changed?.Invoke(this, PanelChange.Focus | (select ? PanelChange.Selection : PanelChange.None));
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _sizeCts?.Cancel();
        _flatCts?.Cancel();
        _watch?.Dispose();
    }
}
