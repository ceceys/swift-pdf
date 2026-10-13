using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftPdf.Core;

/// <summary>
/// GitHub Releases üzerinden güncelleme. Programın internete çıktığı tek yer budur: yeni sürüm var mı diye bakar;
/// kullanıcı "Şimdi güncelle" demeden hiçbir şey indirmez ve kurmaz. PDF'ler hiçbir yere gönderilmez.
/// Akış: /releases/latest → sürüm karşılaştır → dosyayı indir → SHA256SUMS.txt ile doğrula → kur.
/// </summary>
public static class Updater
{
    public const string Owner = "ceceys";
    public const string Repo = "swift-pdf";
    public const string ReleasesPage = $"https://github.com/{Owner}/{Repo}/releases";

    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    /// <summary>Kendiliğinden denetim en çok bu sıklıkta ağa çıkar (ofiste ortak IP'nin GitHub API sınırı saatte 60).</summary>
    private static readonly TimeSpan AutoInterval = TimeSpan.FromHours(12);

    /// <summary>Son sürüm bilgisi. Sınamada yerel sunucuya çevrilir.</summary>
    public static string LatestApi { get; set; } = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    /// <summary>Sürüm notu ({0} = etiket). raw.githubusercontent.com API sınırına sayılmaz.</summary>
    public static string NotesUrl { get; set; } = $"https://raw.githubusercontent.com/{Owner}/{Repo}/{{0}}/SURUM-NOTLARI.md";

    /// <summary>Son denetim zamanı, bulunan sürüm ve yarım kalan güncelleme.</summary>
    public static string StatePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftPdf", "guncelleme.json");

    public sealed record Release(Version Version, string Tag, string? InstallerUrl, string? PortableUrl, string? SumsUrl, string Notes);

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>"1.1.0" biçimi (Version.ToString dördüncü haneyi de yazabilir).</summary>
    public static string Text(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>Kurulum paketiyle mi kuruldu (yanında kaldırıcı var) yoksa taşınabilir exe mi?</summary>
    public static bool IsInstalled => Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").Any();

    // ---------- Denetim ----------

    /// <summary>
    /// Daha yeni bir sürüm varsa onu döndürür, yoksa null. Kendiliğinden denetimde (manual=false) son 12 saatte
    /// bakıldıysa ağa çıkılmaz, o zaman bulunan sonuç kullanılır. Ağ hatasında istisna fırlatır.
    /// </summary>
    public static async Task<Release?> CheckAsync(bool manual, CancellationToken ct = default)
    {
        var state = LoadState() ?? new State();
        var age = DateTime.UtcNow - state.CheckedUtc;
        if (!manual && state.CheckedUtc != default && age >= TimeSpan.Zero && age < AutoInterval)
            return Newer(FromState(state));

        var latest = await FetchLatestAsync(ct);
        state.CheckedUtc = DateTime.UtcNow;
        state.Tag = latest?.Tag;
        state.InstallerUrl = latest?.InstallerUrl;
        state.PortableUrl = latest?.PortableUrl;
        state.SumsUrl = latest?.SumsUrl;
        state.Notes = latest?.Notes;
        SaveState(state);
        return Newer(latest);
    }

    private static Release? Newer(Release? r) => r != null && r.Version > Current ? r : null;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SwiftPdf", Text(Current)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Depodaki son sürüm; depoda henüz sürüm yoksa null.</summary>
    private static async Task<Release?> FetchLatestAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        using var resp = await http.GetAsync(LatestApi, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseTag(tag, out var ver)) return null;

        // Gerçek adres https ise indirme adresleri de https olmalı (sınamadaki yerel sunucu http'dir).
        bool https = LatestApi.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        string? installer = null, portable = null, sums = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                if (url is null || (https && !url.StartsWith("https://", StringComparison.Ordinal))) continue;
                if (name.StartsWith("SwiftPdf-Setup-", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) installer = url;
                else if (name.Equals("SwiftPdf.exe", StringComparison.OrdinalIgnoreCase)) portable = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = url;
            }
        }
        var notes = await FetchNotesAsync(http, tag, ver, ct) ?? $"Bu sürümdeki değişiklikler: {ReleasesPage}";
        return new Release(ver, tag, installer, portable, sums, notes);
    }

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return false;
        version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        return true;
    }

    /// <summary>
    /// Sürüm notu: o sürümün etiketindeki SURUM-NOTLARI.md dosyasının ilk bölümü, yalnız o sürüme aitse.
    /// GitHub'daki sürüm açıklaması hiç gösterilmez; uzunluğu ve biçimi programın dışında kalır.
    /// </summary>
    private static async Task<string?> FetchNotesAsync(HttpClient http, string tag, Version ver, CancellationToken ct)
    {
        try
        {
            var text = await http.GetStringAsync(string.Format(NotesUrl, Uri.EscapeDataString(tag)), ct);
            var section = FirstSection(text, ver);
            return string.IsNullOrWhiteSpace(section) ? null : section;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>SURUM-NOTLARI.md biçimi: "1.1.0 — tarih" satırı, altında "- " maddeleri, bölümler boş satırla ayrılır.</summary>
    public static string FirstSection(string text, Version ver)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length) return "";
        // İlk bölüm bu sürümün değilse gösterilmez ("1.2.30" başlığı 1.2.3 sayılmaz)
        var head = lines[i].TrimStart();
        var vt = Text(ver);
        if (!head.StartsWith(vt, StringComparison.Ordinal)
            || (head.Length > vt.Length && (char.IsDigit(head[vt.Length]) || head[vt.Length] == '.')))
            return "";
        var items = new List<string>();
        for (i++; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]); i++)
        {
            var l = lines[i].Trim();
            items.Add(l.StartsWith("- ", StringComparison.Ordinal) ? "•  " + l[2..] : l);
        }
        var s = string.Join("\n", items);
        return s.Length <= 4000 ? s : s[..4000] + "…";
    }

    // ---------- İndirme ve kurulum ----------

    /// <summary>
    /// Uygun dosyayı geçici klasöre indirir ve SHA-256 özetini doğrular. Özet listesi yoksa ya da özet tutmazsa
    /// dosya silinir, kurulmaz. progress 0..1.
    /// </summary>
    public static async Task<string> DownloadAsync(Release r, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        bool installer = IsInstalled && r.InstallerUrl is not null;
        var url = installer ? r.InstallerUrl : (r.PortableUrl ?? r.InstallerUrl);
        if (url is null) throw new InvalidOperationException("Bu sürümde indirilecek dosya bulunamadı.");
        if (r.SumsUrl is null) throw new InvalidOperationException("Sürümde doğrulama dosyası (SHA256SUMS.txt) yok; doğrulanamayan dosya kurulmaz.");

        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        var dir = Path.Combine(Path.GetTempPath(), "SwiftPdf-Guncelleme");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var target = Path.Combine(dir, fileName);

        using var http = CreateClient();
        var sumsText = await http.GetStringAsync(r.SumsUrl, ct);
        string? expected = null;
        foreach (var line in sumsText.Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].Equals(fileName, StringComparison.OrdinalIgnoreCase)) { expected = parts[0]; break; }
        }
        if (expected is null) throw new InvalidOperationException($"Doğrulama listesinde {fileName} yok; dosya kurulmaz.");

        try
        {
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? length = resp.Content.Headers.ContentLength;
                if (length is > MaxDownloadBytes) throw new InvalidOperationException("İndirilecek dosya beklenenden büyük.");
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(target);
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    total += n;
                    if (total > MaxDownloadBytes) throw new InvalidOperationException("İndirilecek dosya beklenenden büyük.");
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    if (length is > 0) progress?.Report((double)total / length.Value);
                }
            }

            await using (var fs = File.OpenRead(target))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("İndirilen dosya doğrulanamadı (SHA-256 tutmadı); güvenlik için silindi.");
            }
            progress?.Report(1.0);
            return target;
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// İndirilen dosyayı kurar; çağıran program hemen kapanmalıdır. Kurulum paketi sessiz çalışır, açık programı
    /// kapatır ve bitince programı yeniden açar (setup\SwiftPdf.iss, [Run] WizardSilent). Taşınabilir exe, program
    /// kapanınca kendi yerine kopyalanır ve yeniden başlatılır.
    /// </summary>
    public static void Install(string file, Release r)
    {
        bool setup = Path.GetFileName(file).StartsWith("SwiftPdf-Setup-", StringComparison.OrdinalIgnoreCase);
        string? exe = null;
        if (!setup)
        {
            exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programın yolu bulunamadı.");
            if (!CanWrite(Path.GetDirectoryName(exe)!))
                throw new InvalidOperationException($"Programın bulunduğu klasöre yazılamıyor. Yeni sürümü {ReleasesPage} adresinden indirin.");
        }

        var state = LoadState() ?? new State();
        state.PendingTag = r.Tag;
        SaveState(state);

        if (setup)
        {
            Process.Start(new ProcessStartInfo(file)
            {
                UseShellExecute = true,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            });
            return;
        }

        StartPortableReplace(Environment.ProcessId, file, exe!);
    }

    /// <summary>
    /// Taşınabilir exe için: pid'li süreç kapanınca file'ı exe'nin üzerine kopyalar ve exe'yi yeniden başlatır.
    /// "chcp 65001": yol Türkçe harf içerebilir ("C:\Users\Tüm Dijital\..."); betik UTF-8 yazılır, cmd de UTF-8 okur.
    /// </summary>
    public static void StartPortableReplace(int pid, string file, string exe)
    {
        var cmd = Path.Combine(Path.GetDirectoryName(file)!, "uygula.cmd");
        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("chcp 65001 >nul")
            .AppendLine(":bekle")
            .AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto bekle)")
            .AppendLine("set n=0")
            .AppendLine(":kopyala")
            .AppendLine($"copy /y \"{file}\" \"{exe}\" >nul && goto ac")
            .AppendLine("set /a n+=1")
            .AppendLine("if %n% lss 10 (ping -n 2 127.0.0.1 >nul & goto kopyala)")
            .AppendLine(":ac")
            .AppendLine($"start \"\" \"{exe}\"")
            .AppendLine($"del \"{file}\" >nul 2>&1")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(cmd, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>
    /// Önceki açılışta güncelleme başlatıldıysa sonucunu bir kez döndürür: Ok = şimdi o sürüm çalışıyor.
    /// </summary>
    public static (bool Ok, Version Version)? TakeUpdateOutcome()
    {
        var state = LoadState();
        if (state?.PendingTag is null) return null;
        var tag = state.PendingTag;
        state.PendingTag = null;
        SaveState(state);
        if (!TryParseTag(tag, out var v)) return null;
        return (Current >= v, v);
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".swiftpdf-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---------- Durum dosyası ----------

    private sealed class State
    {
        public DateTime CheckedUtc { get; set; }
        public string? Tag { get; set; }
        public string? InstallerUrl { get; set; }
        public string? PortableUrl { get; set; }
        public string? SumsUrl { get; set; }
        public string? Notes { get; set; }
        public string? PendingTag { get; set; }
    }

    private static Release? FromState(State s) =>
        TryParseTag(s.Tag, out var v)
            ? new Release(v, s.Tag!, s.InstallerUrl, s.PortableUrl, s.SumsUrl, s.Notes ?? $"Bu sürümdeki değişiklikler: {ReleasesPage}")
            : null;

    private static State? LoadState()
    {
        try
        {
            return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void SaveState(State s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
