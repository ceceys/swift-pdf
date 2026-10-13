# SWIFT PDF sınama betiği.
# Her uydurma vakayı (tests\vakalar\*.html) dört sayfa düzeninde Microsoft Edge ile PDF'e basar,
# SwiftPdf.exe --test ile dönüştürür ve satırları tests\beklenen\<vaka>.txt ile birebir karşılaştırır.
# Olumsuz vakalar (bozuk, boş, PDF olmayan dosya, SWIFT'siz mail) anlaşılır hata ve çıkış kodu 2 beklemelidir.
#
# Kullanım:  powershell -ExecutionPolicy Bypass -File tests\run.ps1 [-Exe yol\SwiftPdf.exe] [-Filtre t08] [-Sakla]
# -Sakla: basılan PDF'ler ve çıktılar silinmez (inceleme için), klasör yolu yazılır.

param(
    [string]$Exe = "",
    [string]$Filtre = "",
    [switch]$Sakla
)

$ErrorActionPreference = "Stop"
$kok = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Get-ChildItem -Path (Join-Path $kok "bin") -Recurse -Filter SwiftPdf.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw "SwiftPdf.exe bulunamadı; önce 'dotnet build' çalıştırın ya da -Exe verin." }

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge bulunamadı." }

$is = Join-Path $env:TEMP ("swiftpdf-test-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $is | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$kunye = Get-Content (Join-Path $PSScriptRoot "vakalar\kunye.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$kesikOlabilir = @($kunye._kesik_olabilir)

# Sayfa düzenleri: SWIFT tablosunu saran sabit genişlik (pt, 0 = serbest) ve üstündeki boşluk (pt).
$duzenler = @(@(0, 0), @(370, 0), @(320, 37), @(280, 0))

function Duzen([string]$html, [int]$w, [int]$s) {
    $a = $html.IndexOf("<!--SWIFT-TABLO-BASI-->"); $b = $html.IndexOf("<!--SWIFT-TABLO-SONU-->")
    $tablo = $html.Substring($a, $b - $a)
    if ($w) {
        $px = [math]::Round($w * 96 / 72)
        $tablo = "<table border=0 cellspacing=0 cellpadding=0 width=$px style='width:${w}pt;table-layout:fixed'>" +
                 "<tr><td width=$px style='width:${w}pt;padding:0cm 0cm 0cm 0cm'>$tablo</td></tr></table>"
    }
    if ($s) { $tablo = "<p class=MsoNormal style='margin-top:${s}pt'><span style='font-size:1.0pt'>&nbsp;</span></p>$tablo" }
    $html = $html.Substring(0, $a) + $tablo + $html.Substring($b)
    return $html.Replace("<head>", "<head><style>@page { size: A4; margin: 20mm 18mm; }</style>")
}

function Bas([string]$html, [string]$pdf) {
    $h = "$pdf.html"
    [IO.File]::WriteAllText($h, $html, $utf8)
    $uri = ([Uri](Resolve-Path $h).Path).AbsoluteUri
    $p = Start-Process -FilePath $edge -PassThru -WindowStyle Hidden -ArgumentList @(
        "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--no-first-run",
        "--user-data-dir=`"$is\edge`"", "--print-to-pdf=`"$pdf`"", $uri)
    if (-not $p.WaitForExit(90000)) { $p.Kill(); throw "Edge yanıt vermedi: $pdf" }
    if (-not (Test-Path $pdf)) { throw "Edge PDF üretmedi: $pdf" }
}

function Donustur([string]$pdf, [string]$cikti) {
    New-Item -ItemType Directory -Path $cikti -Force | Out-Null
    $p = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden -ArgumentList @("--test", "`"$pdf`"", "`"$cikti`"")
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw "SwiftPdf yanıt vermedi: $pdf" }
    $dokum = Join-Path $cikti "dokum.txt"
    $satirlar = if (Test-Path $dokum) { [IO.File]::ReadAllLines($dokum, $utf8) } else { @() }
    return [pscustomobject]@{
        Kod      = $p.ExitCode
        Cikti    = @($satirlar | Where-Object { $_.StartsWith("CIKTI: ") } | ForEach-Object { $_.Substring(7) })
        Hata     = ($satirlar | Where-Object { $_.StartsWith("HATA: ") } | Select-Object -First 1)
        Uyari    = @($satirlar | Where-Object { $_.StartsWith("UYARI: ") })
        Satirlar = @($satirlar | Where-Object { -not ($_ -match "^(CIKTI|HATA|GIRIS|UYARI): ") })
    }
}

$sonuc = New-Object System.Collections.Generic.List[object]
function Kaydet([string]$ad, [string[]]$hatalar, [string]$not = "") {
    $sonuc.Add([pscustomobject]@{ Ad = $ad; Gecti = ($hatalar.Count -eq 0); Hatalar = $hatalar })
    if ($hatalar.Count -eq 0) { Write-Host ("GEÇTİ  $ad" + $(if ($not) { "  ($not)" } else { "" })) }
    else { Write-Host "KALDI  $ad  $($hatalar[0])" -ForegroundColor Red }
}

try {
    # ---------- olumlu vakalar ----------
    foreach ($f in Get-ChildItem (Join-Path $PSScriptRoot "vakalar") -Filter *.html | Sort-Object Name) {
        $vaka = $f.BaseName
        $html = [IO.File]::ReadAllText($f.FullName, $utf8)
        $beklenen = [IO.File]::ReadAllLines((Join-Path $PSScriptRoot "beklenen\$vaka.txt"), $utf8)
        $k = $kunye.$vaka
        foreach ($d in $duzenler) {
            $ad = "${vaka}__w$($d[0])_s$($d[1])"
            if ($Filtre -and $ad -notmatch $Filtre) { continue }
            $pdf = Join-Path $is "$ad.pdf"
            Bas (Duzen $html $d[0] $d[1]) $pdf
            $r = Donustur $pdf (Join-Path $is "cikti\$ad")
            $hatalar = @()
            $not = ""
            if ($r.Kod -ne 0) { $hatalar += "çıkış kodu $($r.Kod): $($r.Hata)" }
            elseif ($r.Cikti.Count -ne 1) { $hatalar += "$($r.Cikti.Count) çıktı" }
            else {
                $fark = ""
                for ($i = 0; $i -lt [math]::Max($beklenen.Count, $r.Satirlar.Count); $i++) {
                    $x = if ($i -lt $beklenen.Count) { $beklenen[$i] } else { "<yok>" }
                    $y = if ($i -lt $r.Satirlar.Count) { $r.Satirlar[$i] } else { "<yok>" }
                    if ($x -cne $y) { $fark = "satır $($i + 1): beklenen «$($x.Trim())» / çıktı «$($y.Trim())»"; break }
                }
                $kesik = $kesikOlabilir -contains $vaka
                if ($fark -and $kesik -and $r.Uyari.Count -gt 0) { $not = "girdide kesik metin, uyarı verildi" }
                elseif ($fark) { $hatalar += $fark }
                elseif ($r.Uyari.Count -gt 0 -and -not $kesik) { $hatalar += "beklenmeyen uyarı: $($r.Uyari[0])" }

                $dosya = Split-Path -Leaf $r.Cikti[0]
                $bek = "$($k.tarih)_Kuveyt Turk SWIFT Giden Transfer $($k.ref)_($($k.tutar) $($k.doviz)).pdf"
                if ($dosya -cne $bek) { $hatalar += "dosya adı: $dosya" }
                $bayt = [IO.File]::ReadAllBytes($r.Cikti[0])
                $metin = [Text.Encoding]::ASCII.GetString($bayt)
                if (([regex]::Matches($metin, "/Type\s*/Page[^s]")).Count -gt 1) { $hatalar += "birden çok sayfa" }
            }
            Kaydet $ad $hatalar $not
        }
    }

    # ---------- olumsuz vakalar ----------
    $olumsuz = Join-Path $is "olumsuz"
    New-Item -ItemType Directory -Path $olumsuz | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $olumsuz "bos.pdf"), [byte[]]@())
    [IO.File]::WriteAllText((Join-Path $olumsuz "pdf-degil.pdf"), "Bu bir PDF değil.", $utf8)
    $swiftsiz = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "uretec\sablon.html"), $utf8).Replace("SWIFT MESAJI", "").Replace("{TABLO}", "<p>Merhaba, toplantı notları ektedir.</p>")
    Bas ($swiftsiz.Replace("<head>", "<head><style>@page { size: A4; margin: 20mm 18mm; }</style>")) (Join-Path $olumsuz "swiftsiz.pdf")
    $tam = [IO.File]::ReadAllBytes((Join-Path $olumsuz "swiftsiz.pdf"))
    [IO.File]::WriteAllBytes((Join-Path $olumsuz "bozuk.pdf"), $tam[0..([int]($tam.Length / 3))])   # yarıda kesilmiş dosya

    $beklenenMesaj = @{
        "bos"       = "PDF açılamadı"
        "pdf-degil" = "PDF açılamadı"
        "bozuk"     = "PDF açılamadı"
        "swiftsiz"  = "SWIFT mesajı bulunamadı"
    }
    foreach ($ad in $beklenenMesaj.Keys | Sort-Object) {
        if ($Filtre -and "olumsuz-$ad" -notmatch $Filtre) { continue }
        $r = Donustur (Join-Path $olumsuz "$ad.pdf") (Join-Path $is "cikti\olumsuz-$ad")
        $hatalar = @()
        if ($r.Kod -ne 2) { $hatalar += "çıkış kodu $($r.Kod) (beklenen 2): $($r.Hata)" }
        elseif ($r.Hata -notlike "*$($beklenenMesaj[$ad])*") { $hatalar += "mesaj farklı: $($r.Hata)" }
        Kaydet "olumsuz-$ad" $hatalar
    }
}
finally {
    if ($Sakla) { Write-Host "Dosyalar: $is" } else { Remove-Item -Recurse -Force $is -ErrorAction SilentlyContinue }
}

$gecen = @($sonuc | Where-Object Gecti).Count
Write-Host ""
Write-Host "TOPLAM $($sonuc.Count)  GEÇTİ $gecen  KALDI $($sonuc.Count - $gecen)"
if ($gecen -ne $sonuc.Count) { exit 1 }
