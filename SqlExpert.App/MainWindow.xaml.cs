using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.App.Services;
using SqlExpert.App.ViewModels;
using SqlExpert.App.Views;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App;

public partial class MainWindow : Window
{
    private GridLength _explorerWidth = new(280);
    private MainViewModel? _vm;

    private const string TabDragDataFormat = "SqlExpert.DocumentTab";

    private DocumentViewModel? _tabDragDoc;
    private Button? _tabDragButton;
    private System.Windows.Input.ICommand? _tabDragSavedCommand;
    private Point _tabDragStart;
    private Point _tabDragLastPos;
    private bool _tabDragging;
    private bool _tabDragResetting;
    private bool _tabDragInDoDragDrop;

    /// <summary>Off-screen verification only: skip placement restore (which would Maximize onto the user's screen).</summary>
    internal static bool SuppressVisibleDisplay;

    internal static void SetSuppressVisibleDisplay(bool value) => SuppressVisibleDisplay = value;

    /// <summary>Used by the off-screen verification console: RaiseEvent cannot fake the device position or button state.</summary>
    internal Point? TabDragPositionOverride;
    internal MouseButtonState? TabDragLeftButtonOverride;

    internal void SetTabDragTestOverrides(Point? position, MouseButtonState? leftButton)
    {
        TabDragPositionOverride = position;
        TabDragLeftButtonOverride = leftButton;
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ApplyStartupPlacement();
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _lastShownState = WindowState; };
        Closing += OnClosing;
        if (Application.Current is { } app)
        {
            app.SessionEnding += OnSessionEnding;
            Closed += (_, _) => app.SessionEnding -= OnSessionEnding;
        }
    }

    private bool _closeConfirmed;
    private bool _closeInProgress;
    private bool _sessionEnding;

    /// <summary>
    /// Before closing (window X, File | Exit, Alt+F4): "Are you sure you wish to quit SqlExpert?" (Options | Confirm on exit;
    /// not asked when closing to install a downloaded update), then unsaved SQL Editor text, open transactions, running
    /// queries and pending Data edits are confirmed document by document (Cancel/No keeps the app open). Then the window
    /// placement and the open windows of each registration are saved.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_closeConfirmed && _vm is not null)
        {
            if (_closeInProgress)
            {
                e.Cancel = true;
                return;
            }
            var ask = UiSettings.Instance.ConfirmOnExit && UpdateService.Pending is null && !_sessionEnding;
            var vm = _vm;
            if (ask || vm.AnyDocumentNeedsConfirmation)
            {
                e.Cancel = true;
                _closeInProgress = true;
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        if (ask && !QuitConfirmationWindow.Ask(this)) return;
                        if (vm.AnyDocumentNeedsConfirmation && !await vm.CloseAllForExitAsync().ConfigureAwait(true)) return;
                        _closeConfirmed = true;
                        Close();
                    }
                    catch (Exception ex)
                    {
                        App.ReportError(ex);
                    }
                    finally
                    {
                        _closeInProgress = false;
                    }
                });
                return;
            }
        }
        SavePlacement();
        if (_vm is not null)
        {
            _vm.SaveOpenTabsForExit();
            // Release editor sessions (connections) of documents that needed no confirmation.
            foreach (var d in _vm.Documents.ToList()) d.OnClosed();
        }
    }

    /// <summary>Windows log-off / shutdown does not raise Closing: keep the open windows of each registration anyway.</summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _sessionEnding = true;
        try { _vm?.SaveOpenTabsForExit(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(ex); } // no dialogs while Windows is ending
    }

    // ===== Window placement (normal on first run; last bounds/state restored afterwards) =====

    private WindowState _lastShownState = WindowState.Normal;

    private static ScreenRect WorkArea()
    {
        var wa = SystemParameters.WorkArea;
        return new ScreenRect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    private static ScreenRect Desktop() => new(
        SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    private void ApplyStartupPlacement()
    {
        if (SuppressVisibleDisplay)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Normal;
            Left = -20000;
            Top = -20000;
            ShowInTaskbar = false;
            ShowActivated = false;
            Visibility = Visibility.Hidden;
            return;
        }
        var restored = WindowPlacementLogic.Restore(UiSettings.Instance.MainWindowPlacement, Desktop());
        if (restored is null)
        {
            var d = WindowPlacementLogic.Default(WorkArea());
            Width = d.Width;
            Height = d.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Normal;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = restored.Left;
        Top = restored.Top;
        Width = restored.Width;
        Height = restored.Height;
        // Never restore minimized. Open with the saved normal bounds; maximize once the HWND exists
        // (setting Maximized in the constructor is unreliable), on the monitor holding those bounds.
        WindowState = WindowState.Normal;
        if (restored.IsMaximized)
        {
            _lastShownState = WindowState.Maximized;
            SourceInitialized += (_, _) => WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        if (SuppressVisibleDisplay) return;
        try
        {
            var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (normal.IsEmpty || double.IsInfinity(normal.Width)) return;
            var maximized = WindowState == WindowState.Maximized
                || (WindowState == WindowState.Minimized && _lastShownState == WindowState.Maximized);
            UiSettings.Instance.SaveMainWindowPlacement(new WindowPlacement(normal.Left, normal.Top, normal.Width, normal.Height, maximized));
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyExplorerVisibility();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowExplorer))
            ApplyExplorerVisibility();
    }

    /// <summary>Collapse the explorer column when the panel is closed, restore its width when shown.</summary>
    private void ApplyExplorerVisibility()
    {
        if (_vm is null) return;
        if (_vm.ShowExplorer)
        {
            ExplorerColumn.MinWidth = 160;
            ExplorerColumn.Width = _explorerWidth.Value > 0 ? _explorerWidth : new GridLength(280);
            SplitterColumn.Width = new GridLength(4);
            ExplorerSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (ExplorerColumn.ActualWidth > 0)
                _explorerWidth = new GridLength(ExplorerColumn.ActualWidth);
            ExplorerColumn.MinWidth = 0;
            ExplorerColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            ExplorerSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private static TreeViewItem? FindTreeViewItem(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null && d is not TreeViewItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }

    private void Explorer_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void Explorer_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.OnExplorerSelectionChangedAsync(e.NewValue as ExplorerNode);
    }

    private void Explorer_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (FindTreeViewItem(e.OriginalSource) is { DataContext: ExplorerNode node })
            _ = vm.OnExplorerDoubleClickAsync(node);
    }

    /// <summary>Builds the popup for the node under the mouse (registered database vs. objects).</summary>
    private void Explorer_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PrepareExplorerContextMenu();
    }

    /// <summary>Goto Database / programmatic selection: keep the selected node visible.</summary>
    private void ExplorerItem_OnSelected(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is TreeViewItem item)
            item.BringIntoView();
    }

    private void Explorer_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && DataContext is MainViewModel refreshVm)
        {
            // On the Procedures folder / a procedure only that folder is reloaded.
            if (!refreshVm.RefreshProceduresShortcut())
                refreshVm.RefreshExplorerShortcut();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control && DataContext is MainViewModel newVm)
        {
            if (newVm.NewProcedureShortcut()) e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm) return;
        if (ExplorerTree.SelectedItem is ExplorerNode node)
        {
            _ = vm.OnExplorerDoubleClickAsync(node);
            e.Handled = true;
        }
    }

    // ===== Documents: Windows bar, Windows menu, shortcuts =====

    private void WindowsBarTab_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (sender is FrameworkElement { DataContext: DocumentViewModel doc } && DataContext is MainViewModel vm)
        {
            if (vm.CloseDocumentCommand.CanExecute(doc))
                vm.CloseDocumentCommand.Execute(doc);
            e.Handled = true;
        }
    }

    private Point PositionOnBar(MouseEventArgs e) => TabDragPositionOverride ?? e.GetPosition(WindowsBarHost);

    private MouseButtonState LeftButtonOf(MouseEventArgs e) => TabDragLeftButtonOverride ?? e.LeftButton;

    private void WindowsBar_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || e.ChangedButton != MouseButton.Left) return;
        if (DataContext is not MainViewModel vm || vm.Documents.Count < 2) return;
        if (!TryGetWindowsBarTab(e.OriginalSource, vm, out var button, out var doc) && !TryGetWindowsBarTab(e.Source, vm, out button, out doc)) return;
        _tabDragDoc = doc;
        _tabDragButton = button;
        _tabDragStart = PositionOnBar(e);
        _tabDragLastPos = _tabDragStart;
        _tabDragging = false;
    }

    private void WindowsBar_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Once DoDragDrop is running it uses DragOver/Drop; do not also capture the mouse.
        if (_tabDragDoc is null || _tabDragging || _tabDragInDoDragDrop) return;
        if (LeftButtonOf(e) != MouseButtonState.Pressed) return;

        var pos = PositionOnBar(e);
        if (Math.Abs(pos.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        e.Handled = true;
        StartDoDragDrop(pos);
    }

    private void WindowsBar_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A plain click (no drag started): just clear the candidate. A completed DoDragDrop already reset.
        if (_tabDragging || _tabDragInDoDragDrop) return;
        if (_tabDragDoc is not null) ResetTabDrag();
    }

    /// <summary>
    /// Starts a WPF drag-and-drop operation. This is the reliable path with tab Buttons:
    /// ButtonBase captures the mouse on click, so a manual CaptureMouse/MouseUp on the bar
    /// often never sees the release. DoDragDrop takes over the pointer until Drop or cancel.
    /// </summary>
    private void StartDoDragDrop(Point pos)
    {
        if (_tabDragDoc is null) return;

        _tabDragging = true;
        _tabDragInDoDragDrop = true;
        _tabDragLastPos = pos;
        if (_tabDragButton is not null)
        {
            _tabDragSavedCommand = _tabDragButton.Command;
            _tabDragButton.Command = null; // suppress Click/Activate after the drop
            _tabDragButton.Opacity = 0.55;
        }

        try
        {
            var data = new DataObject(TabDragDataFormat, _tabDragDoc);
            DragDrop.DoDragDrop(WindowsBar, data, DragDropEffects.Move);
        }
        finally
        {
            _tabDragInDoDragDrop = false;
            ResetTabDrag();
        }
    }

    private void WindowsBar_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!IsTabDragData(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var pos = e.GetPosition(WindowsBarHost);
        _tabDragLastPos = pos;
        UpdateDropMarker(pos);
    }

    private void WindowsBar_OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (!IsTabDragData(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var pos = e.GetPosition(WindowsBarHost);
        CompleteTabDrop(pos);
    }

    private void WindowsBar_OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (!IsTabDragData(e)) return;
        var p = e.GetPosition(WindowsBar);
        if (p.X < -4 || p.Y < -4 || p.X > WindowsBar.ActualWidth + 4 || p.Y > WindowsBar.ActualHeight + 4)
        {
            WindowsBarDropMarker.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private void WindowsBar_OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (!_tabDragging) return;
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.SizeWE);
        e.Handled = true;
    }

    private void WindowsBar_OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            e.Action = DragAction.Cancel;
        }
    }

    private static bool IsTabDragData(DragEventArgs e) =>
        e.Data.GetDataPresent(TabDragDataFormat);

    private void CompleteTabDrop(Point pos)
    {
        if (DataContext is not MainViewModel vm || _tabDragDoc is null)
        {
            ResetTabDrag();
            return;
        }

        var bounds = GetTabBounds();
        var sourceIndex = vm.Documents.IndexOf(_tabDragDoc);
        var insert = bounds.Count == 0
            ? -1
            : TabReorder.InsertBeforeIndex(bounds, pos.X, pos.Y);
        var dest = insert < 0 ? null : TabReorder.MoveDestination(sourceIndex, insert, vm.Documents.Count);
        var doc = _tabDragDoc;
        ResetTabDrag();
        if (dest is int index)
            vm.ReorderDocument(doc, index);
    }

    private void CancelTabDrag() => ResetTabDrag();

    private void ResetTabDrag()
    {
        if (_tabDragResetting) return;
        _tabDragResetting = true;
        try
        {
            if (_tabDragButton is not null)
            {
                _tabDragButton.Opacity = 1;
                if (_tabDragSavedCommand is not null)
                    _tabDragButton.Command = _tabDragSavedCommand;
            }
            _tabDragSavedCommand = null;
            _tabDragButton = null;
            _tabDragDoc = null;
            _tabDragging = false;
            WindowsBarDropMarker.Visibility = Visibility.Collapsed;
            WindowsBar.Cursor = null;
            if (WindowsBar.IsMouseCaptured) WindowsBar.ReleaseMouseCapture();
        }
        finally
        {
            _tabDragResetting = false;
        }
    }

    private void UpdateDropMarker(Point pos)
    {
        var bounds = GetTabBounds();
        if (bounds.Count == 0)
        {
            WindowsBarDropMarker.Visibility = Visibility.Collapsed;
            return;
        }

        var insert = TabReorder.InsertBeforeIndex(bounds, pos.X, pos.Y);
        TabReorder.TabBounds slot;
        double x;
        if (insert >= bounds.Count)
        {
            slot = bounds[^1];
            x = slot.Right;
        }
        else
        {
            slot = bounds[insert];
            x = slot.X;
        }

        WindowsBarDropMarker.Height = Math.Max(2, slot.Height);
        WindowsBarDropMarker.Margin = new Thickness(Math.Max(0, x - 1), slot.Y, 0, 0);
        WindowsBarDropMarker.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<TabReorder.TabBounds> GetTabBounds()
    {
        var list = new List<TabReorder.TabBounds>(WindowsBarTabs.Items.Count);
        for (var i = 0; i < WindowsBarTabs.Items.Count; i++)
        {
            FrameworkElement? element = null;
            if (WindowsBarTabs.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
                element = FindDescendant<Button>(container) ?? container;
            if (element is null) continue;
            var size = element.RenderSize;
            if (size.Width <= 0 || size.Height <= 0) continue;
            var topLeft = element.TranslatePoint(new Point(0, 0), WindowsBarHost);
            list.Add(new TabReorder.TabBounds(topLeft.X, topLeft.Y, size.Width, size.Height));
        }
        return list;
    }

    private static bool TryGetWindowsBarTab(object? source, MainViewModel vm, out Button? button, out DocumentViewModel? doc)
    {
        button = FindAncestor<Button>(source as DependencyObject);
        doc = button?.DataContext as DocumentViewModel;
        if (doc is null || !vm.Documents.Contains(doc))
        {
            button = null;
            doc = null;
            return false;
        }
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T match) return match;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(d);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }


    private const string DocumentMenuTag = "doc-item";

    /// <summary>Appends one checkable item per open document (checked = active) to the Windows menu.</summary>
    private void WindowsMenu_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WindowsMenu) || DataContext is not MainViewModel vm) return;

        foreach (var old in WindowsMenu.Items.OfType<Control>().Where(i => Equals(i.Tag, DocumentMenuTag)).ToList())
            WindowsMenu.Items.Remove(old);

        if (vm.Documents.Count == 0) return;
        WindowsMenu.Items.Add(new Separator { Tag = DocumentMenuTag });
        var n = 1;
        foreach (var doc in vm.Documents)
        {
            WindowsMenu.Items.Add(new MenuItem
            {
                Tag = DocumentMenuTag,
                Header = (n <= 9 ? $"_{n} " : "") + doc.Title.Replace("_", "__"),
                ToolTip = doc.ToolTip,
                IsCheckable = false,
                IsChecked = doc.IsActive,
                Command = vm.ActivateDocumentCommand,
                CommandParameter = doc
            });
            n++;
        }
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab cycle documents, Ctrl+F4 closes the active one. Esc cancels a tab drag.</summary>
    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_tabDragging && e.Key == Key.Escape)
        {
            // During DoDragDrop, QueryContinueDrag handles Escape; resetting here would cut the drag short unsafely.
            if (!_tabDragInDoDragDrop)
                CancelTabDrag();
            e.Handled = true;
            return;
        }
        if (DataContext is not MainViewModel vm || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Tab && vm.Documents.Count > 1)
        {
            var cmd = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? vm.PreviousDocumentCommand : vm.NextDocumentCommand;
            if (cmd.CanExecute(null)) cmd.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F4 && vm.ActiveDocument is { } active)
        {
            if (vm.CloseDocumentCommand.CanExecute(active)) vm.CloseDocumentCommand.Execute(active);
            e.Handled = true;
        }
    }

    /// <summary>Double-click on a row of the Fields grid opens Edit field (classic-style).</summary>
    private void FieldRow_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not MainViewModel vm) return;
        if (sender is DataGridRow { Item: FieldRow row })
        {
            e.Handled = true;
            vm.EditFieldFromGrid(row);
        }
    }

    /// <summary>Explorer filter box: Esc clears it, Enter applies it without waiting for the debounce.</summary>
    private void FilterCombo_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || FilterCombo.IsDropDownOpen) return;
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(vm.FilterText))
        {
            vm.FilterText = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.ApplyExplorerFilterNow();
            e.Handled = true;
        }
    }

    private void ExplorerItem_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (sender is TreeViewItem { DataContext: ExplorerNode node })
        {
            node.IsExpanded = true;
            _ = vm.OnExplorerExpandedAsync(node);
        }

        e.Handled = false;
    }
}
