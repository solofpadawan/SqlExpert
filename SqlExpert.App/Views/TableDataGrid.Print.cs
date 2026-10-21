using System.Data;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Windows.Xps.Packaging;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Printing;

namespace SqlExpert.App.Views;

/// <summary>Print Data: lazy print preview of the fetched rows, Save (PDF/XPS) and Print.</summary>
public partial class TableDataGrid
{
    private bool _printStale = true;
    private bool _printRebuildQueued;
    private int _printRun;
    private CancellationTokenSource? _printCts;
    private PrintTable? _printTable;
    private PrintLayout? _printLayout;
    private bool _printBusy;

    private void MarkPrintStale()
    {
        _printStale = true;
        if (_mode != DataViewMode.Print || !IsVisible || _printRebuildQueued) return;
        _printRebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _printRebuildQueued = false;
            if (_mode == DataViewMode.Print && IsVisible) EnsurePrintPreview();
        }, DispatcherPriority.Background);
    }

    private void CancelPrintWork() => _printCts?.Cancel();

    private void EnsurePrintPreview()
    {
        if (!_printStale && Viewer.Document is not null) return;
        if (_printBusy) CancelPrintWork();
        _ = BuildPreviewAsync();
    }

    private PrintSettings CurrentPrintSettings(double? pageWidth = null, double? pageHeight = null, bool? landscape = null) => new()
    {
        PageWidth = pageWidth ?? PrintSettings.A4Width,
        PageHeight = pageHeight ?? PrintSettings.A4Height,
        Landscape = landscape ?? PrintLandscapeBox.IsChecked == true,
        Title = PrintTitleBox.Text ?? ""
    };

    /// <summary>Formats the fetched rows off the UI thread, then builds the preview pages. Returns false when cancelled/failed.</summary>
    private async Task<bool> BuildPreviewAsync()
    {
        if (_vm is null) return false;
        _printCts?.Cancel();
        var cts = new CancellationTokenSource();
        _printCts = cts;
        var ct = cts.Token;
        var run = ++_printRun;
        CommitEdit();   // before clearing the stale flag: a committed edit marks the preview stale again
        _printStale = false;
        _printBusy = true;

        try
        {
            var columns = _vm.Columns;
            if (_vm.View is null || columns.Count == 0)
            {
                Viewer.Document = null;
                _printTable = null;
                _printLayout = null;
                PrintInfoText.Text = _vm.IsBusy ? "carregando..." : "Sem dados para imprimir.";
                return false;
            }

            // Snapshot on the UI thread (grid order, current values); formatting runs in the background.
            var rows = new List<object?[]>(_vm.View.Count);
            foreach (DataRowView rv in _vm.View)
                rows.Add(rv.Row.ItemArray);
            var headers = columns.Select(c => c.Header).ToArray();
            var right = columns.Select(c => FormFieldLayout.IsNumericType(c.DataType)).ToArray();
            var memo = columns.Select(c => FormFieldLayout.IsMemoColumn(c.Info)).ToArray();
            var printBlobs = PrintBlobsBox.IsChecked == true;
            var settings = CurrentPrintSettings();
            var culture = CultureInfo.CurrentCulture;
            var footer = $"{_vm.TableTitle} · {rows.Count.ToString("N0", culture)} registro(s) · {DateTime.Now.ToString("g", culture)}";

            ShowPrintProgress($"Formatando registros: 0 de {rows.Count.ToString("N0", culture)}", 0);
            var progress = new Progress<int>(done =>
            {
                if (run != _printRun) return;
                ShowPrintProgress($"Formatando registros: {done.ToString("N0", culture)} de {rows.Count.ToString("N0", culture)}",
                    rows.Count == 0 ? 0 : done * 80.0 / rows.Count);
            });

            var (table, layout) = await Task.Run(() =>
            {
                var t = PrintTable.Build(headers, right, memo, rows, printBlobs, culture, footer, progress, ct);
                var l = PrintLayout.Create(t, settings, ct);
                return (t, l);
            }, ct).ConfigureAwait(true);
            if (run != _printRun) return false;

            var doc = new FixedDocument();
            var size = PrintRenderer.PageSizeDip(layout);
            doc.DocumentPaginator.PageSize = size;
            for (var i = 0; i < layout.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = new FixedPage { Width = size.Width, Height = size.Height, Background = System.Windows.Media.Brushes.White };
                page.Children.Add(new PrintPageElement(layout, i));
                var content = new PageContent();
                ((IAddChild)content).AddChild(page);
                doc.Pages.Add(content);
                if (i % 100 == 99)
                {
                    ShowPrintProgress($"Preparando página {(i + 1).ToString("N0", culture)} de {layout.PageCount.ToString("N0", culture)}",
                        80 + (i + 1) * 20.0 / layout.PageCount);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    if (run != _printRun) return false;
                }
            }

            _printTable = table;
            _printLayout = layout;
            Viewer.Document = doc;
            ApplyScale();
            var bands = layout.Bands.Count > 1 ? $" · colunas em {layout.Bands.Count} faixas" : "";
            PrintInfoText.Text = $"{layout.PageCount.ToString("N0", culture)} página(s) · {rows.Count.ToString("N0", culture)} registro(s) lido(s){bands}";
            return true;
        }
        catch (OperationCanceledException)
        {
            if (run == _printRun)
            {
                _printStale = true;
                Viewer.Document = null;
                _printLayout = null;
                PrintInfoText.Text = "Pré-visualização cancelada — ↻ para gerar novamente.";
            }
            return false;
        }
        catch (Exception ex)
        {
            if (run == _printRun)
            {
                _printStale = true;
                PrintInfoText.Text = "Erro ao gerar a pré-visualização: " + ex.Message;
                App.ReportError(ex);
            }
            return false;
        }
        finally
        {
            if (run == _printRun)
            {
                _printBusy = false;
                HidePrintProgress();
            }
        }
    }

    private void ShowPrintProgress(string text, double percent)
    {
        PrintProgressText.Text = text;
        PrintProgressBar.Value = Math.Clamp(percent, 0, 100);
        PrintProgress.Visibility = Visibility.Visible;
        PrintSaveButton.IsEnabled = false;
        PrintPrintButton.IsEnabled = false;
    }

    private void HidePrintProgress()
    {
        PrintProgress.Visibility = Visibility.Collapsed;
        PrintSaveButton.IsEnabled = true;
        PrintPrintButton.IsEnabled = true;
    }

    private void PrintCancel_OnClick(object sender, RoutedEventArgs e) => CancelPrintWork();

    private void PrintRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        _printStale = true;
        EnsurePrintPreview();
    }

    private void PrintOption_OnClick(object sender, RoutedEventArgs e) => MarkPrintStale();

    private string _appliedTitle = "";

    private void ApplyTitle()
    {
        var t = PrintTitleBox.Text ?? "";
        if (t == _appliedTitle) return;
        _appliedTitle = t;
        MarkPrintStale();
    }

    private void PrintTitleBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyTitle();
        e.Handled = true;
    }

    private void PrintTitleBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyTitle();

    private void ScaleBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        ApplyScale();
    }

    private void ApplyScale()
    {
        if (Viewer is null || ScaleBox is null || Viewer.Document is null) return;
        var index = ScaleBox.SelectedIndex;
        Dispatcher.BeginInvoke(() =>
        {
            if (Viewer.Document is null) return;
            switch (index)
            {
                case 6: Viewer.FitToWidth(); break;
                case 7: Viewer.FitToHeight(); break;
                default:
                    Viewer.Zoom = index switch { 0 => 50, 1 => 75, 3 => 125, 4 => 150, 5 => 200, _ => 100 };
                    break;
            }
        }, DispatcherPriority.ContextIdle);
    }

    private async Task<PrintLayout?> CurrentLayoutAsync()
    {
        if (_printStale || _printLayout is null)
        {
            if (!await BuildPreviewAsync().ConfigureAwait(true)) return null;
        }
        return _printLayout;
    }

    private string SuggestedFileName()
    {
        var name = _vm?.Schema is { } s ? $"{s.Schema}.{s.Table}" : "dados";
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }

    private async void PrintSave_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var layout = await CurrentLayoutAsync().ConfigureAwait(true);
            if (layout is null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Salvar dados para impressão",
                Filter = "PDF (*.pdf)|*.pdf|XPS (*.xps)|*.xps",
                FileName = SuggestedFileName(),
                AddExtension = true,
                OverwritePrompt = true
            };
            var owner = Window.GetWindow(this);
            if ((owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner)) != true) return;
            var path = dlg.FileName;
            var xps = dlg.FilterIndex == 2 || path.EndsWith(".xps", StringComparison.OrdinalIgnoreCase);
            var tmp = path + ".tmp-" + Environment.ProcessId;
            var title = string.IsNullOrWhiteSpace(layout.Settings.Title) ? _vm?.TableTitle ?? "SqlExpert" : layout.Settings.Title;

            if (xps)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    using (var doc = new XpsDocument(tmp, FileAccess.ReadWrite))
                    {
                        var writer = XpsDocument.CreateXpsDocumentWriter(doc);
                        writer.Write(new TablePaginator(layout));
                    }
                    File.Move(tmp, path, overwrite: true);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    TryDelete(tmp);
                }
            }
            else
            {
                _printCts?.Cancel();
                var cts = new CancellationTokenSource();
                _printCts = cts;
                var run = ++_printRun;
                _printBusy = true;
                var total = layout.PageCount;
                ShowPrintProgress($"Gravando PDF: 0 de {total:N0} página(s)", 0);
                var progress = new Progress<int>(done =>
                {
                    if (run == _printRun) ShowPrintProgress($"Gravando PDF: {done:N0} de {total:N0} página(s)", done * 100.0 / Math.Max(1, total));
                });
                try
                {
                    await Task.Run(() =>
                    {
                        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                            PdfTableWriter.Write(fs, layout, title, DateTime.Now, progress, cts.Token);
                        File.Move(tmp, path, overwrite: true);
                    }, cts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    PrintInfoText.Text = "Gravação cancelada.";
                    return;
                }
                finally
                {
                    TryDelete(tmp);
                    if (run == _printRun)
                    {
                        _printBusy = false;
                        HidePrintProgress();
                    }
                }
            }

            PrintInfoText.Text = "Salvo: " + path;
        }
        catch (Exception ex)
        {
            PrintInfoText.Text = "Erro ao salvar: " + ex.Message;
            App.ReportError(ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async void PrintPrint_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = await CurrentLayoutAsync().ConfigureAwait(true);
            if (preview is null || _printTable is null) return;

            var dlg = new PrintDialog { UserPageRangeEnabled = false };
            try
            {
                dlg.PrintTicket.PageOrientation = PrintLandscapeBox.IsChecked == true ? PageOrientation.Landscape : PageOrientation.Portrait;
            }
            catch (Exception) { /* printer without a ticket: keep its default */ }
            if (dlg.ShowDialog() != true) return;

            // Re-paginate for the chosen paper (sizes from the dialog are DIPs; the layout works in points).
            var w = dlg.PrintableAreaWidth / PrintRenderer.DipPerPoint;
            var h = dlg.PrintableAreaHeight / PrintRenderer.DipPerPoint;
            var layout = w > 50 && h > 50
                ? PrintLayout.Create(_printTable, CurrentPrintSettings(w, h, landscape: w > h))
                : preview;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                dlg.PrintDocument(new TablePaginator(layout), "SqlExpert - " + (_vm?.TableTitle ?? "Data"));
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            PrintInfoText.Text = $"Enviado para a impressora: {layout.PageCount:N0} página(s).";
        }
        catch (Exception ex)
        {
            PrintInfoText.Text = "Erro ao imprimir: " + ex.Message;
            App.ReportError(ex);
        }
    }
}
