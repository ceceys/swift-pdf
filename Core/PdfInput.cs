using UglyToad.PdfPig;

namespace SwiftPdf.Core;

/// <summary>
/// Girdi PDF'ini okur. Outlook'un "Adobe PDF'e dönüştür" komutu çoğu ayarda bir PDF Portföyü üretir:
/// kapak sayfasında yalnız "Adobe Reader ile açın" yazar, asıl mail portföyün içine gömülü PDF'tir.
/// </summary>
public static class PdfInput
{
    public sealed record Candidate(string Name, byte[] Bytes);

    public static List<Candidate> Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var list = new List<Candidate>();
        using (var doc = PdfDocument.Open(bytes))
        {
            if (doc.Advanced.TryGetEmbeddedFiles(out var files) && files != null)
            {
                foreach (var f in files)
                {
                    var data = f.Bytes.ToArray();
                    if (IsPdf(data)) list.Add(new Candidate(f.Name ?? "ek.pdf", data));
                }
            }
        }
        if (list.Count == 0) list.Add(new Candidate(Path.GetFileName(path), bytes));
        return list;
    }

    private static bool IsPdf(byte[] data) =>
        data.Length > 5 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';
}
