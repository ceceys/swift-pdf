using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwiftPdf.Core;

namespace SwiftPdf;

public partial class MainWindow : Window
{
    private sealed record ResultItem(string Title, string Detail, string Path);

    private readonly PdfRenderer _renderer;
    private List<ResultItem> _results = new();
    private bool _busy;

    public MainWindow(string? startFile = null)
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
            if (startFile != null) await RunAsync(startFile);
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
        if (!TryGetPdf(e, out var path))
        {
            ShowError("Yalnızca PDF dosyası bırakabilirsiniz. Maili önce Outlook'ta \"Adobe PDF'e dönüştür\" ile PDF yapın.");
            return;
        }
        await RunAsync(path!);
    }

    private static bool TryGetPdf(DragEventArgs e, out string? path)
    {
        path = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        path = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        return path != null;
    }

    // ---------- Düğmeler ----------

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Adobe PDF dosyasını seçin",
            Filter = "PDF dosyası (*.pdf)|*.pdf",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true) await RunAsync(dlg.FileName);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        foreach (var r in _results) Start(r.Path);
    }

    private void OnFolder(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_results[0].Path}\"") { UseShellExecute = true });
    }

    private void OnReset(object sender, RoutedEventArgs e) => Show(IdlePanel);

    private static void Start(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // ---------- Akış ----------

    private async Task RunAsync(string input)
    {
        _busy = true;
        Show(BusyPanel);
        try
        {
            var results = await Converter.ConvertAsync(input, _renderer);
            _results = results.Select(r => new ResultItem(
                Path.GetFileName(r.OutputPath),
                Detail(r.Message) + "\nKaydedildi: " + Path.GetDirectoryName(r.OutputPath),
                r.OutputPath)).ToList();
            ResultTitle.Text = results.Count == 1 ? "SWIFT PDF hazır" : $"{results.Count} SWIFT PDF hazır";
            ResultList.ItemsSource = _results;
            Show(ResultPanel);
        }
        catch (SwiftNotFoundException ex) { ShowError(ex.Message); }
        catch (UnauthorizedAccessException)
        {
            ShowError("PDF'in bulunduğu klasöre yazılamadı. Dosyayı Masaüstü ya da Belgeler gibi bir klasöre taşıyıp yeniden deneyin.");
        }
        catch (Exception ex) { ShowError("Beklenmeyen bir hata oluştu: " + ex.Message); }
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
    }
}
