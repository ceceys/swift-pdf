using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace SwiftPdf.Core;

/// <summary>
/// HTML'i Windows'taki Edge (WebView2) motoruyla tek sayfa A4 PDF'e basar.
/// Görünmez bir denetleyici kullanır; Python sürümündeki Playwright çağrısının aynısı (Page.printToPDF).
/// </summary>
public sealed class PdfRenderer : IDisposable
{
    private readonly Window _owner;
    private CoreWebView2Controller? _controller;

    public PdfRenderer(Window owner) => _owner = owner;

    public static string? RuntimeVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { return null; }
    }

    private async Task<CoreWebView2> CoreAsync()
    {
        if (_controller != null) return _controller.CoreWebView2;
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwiftPdf", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, data);
        var hwnd = new WindowInteropHelper(_owner).EnsureHandle();
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.IsVisible = false;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 1130);
        return _controller.CoreWebView2;
    }

    public async Task RenderAsync(string html, string outPath)
    {
        var core = await CoreAsync();

        var loaded = new TaskCompletionSource<bool>();
        void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= OnDone;
            loaded.TrySetResult(e.IsSuccess);
        }
        core.NavigationCompleted += OnDone;
        core.NavigateToString(html);
        if (!await loaded.Task) throw new InvalidOperationException("Şablon yüklenemedi.");

        await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            """{"expression":"document.fonts.ready.then(() => true)","awaitPromise":true}""");

        var json = await core.CallDevToolsProtocolMethodAsync("Page.printToPDF",
            """{"paperWidth":8.27,"paperHeight":11.7,"marginTop":0,"marginBottom":0,"marginLeft":0,"marginRight":0,"printBackground":true,"displayHeaderFooter":false,"preferCSSPageSize":false,"scale":1}""");
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("PDF üretilemedi.");
        await File.WriteAllBytesAsync(outPath, Convert.FromBase64String(data));
    }

    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
    }
}
