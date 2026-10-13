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

    // ---------- PDF'ten görsel satırlar ----------

    private static List<VLine> ReadLines(byte[] pdf)
    {
        var all = new List<VLine>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            double h = page.Height;
            var glyphs = page.Letters
                .Select(l => new Glyph(
                    l.StartBaseLine.X, l.EndBaseLine.X, h - l.StartBaseLine.Y, l.Value,
                    (l.FontName ?? "").Contains("Courier", StringComparison.OrdinalIgnoreCase),
                    l.PointSize))
                .OrderBy(g => g.Y).ThenBy(g => g.X0)
                .ToList();

            var pageLines = new List<VLine>();
            foreach (var g in glyphs)
            {
                var line = pageLines.Count > 0 && Math.Abs(pageLines[^1].Y - g.Y) <= 2.5 ? pageLines[^1] : null;
                if (line == null) { line = new VLine { Y = g.Y }; pageLines.Add(line); }
                line.G.Add(g);
            }
            foreach (var l in pageLines) l.G.Sort((a, b) => a.X0.CompareTo(b.X0));
            all.AddRange(pageLines);
        }
        return all;
    }

    private static string BuildText(IEnumerable<Glyph> glyphs)
    {
        var sb = new StringBuilder();
        Glyph? prev = null;
        foreach (var g in glyphs)
        {
            if (string.IsNullOrWhiteSpace(g.Ch))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                prev = g;
                continue;
            }
            if (prev != null && !string.IsNullOrWhiteSpace(prev.Ch) && g.X0 - prev.X1 > 0.25 * g.Size
                && sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
            sb.Append(g.Ch);
            prev = g;
        }
        return sb.ToString().Trim();
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

        // Bölümlere ayır: ayraçtan önceki satır tarih satırıdır.
        var groups = new List<(string? Name, List<VLine> Lines)> { (null, new List<VLine>()) };
        foreach (var l in block)
        {
            var m = RuleRegex().Match(l.Text);
            if (m.Success) { groups.Add((m.Groups[1].Value.Trim(), new List<VLine>())); continue; }
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

    /// <summary>Sınama kipinde satır ve sütun ayrıntısını yazar.</summary>
    public static Action<string>? Debug { get; set; }

    private static List<Row> BuildRows(List<VLine> lines, double charW)
    {
        double colonX = FindColonX(lines);
        if (Debug != null)
        {
            Debug($"-- bölüm: colonX={colonX:0.00} charW={charW:0.000}");
            foreach (var l in lines)
                Debug($"y={l.Y,7:0.00} x0={l.G[0].X0,7:0.00} x1={l.G[^1].X1,7:0.00} | {l.Text}");
        }
        var rows = new List<Row>();
        Row? pending = null;      // etiket kayması ya da ayrı satırdaki ":" bu satıra bağlanır
        Row? holder = null;       // değer kayması (devam satırı) bu satıra eklenir
        string? prefix = null;    // dikeyde ortalanmış satırda etiketin üstünde kalan değer parçası

        var split = lines.Select(l =>
        {
            if (double.IsNaN(colonX)) return (Lab: l.G, Val: new List<Glyph>());
            return (Lab: l.G.Where(g => g.X0 < colonX - 1.5).ToList(), Val: l.G.Where(g => g.X0 >= colonX - 1.5).ToList());
        }).ToList();
        var texts = split.Select(s => (Label: BuildText(s.Lab), Value: BuildText(s.Val))).ToList();

        for (int idx = 0; idx < lines.Count; idx++)
        {
            var (lab, _) = split[idx];
            var (label, value) = texts[idx];
            var labG = lab.Where(g => !string.IsNullOrWhiteSpace(g.Ch)).ToList();

            // Ortalanmış hücre: değer iki (ya da daha çok) satıra bölünmüş, etiket ve ":" ortadaki satırda.
            // Üstteki değer parçası bir önceki satırın devamı değil, alttaki etiketin değeridir.
            if (label.Length == 0 && value.Length > 0 && !value.StartsWith(':') && !value.StartsWith('(')
                && IsCenteredPrefix(lines, texts, idx))
            {
                prefix = prefix == null ? value : Join(prefix, value);
                continue;
            }

            if (label.Length > 0 && value.Length == 0)
            {
                double x0 = labG[0].X0, x1 = labG[^1].X1;
                if (pending != null && pending != rows[0] && !pending.Label.Any(char.IsDigit)
                    && Math.Abs(pending.LabelX0 - x0) <= 1.5 && !double.IsNaN(colonX)
                    && IsWrap(pending.LastLabelX1, label, charW, colonX))
                {
                    pending.Label += " " + label;
                    pending.LastLabelX1 = x1;
                    continue;
                }
                var row = new Row { Kind = LineKind.Bold, Label = label, LabelX0 = x0, LastLabelX1 = x1 };
                rows.Add(row);
                pending = row; holder = null;
                continue;
            }

            if (value.StartsWith(':'))
            {
                string v = value[1..].Trim();
                if (prefix != null) { v = v.Length == 0 ? prefix : Join(prefix, v); prefix = null; }
                if (label.Length > 0)
                {
                    var row = new Row
                    {
                        Kind = LineKind.KeyValue, Label = label, Value = v, HasValue = true,
                        LabelX0 = labG[0].X0, LastLabelX1 = labG[^1].X1,
                    };
                    rows.Add(row);
                    pending = row; holder = row;
                }
                else if (pending != null && !pending.HasValue)
                {
                    pending.Kind = LineKind.KeyValue; pending.Value = v; pending.HasValue = true;
                    holder = pending;
                }
                else
                {
                    var row = new Row { Kind = LineKind.KeyValue, Value = v, HasValue = true };
                    rows.Add(row);
                    holder = row;
                }
                continue;
            }

            if (value.Length > 0)
            {
                if (label.Length > 0)
                {
                    var row = new Row { Kind = LineKind.Bold, Label = label, LabelX0 = labG[0].X0, LastLabelX1 = labG[^1].X1 };
                    rows.Add(row);
                    pending = row;
                }
                if (value.StartsWith('(') || holder == null)
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
        return rows;
    }

    /// <summary>
    /// Satır idx, alttaki "etiket :" satırının üst yarısı mı? Etiket satırı yakın (6 pt'den az) ve onun
    /// altında aynı uzaklıkta bir değer satırı daha varsa hücre dikeyde ortalanmıştır.
    /// </summary>
    private static bool IsCenteredPrefix(List<VLine> lines, List<(string Label, string Value)> texts, int idx)
    {
        int k = idx + 1;
        while (k < lines.Count && texts[k].Label.Length == 0 && !texts[k].Value.StartsWith(':')
               && !texts[k].Value.StartsWith('(') && lines[k].Y - lines[k - 1].Y < 6) k++;
        if (k >= lines.Count) return false;
        var (label, value) = texts[k];
        if (label.Length == 0 || !value.StartsWith(':')) return false;
        double up = lines[k].Y - lines[idx].Y;
        if (up <= 0 || up >= 6 * (k - idx)) return false;
        // Etiketin altında simetrik bir değer satırı var mı?
        if (k + 1 >= lines.Count) return false;
        var below = texts[k + 1];
        if (below.Label.Length > 0 || below.Value.Length == 0 || below.Value.StartsWith(':')) return false;
        double down = lines[k + 1].Y - lines[k].Y;
        double upOne = lines[k].Y - lines[k - 1].Y;
        return Math.Abs(down - upOne) < 1.5;
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
        var xs = new List<double>();
        foreach (var l in lines)
        {
            Glyph? prev = null;
            foreach (var g in l.G)
            {
                if (string.IsNullOrWhiteSpace(g.Ch)) continue;
                if (g.Ch == ":" && (prev == null || g.X0 - prev.X1 > 2.0)) { xs.Add(Math.Round(g.X0 * 2) / 2); break; }
                prev = g;
            }
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
