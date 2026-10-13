namespace SwiftPdf.Core;

/// <summary>SWIFT bloğundaki bir satırın türü; şablon her türü farklı basar.</summary>
public enum LineKind
{
    /// <summary>Kalın satır: bölüm ayracı ya da değersiz başlık ("Group Header").</summary>
    Bold,
    /// <summary>Düz satır: tarih satırı ya da değer sütunundaki banka adı.</summary>
    Normal,
    /// <summary>Etiket : değer satırı.</summary>
    KeyValue,
}

/// <summary>Şablona gidecek tek satır. KeyValue'da Text etiket (hizalanmış), Value ": değer".</summary>
public sealed record SwiftLine(LineKind Kind, string Text, string Value = "");

/// <summary>Mailden çıkarılan SWIFT bildirimi.</summary>
public sealed class SwiftMessage
{
    /// <summary>"Talimatınızla ... gönderildi." cümlesi.</summary>
    public string Intro { get; set; } = "";
    public List<SwiftLine> Lines { get; } = new();

    /// <summary>Hizalanmamış satırlar (etiket, değer); alan okumak için.</summary>
    public List<(LineKind Kind, string Label, string Value)> RawRows { get; } = new();

    public string Reference { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Currency { get; set; } = "";
    public string SettlementDate { get; set; } = "";
    public string Creditor { get; set; } = "";

    /// <summary>Kullanıcıya gösterilecek uyarılar (girdide kesilmiş satır, çok küçük punto).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Şablonun düz metin dökümü; testte beklenen çıktıyla karşılaştırılır.</summary>
    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("GIRIS: " + Intro);
        foreach (var w in Warnings) sb.AppendLine("UYARI: " + w);
        foreach (var l in Lines)
        {
            sb.AppendLine(l.Kind switch
            {
                LineKind.KeyValue => "kv | " + l.Text + l.Value,
                LineKind.Bold => "b  | " + l.Text,
                _ => "n  | " + l.Text,
            });
        }
        return sb.ToString();
    }
}
