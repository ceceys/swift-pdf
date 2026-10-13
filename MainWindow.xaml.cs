using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwiftPdf.Core;

namespace SwiftPdf;

public partial class MainWindow : Window
{
    private sealed record ResultItem(string Title, string Detail, string Path, bool Ok, string Warning = "");

    private readonly PdfRenderer _renderer;
    private List<ResultItem> _results = new();
    private bool _busy;

    public MainWindow(IReadOnlyList<string>? startFiles = null)
    {
        InitializeComponent();
        _renderer = new PdfRenderer(this);
        Closed += (_, _) => _renderer.Dispose();
        Loaded += async (_, _) =>
        {
            if (PdfRenderer.RuntimeVersion() == null)
            {
                ShowError("Bu bilgisayarda Microsoft Edge WebView2 bileşeni yok. Windows Update'i çalıştırın ya da " +
                          "\"WebView2 Runtime\" kurulumunu yapın, sonra programı yeniden açın.");
                return;
            }
            if (startFiles is { Count: > 0 }) await RunAsync(startFiles);
        };
    }

    // ---------- Sürükle bırak ----------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        bool ok = !_busy && TryGetPdf(e, out _);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.BorderBrush = ok ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xDF));
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xDF));

    private async void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        if (_busy) return;
        if (!TryGetPdf(e, out var paths))
        {
            ShowError("Yalnızca PDF dosyası bırakabilirsiniz. Maili önce Outlook'ta \"Adobe PDF'e dönüştür\" ile PDF yapın.");
            return;
        }
        await RunAsync(paths);
    }

    private static bool TryGetPdf(DragEventArgs e, out List<string> paths)
    {
        paths = new();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        paths = files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        return paths.Count > 0;
    }

    // ---------- Düğmeler ----------

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Adobe PDF dosyasını seçin",
            Filter = "PDF dosyası (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) await RunAsync(dlg.FileNames);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        foreach (var r in _results.Where(r => r.Ok)) Start(r.Path);
    }

    private void OnFolder(object sender, RoutedEventArgs e)
    {
        var first = _results.FirstOrDefault(r => r.Ok);
        if (first == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first.Path}\"") { UseShellExecute = true });
    }

    private void OnReset(object sender, RoutedEventArgs e) => Show(IdlePanel);

    private static void Start(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // ---------- Akış ----------

    /// <summary>Bir ya da birkaç PDF'i sırayla işler; olmayanlar listede ayrıca gösterilir.</summary>
    private async Task RunAsync(IReadOnlyList<string> inputs)
    {
        _busy = true;
        Show(BusyPanel);
        var items = new List<ResultItem>();
        var errors = new List<(string File, string Message)>();
        try
        {
            foreach (var input in inputs)
            {
                try
                {
                    foreach (var r in await Converter.ConvertAsync(input, _renderer))
                    {
                        var where = "Kaydedildi: " + Path.GetDirectoryName(r.OutputPath)
                                    + (r.SavedToFallback ? "  (girdinin klasörüne yazılamadığı için)" : "");
                        items.Add(new ResultItem(Path.GetFileName(r.OutputPath), Detail(r.Message) + "\n" + where,
                            r.OutputPath, true, string.Join("\n", r.Message.Warnings.Select(w => "Dikkat: " + w))));
                    }
                }
                catch (UserFacingException ex) { errors.Add((Path.GetFileName(input), ex.Message)); }
                catch (Exception ex) { errors.Add((Path.GetFileName(input), "Beklenmeyen bir hata oluştu: " + ex.Message)); }
            }

            if (items.Count == 0)
            {
                ShowError(errors.Count == 1
                    ? errors[0].Message
                    : string.Join("\n\n", errors.Select(x => x.File + ": " + x.Message)));
                return;
            }
            _results = items;
            // Dikkat isteyenler üstte: uyarılı çıktılar, sonra olmayan dosyalar, sonra sorunsuzlar.
            var failed = errors.Select(x => new ResultItem("Olmadı: " + x.File, x.Message, "", false));
            var shown = items.Where(i => i.Warning.Length > 0).Concat(failed)
                .Concat(items.Where(i => i.Warning.Length == 0)).ToList();
            int warned = items.Count(i => i.Warning.Length > 0);
            ResultTitle.Text = (items.Count == 1 ? "SWIFT PDF hazır" : $"{items.Count} SWIFT PDF hazır")
                               + (warned > 0 ? $"  ·  {warned} uyarı" : "")
                               + (errors.Count > 0 ? $"  ·  {errors.Count} dosya olmadı" : "");
            ResultList.ItemsSource = shown;
            Show(ResultPanel);
        }
        finally { _busy = false; }
    }

    private static string Detail(SwiftMessage m)
    {
        var parts = new List<string>();
        if (m.SettlementDate.Length > 0) parts.Add(m.SettlementDate);
        if (m.Amount.Length > 0) parts.Add($"{m.Amount} {m.Currency}");
        if (m.Creditor.Length > 0) parts.Add(m.Creditor);
        if (m.Reference.Length > 0) parts.Add("Ref. " + m.Reference);
        return string.Join("  ·  ", parts);
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        Show(ErrorPanel);
    }

    private void Show(UIElement panel)
    {
        foreach (var p in new UIElement[] { IdlePanel, BusyPanel, ResultPanel, ErrorPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        // Sonuç listesi uzunsa (uyarı, olmayan dosya) yardım kutusu yer kaplamasın.
        HelpPanel.Visibility = panel == ResultPanel ? Visibility.Collapsed : Visibility.Visible;
    }
}
