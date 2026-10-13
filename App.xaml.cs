using System.Text;
using System.Windows;
using SwiftPdf.Core;

namespace SwiftPdf;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Sınama kipi: SwiftPdf.exe --test girdi.pdf cikti-klasoru
        // Pencere açmadan dönüştürür, satır dökümünü cikti-klasoru\dokum.txt'ye yazar.
        if (e.Args.Length == 3 && e.Args[0] == "--test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = 0;
            var log = new StringBuilder();
            var debug = new StringBuilder();
            if (Environment.GetEnvironmentVariable("SWIFTPDF_DEBUG") == "1")
                SwiftParser.Debug = s => debug.AppendLine(s);
            var host = new Window();
            using (var renderer = new PdfRenderer(host))
            {
                try
                {
                    Directory.CreateDirectory(e.Args[2]);
                    foreach (var r in await Converter.ConvertAsync(e.Args[1], renderer, e.Args[2]))
                    {
                        log.AppendLine("CIKTI: " + r.OutputPath);
                        log.Append(r.Message.Dump());
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine("HATA: " + ex.GetType().Name + ": " + ex.Message);
                    code = ex is SwiftNotFoundException ? 2 : 1;
                }
            }
            Directory.CreateDirectory(e.Args[2]);
            File.WriteAllText(Path.Combine(e.Args[2], "dokum.txt"), log.ToString(), new UTF8Encoding(false));
            if (debug.Length > 0)
                File.WriteAllText(Path.Combine(e.Args[2], "ayiklama.txt"), debug.ToString(), new UTF8Encoding(false));
            Shutdown(code);
            return;
        }

        var file = e.Args.FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        var main = new MainWindow(file);
        MainWindow = main;
        main.Show();
    }
}
