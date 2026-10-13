using UglyToad.PdfPig;

namespace SwiftPdf.Core;

public sealed class SwiftNotFoundException(string message) : Exception(message);

public sealed record ConvertResult(string OutputPath, SwiftMessage Message);

/// <summary>Girdi PDF'i → bir ya da birkaç tek sayfa SWIFT PDF'i.</summary>
public static class Converter
{
    public static async Task<List<ConvertResult>> ConvertAsync(string inputPath, PdfRenderer renderer, string? outDir = null)
    {
        var messages = new List<SwiftMessage>();
        foreach (var c in PdfInput.Read(inputPath))
        {
            foreach (var m in SwiftParser.Parse(c.Bytes))
            {
                if (m.Reference.Length > 0 && messages.Any(x => x.Reference == m.Reference)) continue;
                messages.Add(m);
            }
        }
        if (messages.Count == 0)
            throw new SwiftNotFoundException(
                "Bu PDF'te Kuveyt Türk SWIFT mesajı bulunamadı. Outlook'ta \"Giden Döviz Transfer İşleminiz\" mailini açıp " +
                "Acrobat sekmesinden \"Adobe PDF'e dönüştür\" ile PDF yapın ve o dosyayı buraya bırakın.");

        var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var results = new List<ConvertResult>();
        foreach (var m in messages)
        {
            var target = Unique(Path.Combine(dir, FileName(m)));
            var temp = Path.Combine(Path.GetTempPath(), "SwiftPdf-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                await renderer.RenderAsync(HtmlTemplate.Build(m), temp);
                using (var check = PdfDocument.Open(temp))
                {
                    if (check.NumberOfPages != 1)
                        throw new InvalidOperationException(
                            $"SWIFT mesajı tek sayfaya sığmadı ({check.NumberOfPages} sayfa). Mesaj alışılmadık uzunlukta.");
                }
                File.Move(temp, target);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            results.Add(new ConvertResult(target, m));
        }
        return results;
    }

    /// <summary>Dosya adı biçimi, örnek: 2026-01-15_Kuveyt Turk SWIFT Giden Transfer 100000001_(1.250,00 EUR).pdf</summary>
    public static string FileName(SwiftMessage m)
    {
        var date = DateTime.TryParse(m.SettlementDate, out var d) ? d.ToString("yyyy-MM-dd") : DateTime.Today.ToString("yyyy-MM-dd");
        var name = $"{date}_Kuveyt Turk SWIFT Giden Transfer";
        if (m.Reference.Length > 0) name += " " + m.Reference;
        if (m.Amount.Length > 0) name += $"_({m.Amount} {m.Currency})";
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name + ".pdf";
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}).pdf");
            if (!File.Exists(p)) return p;
        }
    }
}
