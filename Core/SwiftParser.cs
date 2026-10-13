using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace SwiftPdf.Core;

/// <summary>
/// Outlook'tan "Adobe PDF'e dönüştür" ile basılmış Kuveyt Türk mailinden SWIFT bloğunu geri kurar.
/// Mail PDF'inde SWIFT metni üç sütunlu bir tabloda durur (etiket, ":", değer) ve dar sütunlarda
/// uzun etiketler ("Financial Institution Identification") ile banka adları alt satıra kayar.
/// Satırlar harf konumlarından kurulur; kaymalar sütun genişliğine bakılarak birleştirilir.
/// </summary>
public static partial class SwiftParser
{
    private sealed record Glyph(double X0, double X1, double Y, string Ch, bool Courier, double Size);

    private sealed class VLine
    {
        public List<Glyph> G { get; } = new();
        public double Y;
        public double PageWidth;
        public bool Courier => G.Count(g => g.Courier) * 2 > G.Count;
        public string Text => BuildText(G);
    }

    private sealed class Row
    {
        public LineKind Kind;          // Bold = değersiz etiket, KeyValue, Normal = banka adı / tarih
        public string Label = "";
        public string Value = "";
        public double LabelX0 = double.NaN;
        public double LastLabelX1 = double.NaN;
        public bool HasValue;
        public string? PreValue;       // ":" gelmeden önce etiketin yanında görülen değer parçası
    }

    // Bölüm adına göre şablon sütunları (Python sürümündeki SEP_COL / SEP_INDENT ile aynı).
    private static readonly Dictionary<string, (int Col, int Indent, string Rule)> Sections = new()
    {
        ["Message Header"] = (36, 3, "---------------------------- Message Header ----------------------------"),
        ["Message Text"] = (39, 3, "---------------------------- Message Text ------------------------------"),
        ["Interventions"] = (31, 0, "----------------------- Interventions ----------------------------------"),
    };

    [GeneratedRegex(@"^-{3,}\s*(.+?)\s*-{3,}$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"#?([A-Z]{3})\s*([0-9][0-9.,]*)#?")]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"^-+$")]
    private static partial Regex DashesRegex();

    private const double PageStride = 10000;

    /// <summary>PDF'teki bütün SWIFT bildirimlerini döndürür; yoksa boş liste.</summary>
    public static List<SwiftMessage> Parse(byte[] pdf)
    {
        var lines = ReadLines(pdf);
        var result = new List<SwiftMessage>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Courier || lines[i].Text != "SWIFT MESAJI") continue;
            var msg = new SwiftMessage { Intro = ReadIntro(lines, i) };

            var block = new List<VLine>();
            int j = i + 1;
            for (; j < lines.Count; j++)
            {
                var t = lines[j].Text;
                if (t.Length == 0) continue;
                if (!lines[j].Courier) { if (block.Count > 0) break; else continue; }
                block.Add(lines[j]);
            }
            if (block.Count == 0) continue;
            BuildLines(block, msg);
            FillFields(msg);
            result.Add(msg);
            i = j - 1;
        }
        return result;
    }

    /// <summary>PDF'te en az bir harf var mı (taranmış/resim PDF'i ayırmak için)?</summary>
    public static bool HasText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            if (page.Letters.Any(l => !string.IsNullOrWhiteSpace(l.Value))) return true;
        return false;
    }

    // ---------- PDF'ten görsel satırlar ----------

    private static List<VLine> ReadLines(byte[] pdf)
    {
        var all = new List<VLine>();
        using var doc = PdfDocument.Open(pdf);
        int pageIndex = 0;
        foreach (var page in doc.GetPages())
        {
            double h = page.Height;
            double offset = pageIndex++ * PageStride;   // sayfalar tek bir dikey eksende ardışık durur
            var glyphs = page.Letters
                .Select(l => new Glyph(
                    l.StartBaseLine.X, l.EndBaseLine.X, offset + h - l.StartBaseLine.Y, l.Value,
                    (l.FontName ?? "").Contains("Courier", StringComparison.OrdinalIgnoreCase),
                    l.PointSize))
                .OrderBy(g => g.Y).ThenBy(g => g.X0)
                .ToList();

            var pageLines = new List<VLine>();
            foreach (var g in glyphs)
            {
                var line = pageLines.Count > 0 && Math.Abs(pageLines[^1].Y - g.Y) <= 2.5 ? pageLines[^1] : null;
                if (line == null) { line = new VLine { Y = g.Y, PageWidth = page.Width }; pageLines.Add(line); }
                line.G.Add(g);
            }
            foreach (var l in pageLines) l.G.Sort((a, b) => a.X0.CompareTo(b.X0));
            all.AddRange(pageLines);
        }
        return all;
    }

    /// <summary>
    /// Harflerden metin kurar. Boşluk, PDF'teki boşluk karakterlerinden değil iki harf arasındaki mesafeden çıkarılır:
    /// bazı çizimler (Word) harflerin üstüne binen fazladan boşluk karakteri yazar ve sözcüğü bölerdi ("N etwork").
    /// </summary>
    private static string BuildText(IEnumerable<Glyph> glyphs)
    {
        var sb = new StringBuilder();
        Glyph? prev = null;
        var spaces = new List<Glyph>();
        foreach (var g in glyphs)
        {
            if (string.IsNullOrWhiteSpace(g.Ch)) { if (prev != null) spaces.Add(g); continue; }
            if (prev != null)
            {
                // Aradaki mesafe yeterliyse ya da iki harfin ARASINDA duran bir boşluk karakteri varsa sözcük biter.
                // Harfin üstüne binen boşluk karakteri (Word) sayılmaz.
                bool gap = g.X0 - prev.X1 > 0.25 * g.Size;
                bool between = spaces.Any(s =>
                {
                    double c = (s.X0 + s.X1) / 2;
                    return c >= prev.X1 - 0.3 && c <= g.X0 + 0.3;
                });
                if (gap || between) sb.Append(' ');
            }
            spaces.Clear();
            sb.Append(g.Ch);
            prev = g;
        }
        return sb.ToString();
    }

    private static string ReadIntro(List<VLine> lines, int swiftIndex)
    {
        int start = -1;
        for (int k = swiftIndex - 1; k >= Math.Max(0, swiftIndex - 8); k--)
            if (lines[k].Text.StartsWith("Değerli Müşterimiz", StringComparison.Ordinal)) { start = k; break; }
        if (start < 0) return "";
        var parts = lines.Skip(start + 1).Take(swiftIndex - start - 1)
            .Select(l => l.Text).Where(t => t.Length > 0);
        return string.Join(" ", parts);
    }

    // ---------- SWIFT satırlarının kurulumu ----------

    private static void BuildLines(List<VLine> block, SwiftMessage msg)
    {
        double charW = Median(block.SelectMany(l => l.G)
            .Where(g => g.Courier && !string.IsNullOrWhiteSpace(g.Ch)).Select(g => g.X1 - g.X0));
        if (double.IsNaN(charW) || charW <= 0) charW = 5.0;

        if (LooksClipped(block, charW))
            msg.Warnings.Add("Maildeki bazı uzun satırlar Adobe PDF'te sayfa kenarında kesilmiş olabilir. " +
                             "Çıktıyı Outlook'taki mailin kendisiyle karşılaştırın.");

        // Bölümlere ayır: ayraçtan önceki satır tarih satırıdır. Dar basımda ayraç da bölünür:
        // "---- Message Header ------" + alt satırda "------", ya da bölüm adı ikiye ayrılır.
        var groups = new List<(string? Name, List<VLine> Lines)> { (null, new List<VLine>()) };
        bool afterRule = false;
        string? partialRule = null;
        foreach (var l in block)
        {
            var t = l.Text;
            if (partialRule != null)
            {
                var joined = partialRule + " " + t;
                partialRule = null;
                var jm = RuleRegex().Match(joined);
                if (jm.Success) { groups.Add((jm.Groups[1].Value.Trim(), new List<VLine>())); afterRule = true; continue; }
            }
            if (afterRule && DashesRegex().IsMatch(t)) continue;
            var m = RuleRegex().Match(t);
            if (m.Success) { groups.Add((m.Groups[1].Value.Trim(), new List<VLine>())); afterRule = true; continue; }
            if (t.StartsWith("---", StringComparison.Ordinal) && !DashesRegex().IsMatch(t)) { partialRule = t; continue; }
            afterRule = false;
            groups[^1].Lines.Add(l);
        }

        foreach (var (name, lines) in groups)
        {
            if (name == null)
            {
                foreach (var l in lines) msg.Lines.Add(new SwiftLine(LineKind.Normal, l.Text));
                continue;
            }
            var (col, indent, rule) = Sections.TryGetValue(name, out var s) ? s : (39, 3, GenericRule(name));
            msg.Lines.Add(new SwiftLine(LineKind.Bold, rule));
            foreach (var r in BuildRows(lines, charW))
            {
                switch (r.Kind)
                {
                    case LineKind.KeyValue:
                        msg.Lines.Add(new SwiftLine(LineKind.KeyValue,
                            (new string(' ', indent) + r.Label).PadRight(col), ": " + r.Value));
                        break;
                    case LineKind.Bold:
                        msg.Lines.Add(new SwiftLine(LineKind.Bold, r.Label));
                        break;
                    default:
                        msg.Lines.Add(new SwiftLine(LineKind.Normal, new string(' ', col + 2) + r.Value));
                        break;
                }
                msg.RawRows.Add((r.Kind, r.Label, r.Value));
            }
        }

        static string GenericRule(string n) => "---------------------------- " + n + " ----------------------------";
    }

    /// <summary>
    /// Kesilmiş satır: boşluksuz çok uzun bir değer tabloyu sayfadan genişletirse bazı çizimler (Edge) sayfa
    /// kenarından taşan harfleri hiç yazmaz. Belirtisi: bölüm ayracının (tablo genişliği) belirgin ötesine taşan
    /// satırların, en az iki ayrı bölümde, sayfa içinde aynı noktada bitmesi. Aynı hücrede alt satıra kayan uzun
    /// değer de satırları aynı noktada bitirir; o tek bölümde kalır ve ayraçtan bu kadar taşmaz. Word taşan
    /// harfleri sayfanın dışına da yazar; metin eksik olmadığı için o durum uyarı sayılmaz.
    /// </summary>
    private static bool LooksClipped(List<VLine> block, double charW)
    {
        static double LastX1(VLine l) => l.G.LastOrDefault(g => !string.IsNullOrWhiteSpace(g.Ch))?.X1 ?? 0;
        var section = new Dictionary<VLine, int>();
        int sec = 0;
        foreach (var l in block)
        {
            if (l.Text.StartsWith("---", StringComparison.Ordinal)) sec++;
            section[l] = sec;
        }
        var ruleEnds = block.Where(l => l.Text.StartsWith("---", StringComparison.Ordinal)).Select(LastX1).ToList();
        if (ruleEnds.Count == 0) return false;
        double ruleEnd = ruleEnds.Max();
        var over = block.Where(l => LastX1(l) > ruleEnd + 6 * charW).ToList();
        if (over.Count < 2 || over.Any(l => LastX1(l) > l.PageWidth)) return false;
        double edge = over.Max(LastX1);
        return over.Where(l => LastX1(l) >= edge - 1.2 * charW).Select(l => section[l]).Distinct().Count() >= 2;
    }

    /// <summary>Sınama kipinde satır ve sütun ayrıntısını yazar.</summary>
    public static Action<string>? Debug { get; set; }

    /// <summary>Bu metni içeren satırların harfleri de yazılır (SWIFTPDF_DEBUG_GLYPHS).</summary>
    public static string? DebugGlyphs { get; set; }

    private static List<Row> BuildRows(List<VLine> lines, double charW)
    {
        double colonX = FindColonX(lines);
        if (Debug != null)
        {
            Debug($"-- bölüm: colonX={colonX:0.00} charW={charW:0.000}");
            foreach (var l in lines)
            {
                Debug($"y={l.Y,7:0.00} x0={l.G[0].X0,7:0.00} x1={l.G[^1].X1,7:0.00} | {l.Text}");
                if (DebugGlyphs != null && l.Text.Contains(DebugGlyphs, StringComparison.Ordinal))
                    Debug("   " + string.Join(" ", l.G.Select(g => $"[{g.Ch}|{g.X0:0.00}-{g.X1:0.00}]")));
            }
        }
        var rows = new List<Row>();
        Row? pending = null;      // etiket kayması ya da ayrı satırdaki ":" bu satıra bağlanır
        Row? holder = null;       // değer kayması (devam satırı) bu satıra eklenir
        string? prefix = null;    // dikeyde ortalanmış satırda etiketin üstünde kalan değer parçası
        int prefixLines = 0;      // prefix kaç satırdı
        int owed = 0;             // ortalanmış değerin ":" satırının altında kalan, holder'a ait satır sayısı

        var split = lines.Select(l => SplitLine(l, colonX, charW)).ToList();
        var texts = split.Select(s => (Label: BuildText(s.Lab), Value: BuildText(s.Val))).ToList();

        void Attach(Row row, string v)
        {
            AttachValue(row, ref prefix, v);
            owed = prefixLines;
            prefixLines = 0;
        }

        for (int idx = 0; idx < lines.Count; idx++)
        {
            var (lab, _) = split[idx];
            var (label, value) = texts[idx];
            var labG = lab.Where(g => !string.IsNullOrWhiteSpace(g.Ch)).ToList();
            bool hasColon = value.StartsWith(':');
            bool valueOnly = label.Length == 0 && value.Length > 0 && !hasColon;

            // 1) Ortalanmış değer simetriktir: ":" satırının üstünde kaç satır varsa altında da o kadar satır o değere aittir.
            if (valueOnly && owed > 0 && holder != null)
            {
                holder.Value = Join(holder.Value, value);
                owed--;
                continue;
            }

            // 2) Ortalanmış hücre: değer birkaç satıra bölünmüş, etiket ve ":" ortada. Üstteki değer parçaları
            //    önceki satırın devamı değil, alttaki etiketin değeridir. Banka adı (BICFI altında, hücre üste
            //    hizalı) ve yarım kalmış banka adının devamı bu kurala girmez.
            bool bankLine = value.StartsWith('(') && holder is { Label: "BICFI" } or { Kind: LineKind.Normal };
            bool bankCont = holder is { Kind: LineKind.Normal } && !holder.Value.EndsWith(')');
            if (valueOnly && !bankLine && !bankCont && IsCenteredPrefix(lines, texts, idx))
            {
                prefix = prefix == null ? value : Join(prefix, value);
                prefixLines++;
                continue;
            }

            // 3) Etiket parçası: üstteki kaymış etikete eklenebilir mi, yoksa yeni bir satır mı?
            if (label.Length > 0)
            {
                double x0 = labG[0].X0, x1 = labG[^1].X1;
                string? v = hasColon ? value[1..].Trim() : (value.Length > 0 ? value : null);
                if (pending != null && (!hasColon || !pending.HasValue)
                    && ShouldMerge(pending, label, x0, charW, colonX))
                {
                    pending.Label += " " + label;
                    pending.LastLabelX1 = x1;
                    if (hasColon) { Attach(pending, v!); holder = pending; }
                    else if (v != null)
                    {
                        if (pending.HasValue) { pending.Value = Join(pending.Value, v); holder = pending; }
                        else pending.PreValue = pending.PreValue == null ? v : Join(pending.PreValue, v);
                    }
                    continue;
                }

                FlushPreValue(pending, holder);
                owed = 0;
                var row = new Row { Kind = LineKind.Bold, Label = label, LabelX0 = x0, LastLabelX1 = x1 };
                rows.Add(row);
                pending = row;
                if (hasColon) { Attach(row, v!); holder = row; }
                else { row.PreValue = v; holder = null; }
                continue;
            }

            // 4) Ayrı satırdaki ":" (etiket bölünmüş, ":" ortada)
            if (hasColon)
            {
                string v = value[1..].Trim();
                if (pending is { HasValue: false }) { Attach(pending, v); holder = pending; }
                else
                {
                    var row = new Row { Kind = LineKind.KeyValue, Value = v, HasValue = true };
                    rows.Add(row);
                    holder = row;
                }
                continue;
            }

            // 5) Değerin devamı ya da banka adı.
            if (value.Length > 0)
            {
                if (holder == null || bankLine)
                {
                    var bank = new Row { Kind = LineKind.Normal, Value = value, HasValue = true };
                    rows.Add(bank);
                    holder = bank;
                }
                else
                {
                    holder.Value = Join(holder.Value, value);
                }
            }
        }
        FlushPreValue(pending, holder);
        // ":" satırı hiç gelmediyse üstte bekleyen parça kaybolmasın.
        if (prefix != null)
        {
            if (holder != null) holder.Value = Join(holder.Value, prefix);
            else rows.Add(new Row { Kind = LineKind.Normal, Value = prefix, HasValue = true });
        }
        return rows;
    }

    /// <summary>":" gelince değeri kurar: üstte kalan parça, etiketin ilk satırındaki parça, ":" satırındaki parça.</summary>
    private static void AttachValue(Row row, ref string? prefix, string v)
    {
        var parts = new[] { prefix, row.PreValue, v }.OfType<string>().Where(p => p.Length > 0).ToList();
        row.Value = parts.Count == 0 ? "" : parts.Aggregate(Join);
        row.Kind = LineKind.KeyValue;
        row.HasValue = true;
        row.PreValue = null;
        prefix = null;
    }

    /// <summary>":" hiç gelmediyse etiketin yanındaki değer parçası bir önceki değerin devamıdır.</summary>
    private static void FlushPreValue(Row? row, Row? holder)
    {
        if (row?.PreValue == null) return;
        if (holder != null && holder != row) holder.Value = Join(holder.Value, row.PreValue);
        else { row.Kind = LineKind.KeyValue; row.Value = row.PreValue; row.HasValue = true; }
        row.PreValue = null;
    }

    /// <summary>
    /// Satır idx, alttaki ":" satırına ait ortalanmış değerin üst parçası mı? Hücre dikeyde ortalandığı için ":"
    /// satırının tam aynası uzaklıkta, arada başka ":" olmadan, bir değer satırı daha bulunur. Aradaki etiket
    /// parçaları (iki satıra bölünmüş etiket) sayılmaz.
    /// </summary>
    private static bool IsCenteredPrefix(List<VLine> lines, List<(string Label, string Value)> texts, int idx)
    {
        bool HasColon(int i) => texts[i].Value.StartsWith(':');
        bool ValueOnly(int i) => texts[i].Label.Length == 0 && texts[i].Value.Length > 0 && !HasColon(i);
        bool LabelOnly(int i) => texts[i].Label.Length > 0 && texts[i].Value.Length == 0;

        int k = idx + 1;
        while (k < lines.Count && !HasColon(k))
        {
            if (!ValueOnly(k) && !LabelOnly(k)) return false;
            k++;
        }
        if (k >= lines.Count) return false;
        double d = lines[k].Y - lines[idx].Y;
        for (int m = k + 1; m < lines.Count; m++)
        {
            if (HasColon(m)) return false;
            double dm = lines[m].Y - lines[k].Y;
            if (dm > d + 1.5) return false;
            if (Math.Abs(dm - d) <= 1.5) return ValueOnly(m);
        }
        return false;
    }

    /// <summary>
    /// Satırı etiket ve değer kısmına ayırır. Etiketlerde ":" geçmez: etiket sütunundan başlayan satırdaki ilk ":"
    /// ayırıcıdır, etikete bitişik olsa bile ("Message Identification: 100000001"). ":" yoksa ve etiket ile değer
    /// sütunu arasında geniş boşluk varsa iki hücredir ("Creation Date      2026-09-"); boşluksuz uzun satır
    /// tek hücredir ("Swift INPUT FIN pacs.008.001.08"). ":" sütunundan başlayan satır yalnız değerdir.
    /// </summary>
    private static (List<Glyph> Lab, List<Glyph> Val) SplitLine(VLine l, double colonX, double charW)
    {
        var gs = l.G;
        var first = gs.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Ch));
        if (first == null) return (new(), new());
        if (!double.IsNaN(colonX) && first.X0 >= colonX - 1.5) return (new(), gs);

        int colon = gs.FindIndex(g => g.Ch == ":");
        int gap = -1;
        if (!double.IsNaN(colonX))
        {
            Glyph? prev = null;
            for (int i = 0; i < gs.Count && gap < 0; i++)
            {
                var g = gs[i];
                if (string.IsNullOrWhiteSpace(g.Ch)) continue;
                if (prev != null && g.X0 >= colonX - 1.5 && g.X0 - prev.X1 >= 1.4 * charW) gap = i;
                prev = g;
            }
        }
        // Değer sütunu ":"dan önce başlıyorsa ayırıcı yok demektir; değerin içindeki ":" ("17T13:10:44") ayırıcı sayılmaz.
        int cut = gap >= 0 && (colon < 0 || gap < colon) ? gap : colon;
        return cut >= 0 ? (gs.Take(cut).ToList(), gs.Skip(cut).ToList()) : (gs, new());
    }

    /// <summary>
    /// Alt alta iki etiket satırı tek bir etiketin parçaları mı? Önce bilinen etiket sözlüğüne bakılır
    /// ("Financial Institution" + "Identification" birleşir, "Credit Transfer Transaction Information" +
    /// "Payment Identification" ayrı kalır); sözlükte yoksa sütun genişliği karar verir.
    /// </summary>
    private static bool ShouldMerge(Row pending, string next, double nextX0, double charW, double colonX)
    {
        if (Math.Abs(pending.LabelX0 - nextX0) > 1.5) return false;
        var merged = pending.Label + " " + next;
        if (KnownLabels.IsKnown(merged) || KnownLabels.IsPrefixOfKnown(merged)) return true;
        if (KnownLabels.IsKnown(pending.Label)) return false;
        return !double.IsNaN(colonX) && IsWrap(pending.LastLabelX1, next, charW, colonX);
    }

    /// <summary>Önceki etiket satırının sonuna sonraki satırın ilk sözcüğü sığmıyorsa satır kaymıştır.</summary>
    private static bool IsWrap(double prevX1, string next, double charW, double colonX)
    {
        int firstWord = next.IndexOf(' ') is var p && p > 0 ? p : next.Length;
        return prevX1 + charW + firstWord * charW > colonX - 2.0;
    }

    private static string Join(string a, string b)
    {
        if (a.Length == 0) return b;
        char last = a[^1];
        bool tight = last is '{' or ':' or '}' or '/' or '-' || b.StartsWith('{');
        return tight ? a + b : a + " " + b;
    }

    private static double FindColonX(List<VLine> lines)
    {
        // Her satırın ilk ":" işaretinin konumu; ayırıcılar aynı sütunda durduğu için en sık konum ":" sütunudur.
        var xs = new List<double>();
        foreach (var l in lines)
        {
            var c = l.G.FirstOrDefault(g => g.Ch == ":");
            if (c != null) xs.Add(Math.Round(c.X0 * 2) / 2);
        }
        if (xs.Count == 0) return double.NaN;
        return xs.GroupBy(x => x).OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key).First().Key;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        return a.Length == 0 ? double.NaN : a[a.Length / 2];
    }

    // ---------- Dosya adı için alanlar ----------

    private static void FillFields(SwiftMessage msg)
    {
        string? Get(string label) => msg.RawRows
            .Where(r => r.Kind == LineKind.KeyValue && r.Label == label).Select(r => r.Value).FirstOrDefault();

        msg.Reference = Get("Message Identification") ?? Get("Instruction Identification") ?? "";
        msg.SettlementDate = Get("Interbank Settlement Date") ?? "";

        var amount = Get("Instructed Amount") ?? Get("Interbank Settlement Amount") ?? "";
        var m = AmountRegex().Match(amount);
        if (m.Success) { msg.Currency = m.Groups[1].Value; msg.Amount = m.Groups[2].Value; }

        // Alıcı adı: "Creditor" başlığından sonraki ilk "Name" satırı.
        bool afterCreditor = false;
        foreach (var r in msg.RawRows)
        {
            if (r.Kind == LineKind.Bold) afterCreditor = r.Label == "Creditor";
            else if (afterCreditor && r.Kind == LineKind.KeyValue && r.Label == "Name") { msg.Creditor = r.Value; break; }
        }
    }
}
