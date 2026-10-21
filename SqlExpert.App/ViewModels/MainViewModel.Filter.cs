using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Threading;
using SqlExpert.Core.Explorer;

namespace SqlExpert.App.ViewModels;

/// <summary>
/// Database Explorer "Enter filter string": deep, debounced filter over the metadata already loaded.
/// <list type="bullet">
/// <item>Typing restarts a ~220 ms timer; each run snapshots the tree into a flat
/// <see cref="ExplorerFilterIndex"/> (UI thread, O(n)), matches it on the thread pool (stale runs are
/// cancelled) and applies the result in one batch: plain <c>FilterVisible</c> flags + one
/// CollectionView refresh per collection whose visible set changed. No server query per keystroke.</item>
/// <item>The first run loads (once, in the background, cached in the nodes) the object lists of the
/// databases of the connection whose folders are already loaded; the filter re-runs when they arrive.</item>
/// <item>Ancestors of matches are auto-expanded; clearing the filter restores the previous expansion.</item>
/// </list>
/// </summary>
public sealed partial class MainViewModel
{
    internal const int FilterDebounceMs = 220;
    /// <summary>Re-run delay after new nodes were loaded while filtering (coalesces bursts of loads).</summary>
    private const int FilterTreeChangedDelayMs = 40;

    private DispatcherTimer? _filterTimer;
    private CancellationTokenSource? _filterCts;
    private bool _filterActive;
    private bool _restoringFilterExpansion;
    /// <summary>Expansion of every node when the filter became active (restored on clear).</summary>
    private Dictionary<ExplorerNode, bool>? _savedExpansion;
    /// <summary>Nodes expanded by the filter (not by the user).</summary>
    private readonly HashSet<ExplorerNode> _filterExpanded = new();
    /// <summary>Database nodes whose object lists were already requested for the filter.</summary>
    private readonly HashSet<ExplorerNode> _filterPreloaded = new();
    private bool _filterPreloadRunning;

    /// <summary>True while a non-empty filter is applied to the Database Explorer.</summary>
    public bool IsExplorerFilterActive => _filterActive;

    /// <summary>Last UI-thread time of a filter run (snapshot + apply), for diagnostics.</summary>
    internal double LastFilterUiMs { get; private set; }

    private void OnFilterTextChanged()
    {
        if (ExplorerFilterText.NormalizeQuery(_filterText).Length == 0)
        {
            // Clearing is immediate (Goto Database / ✕ rely on it).
            _filterTimer?.Stop();
            ClearExplorerFilter();
            return;
        }
        ScheduleFilterRun(FilterDebounceMs);
    }

    /// <summary>Enter in the filter box: run now instead of waiting for the debounce.</summary>
    public void ApplyExplorerFilterNow()
    {
        _filterTimer?.Stop();
        if (ExplorerFilterText.NormalizeQuery(_filterText).Length == 0) ClearExplorerFilter();
        else RunSafe(RunExplorerFilterAsync);
    }

    /// <summary>Called after nodes were added/replaced (lazy loads, refresh, sort): re-filters if active.</summary>
    private void OnExplorerTreeChanged()
    {
        if (_filterActive)
            ScheduleFilterRun(FilterTreeChangedDelayMs);
    }

    private void ScheduleFilterRun(int delayMs)
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher)
            return;
        if (_filterTimer is null)
        {
            _filterTimer = new DispatcherTimer(DispatcherPriority.Input, dispatcher);
            _filterTimer.Tick += (_, _) =>
            {
                _filterTimer!.Stop();
                RunSafe(RunExplorerFilterAsync);
            };
        }
        // Debounce: a pending keystroke run is pushed back; a tree-change run never delays a sooner one.
        var interval = TimeSpan.FromMilliseconds(delayMs);
        if (_filterTimer.IsEnabled && delayMs < FilterDebounceMs && _filterTimer.Interval <= interval)
            return;
        _filterTimer.Stop();
        _filterTimer.Interval = interval;
        _filterTimer.Start();
    }

    private async Task RunExplorerFilterAsync()
    {
        var query = _filterText;
        if (ExplorerFilterText.NormalizeQuery(query).Length == 0)
        {
            ClearExplorerFilter();
            return;
        }

        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        var sw = Stopwatch.StartNew();
        if (!_filterActive)
        {
            _savedExpansion = new Dictionary<ExplorerNode, bool>(ReferenceEqualityComparer.Instance);
            foreach (var (n, _) in Walk())
                _savedExpansion[n] = n.IsExpanded;
            _filterActive = true;
            OnPropertyChanged(nameof(IsExplorerFilterActive));
        }

        var nodes = new List<ExplorerNode>(1024);
        var owners = new List<ObservableCollection<ExplorerNode>>(1024);
        var entries = new List<ExplorerFilterEntry>(1024);
        SnapshotTree(nodes, owners, entries);
        var snapshotMs = sw.Elapsed.TotalMilliseconds;

        ExplorerFilterResult? result;
        try
        {
            result = await Task.Run(() => new ExplorerFilterIndex(entries).Match(query, cts.Token), cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || !ReferenceEquals(_filterCts, cts) || result is null)
            return;

        sw.Restart();
        ApplyFilterResult(nodes, owners, result);
        LastFilterUiMs = snapshotMs + sw.Elapsed.TotalMilliseconds;
        Debug.WriteLine($"[ExplorerFilter] '{query}': {nodes.Count} nodes, {result.MatchCount} matches, UI {LastFilterUiMs:F1} ms");

        StartFilterPreload();
    }

    /// <summary>Pre-order snapshot of the whole tree (collapsed branches included).</summary>
    private void SnapshotTree(List<ExplorerNode> nodes, List<ObservableCollection<ExplorerNode>> owners, List<ExplorerFilterEntry> entries)
    {
        void Visit(ObservableCollection<ExplorerNode> coll, int parent)
        {
            foreach (var n in coll)
            {
                var index = nodes.Count;
                nodes.Add(n);
                owners.Add(coll);
                var role = n.NodeKind switch
                {
                    ExplorerNodeKind.Placeholder => ExplorerFilterRole.Placeholder,
                    ExplorerNodeKind.Object => ExplorerFilterRole.Object,
                    _ => ExplorerFilterRole.Container
                };
                var name = n.Name ?? "";
                var nameStart = role == ExplorerFilterRole.Object && n.SchemaName is { Length: > 0 } sch
                                && name.Length > sch.Length && name[sch.Length] == '.'
                                && name.StartsWith(sch, StringComparison.Ordinal)
                    ? sch.Length + 1
                    : 0;
                entries.Add(new ExplorerFilterEntry(parent, name, nameStart, role));
                if (n.Children.Count > 0)
                    Visit(n.Children, index);
            }
        }
        Visit(ExplorerRoots, -1);
    }

    private IEnumerable<(ExplorerNode node, ObservableCollection<ExplorerNode> owner)> Walk()
    {
        var stack = new Stack<ObservableCollection<ExplorerNode>>();
        stack.Push(ExplorerRoots);
        while (stack.Count > 0)
        {
            var coll = stack.Pop();
            foreach (var n in coll)
            {
                yield return (n, coll);
                if (n.Children.Count > 0) stack.Push(n.Children);
            }
        }
    }

    /// <summary>One batch: flags, highlights, folder counts, auto-expansion, then one refresh per changed collection.</summary>
    private void ApplyFilterResult(List<ExplorerNode> nodes, List<ObservableCollection<ExplorerNode>> owners, ExplorerFilterResult r)
    {
        var changed = new HashSet<ObservableCollection<ExplorerNode>>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var visible = r.IsVisible[i];
            if (n.FilterVisible != visible)
            {
                n.FilterVisible = visible;
                changed.Add(owners[i]);
            }
            n.SetHighlight(r.MatchStart[i], r.MatchLength);
            if (n.NodeKind == ExplorerNodeKind.Category)
                n.FilteredCount = visible && !r.IsCovered[i] ? r.VisibleChildren[i] : null;

            if (r.IsExpanded[i])
            {
                if (!n.IsExpanded)
                {
                    _filterExpanded.Add(n);
                    n.IsExpanded = true;
                }
            }
            else if (_filterExpanded.Remove(n))
            {
                n.IsExpanded = _savedExpansion is not null && _savedExpansion.TryGetValue(n, out var was) && was;
            }
        }

        foreach (var coll in changed)
            RefreshFilteredView(coll);
    }

    private void RefreshFilteredView(ObservableCollection<ExplorerNode> coll)
    {
        var view = CollectionViewSource.GetDefaultView(coll);
        if (view is null) return;
        if (ReferenceEquals(coll, ExplorerRoots))
        {
            view.Refresh(); // root view always filters through FilterRoot (Hide Disconnected Databases)
            return;
        }
        if (view.Filter is null)
            view.Filter = FilterChild; // setting the predicate refreshes once
        else
            view.Refresh();
    }

    /// <summary>CollectionView predicate of every child collection: hides nodes the active filter dropped.</summary>
    private bool FilterChild(object item) => !_filterActive || item is not ExplorerNode n || n.FilterVisible;

    private void ClearExplorerFilter()
    {
        _filterCts?.Cancel();
        _filterCts = null;
        if (!_filterActive) return;
        _filterActive = false;

        var changed = new HashSet<ObservableCollection<ExplorerNode>>(ReferenceEqualityComparer.Instance);
        foreach (var (n, owner) in Walk())
        {
            if (!n.FilterVisible)
            {
                n.FilterVisible = true;
                changed.Add(owner);
            }
            n.SetHighlight(-1, 0);
            n.FilteredCount = null;
        }

        _restoringFilterExpansion = true;
        try
        {
            foreach (var n in _filterExpanded)
                n.IsExpanded = _savedExpansion is not null && _savedExpansion.TryGetValue(n, out var was) && was;
            if (_savedExpansion is not null)
                foreach (var (n, was) in _savedExpansion)
                    n.IsExpanded = was;
            foreach (var coll in changed)
                RefreshFilteredView(coll);
        }
        finally
        {
            // Containers realized by the refresh raise Expanded during the next layout pass.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _restoringFilterExpansion = false);
            if (System.Windows.Application.Current is null) _restoringFilterExpansion = false;
        }
        _filterExpanded.Clear();
        _savedExpansion = null;
        OnPropertyChanged(nameof(IsExplorerFilterActive));
    }

    /// <summary>
    /// Once per database of the connection whose folders are loaded: fetch the object lists that were
    /// never expanded, sequentially, in the background (cached in the nodes — never per keystroke).
    /// </summary>
    private void StartFilterPreload()
    {
        if (_filterPreloadRunning || !IsConnected || _activeProfileId is null) return;
        var profileNode = ExplorerRoots.FirstOrDefault(n => n.ProfileId == _activeProfileId && n.IsActive);
        if (profileNode is null) return;

        _filterPreloaded.RemoveWhere(d => !profileNode.Children.Contains(d));
        var pending = new List<ExplorerNode>();
        foreach (var db in profileNode.Children)
        {
            if (db.NodeKind != ExplorerNodeKind.Database || db.LoadTask is not { IsCompletedSuccessfully: true }) continue;
            if (!_filterPreloaded.Add(db)) continue;
            pending.AddRange(db.Children.Where(c => c.NodeKind == ExplorerNodeKind.Category && c.HasDummyChild && c.LoadTask is null));
        }
        if (pending.Count == 0) return;

        _filterPreloadRunning = true;
        RunSafe(async () =>
        {
            var profileId = _activeProfileId;
            var before = StatusText;
            var status = $"Filtro: carregando a lista de objetos ({pending.Count} pastas)...";
            StatusText = status;
            var failed = 0;
            try
            {
                foreach (var cat in pending)
                {
                    if (!IsConnected || _activeProfileId != profileId) break;
                    try { await LoadCategoryChildrenAsync(cat).ConfigureAwait(true); }
                    catch { failed++; } // the folder keeps its placeholder; expanding it retries
                }
            }
            finally
            {
                _filterPreloadRunning = false;
            }
            if (StatusText == status)
                StatusText = failed == 0 ? before : $"Filtro: {failed} pasta(s) não puderam ser carregadas.";
            OnExplorerTreeChanged();
        });
    }
}
