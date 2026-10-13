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
    private const double MinFont = 5.0;                    // genişlik yüzünden bunun altına inilmez, satır kaydırılır

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>Basılacak satır: kalın kısım (etiket) ve düz kısım (değer).</summary>
    public sealed record PrintLine(string Bold, string Normal);

    /// <summary>
    /// Punto ve satır aralığını seçer. En uzun satır 5 puntoda bile sığmıyorsa o satır değer sütununa hizalı
    /// alt satırlara kaydırılır; metin değişmez, yalnız basımda bölünür.
    /// </summary>
    public static (double Font, double LineHeight, List<PrintLine> Lines) Fit(SwiftMessage msg)
    {
        double w = PageW - 2 * MarginX, h = PageH - 2 * MarginY;
        int longest = Math.Max(1, msg.Lines.Count == 0 ? 1 : msg.Lines.Max(l => l.Text.Length + l.Value.Length));
        double font = Math.Min(MaxFont, w / (CourierAdvance * longest));
        int maxChars = int.MaxValue;
        if (font < MinFont)
        {
            font = MinFont;
            maxChars = (int)Math.Floor(w / (CourierAdvance * MinFont));
        }

        var lines = new List<PrintLine>();
        foreach (var l in msg.Lines)
        {
            if (l.Kind == LineKind.KeyValue)
            {
                var parts = Wrap(l.Text + l.Value, maxChars, l.Text.Length + 2);
                lines.Add(new PrintLine(l.Text, parts[0].Length > l.Text.Length ? parts[0][l.Text.Length..] : ""));
                lines.AddRange(parts.Skip(1).Select(p => new PrintLine("", p)));
            }
            else
            {
                int indent = l.Text.Length - l.Text.TrimStart().Length;
                var parts = Wrap(l.Text, maxChars, indent);
                lines.AddRange(parts.Select(p => l.Kind == LineKind.Bold ? new PrintLine(p, "") : new PrintLine("", p)));
            }
        }

        int n = Math.Max(1, lines.Count);
        font = Math.Min(font, h / (n * 1.12));
        double line = Math.Min(h / n, font * 1.25);
        return (font, line, lines);
    }

    /// <summary>Satırı en çok max harflik parçalara böler; devam parçaları indent kadar girintilenir.</summary>
    private static List<string> Wrap(string s, int max, int indent)
    {
        var result = new List<string>();
        if (s.Length <= max) { result.Add(s); return result; }
        indent = Math.Min(indent, max / 2);
        string pad = new(' ', indent);
        string rest = s;
        bool first = true;
        while (true)
        {
            string line = first ? rest : pad + rest;
            if (line.Length <= max) { result.Add(line); break; }
            // Sözcük sınırından böl; girinti (ilk satırda etiket ve ": ") hep satırda kalır. Boşluksuzsa harften böl.
            int cut = line.LastIndexOf(' ', max, Math.Max(1, max - indent - 8));
            if (cut <= indent) cut = max;
            result.Add(line[..cut].TrimEnd());
            rest = line[cut..].TrimStart();
            first = false;
        }
        return result;
    }

    public static string Build(SwiftMessage msg)
    {
        var (font, line, lines) = Fit(msg);
        if (font < MinFont - 0.005)
            msg.Warnings.Add($"Mesaj çok uzun olduğu için yazı {font.ToString("0.0", CultureInfo.GetCultureInfo("tr-TR"))} " +
                             "puntoya küçüldü; yazdırmadan önce okunurluğunu kontrol edin.");
        var rows = new StringBuilder();
        foreach (var l in lines)
        {
            rows.Append("<div class=\"m\">");
            if (l.Bold.Length > 0) rows.Append("<b>").Append(E(l.Bold)).Append("</b>");
            rows.Append(E(l.Normal)).Append("</div>\n");
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
