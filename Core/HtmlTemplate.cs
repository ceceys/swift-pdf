using System.Globalization;
using System.Net;
using System.Text;

namespace SwiftPdf.Core;

/// <summary>
/// Tek sayfa A4: yalnız ham SWIFT metni. Bankanın mail metinleri (giriş cümlesi, mobil şube ve uyarı
/// paragrafları, başlık bandı) bilerek basılmaz. Punto, en uzun satır ve satır sayısı sayfaya sığacak
/// biçimde seçilir.
/// </summary>
public static class HtmlTemplate
{
    private const double PageW = 595.92, PageH = 842.88;   // Chromium A4 (8.27 x 11.7 in)
    private const double MarginX = 42.0, MarginY = 40.0;
    private const double CourierAdvance = 0.6015;          // Courier New karakter genişliği / punto
    private const double MaxFont = 10.0;

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string E(string s) => WebUtility.HtmlEncode(s);

    public static (double Font, double LineHeight) Fit(SwiftMessage msg)
    {
        int n = Math.Max(1, msg.Lines.Count);
        int longest = Math.Max(1, msg.Lines.Max(l => l.Text.Length + l.Value.Length));
        double w = PageW - 2 * MarginX, h = PageH - 2 * MarginY;
        double font = Math.Min(MaxFont, Math.Min(w / (CourierAdvance * longest), h / (n * 1.12)));
        double line = Math.Min(h / n, font * 1.25);
        return (font, line);
    }

    public static string Build(SwiftMessage msg)
    {
        var (font, line) = Fit(msg);
        var rows = new StringBuilder();
        foreach (var l in msg.Lines)
        {
            rows.Append(l.Kind switch
            {
                LineKind.KeyValue => $"<div class=\"m\"><b>{E(l.Text)}</b>{E(l.Value)}</div>",
                LineKind.Bold => $"<div class=\"m\"><b>{E(l.Text)}</b></div>",
                _ => $"<div class=\"m\">{E(l.Text)}</div>",
            }).Append('\n');
        }

        return $$"""
<!doctype html><html lang="tr"><head><meta charset="utf-8">
<title>SWIFT {{E(msg.Reference)}}</title>
<style>
@page { size: A4; margin: 0; }
html, body { margin:0; padding:0; background:#fff; }
.blok { position:absolute; left:{{F(MarginX)}}pt; top:{{F(MarginY)}}pt; color:#1a1a1a; }
.m { font-family:'Courier New',monospace; font-size:{{F(font)}}pt; line-height:{{F(line)}}pt; white-space:pre; }
.m b { font-weight:700; }
</style></head><body><div class="blok">
{{rows}}</div></body></html>
""";
    }
}
