using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace SwiftPdf.Core;

/// <summary>
/// Girdi PDF'ini okur. Outlook'un "Adobe PDF'e dönüştür" komutu çoğu ayarda bir PDF Portföyü üretir:
/// kapak sayfasında yalnız "Adobe Reader ile açın" yazar, asıl mail portföyün içine gömülü PDF'tir.
/// </summary>
public static class PdfInput
{
    public sealed record Candidate(string Name, byte[] Bytes);

    private const string NotPdf =
        "PDF açılamadı: dosya boş, bozuk ya da PDF değil. Maili Outlook'ta \"Adobe PDF'e dönüştür\" ile yeniden PDF yapın.";

    public static List<Candidate> Read(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (IOException ex) { throw new UserFacingException("Dosya okunamadı: " + ex.Message); }
        catch (UnauthorizedAccessException) { throw new UserFacingException("Dosyayı okuma izni yok: " + path); }
        if (bytes.Length < 8 || !IsPdf(bytes)) throw new UserFacingException(NotPdf);

        var list = new List<Candidate>();
        try
        {
            using var doc = PdfDocument.Open(bytes);
            _ = doc.NumberOfPages;
            if (doc.Advanced.TryGetEmbeddedFiles(out var files) && files != null)
            {
                foreach (var f in files)
                {
                    var data = f.Bytes.ToArray();
                    if (IsPdf(data)) list.Add(new Candidate(f.Name ?? "ek.pdf", data));
                }
            }
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new UserFacingException(
                "Bu PDF parolalı. Parola koymadan yeniden PDF yapın: Outlook'ta maili açıp \"Adobe PDF'e dönüştür\" deyin.");
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            throw new UserFacingException(NotPdf);
        }
        if (list.Count == 0) list.Add(new Candidate(Path.GetFileName(path), bytes));
        return list;
    }

    private static bool IsPdf(byte[] data)
    {
        // "%PDF" dosyanın ilk 1 KB'ı içinde olabilir (başında birkaç bayt çöp bulunan PDF'ler).
        int max = Math.Min(data.Length - 4, 1024);
        for (int i = 0; i <= max; i++)
            if (data[i] == '%' && data[i + 1] == 'P' && data[i + 2] == 'D' && data[i + 3] == 'F') return true;
        return false;
    }
}
