# SWIFT PDF

Kuveyt Türk'ün **"Giden Döviz Transfer İşleminiz"** bildirim mailindeki SWIFT mesajını, tek sayfa A4 bir PDF'e
çeviren küçük bir Windows programı. Çıktıda **yalnız ham SWIFT metni** bulunur: bankanın giriş cümlesi, mobil şube
yönlendirmesi ve uyarı paragrafları basılmaz.

*English: a small Windows tool that turns the SWIFT message in Kuveyt Türk's "outgoing FX transfer" e-mail into a
single-page A4 PDF containing only the raw SWIFT text.*

## Nasıl kullanılır

1. Outlook'ta Kuveyt Türk'ün "Giden Döviz Transfer İşleminiz" mailini açın.
2. Acrobat sekmesinden **"Adobe PDF'e dönüştür"** deyip kaydedin.
3. SWIFT PDF'i açın ve o PDF'i pencereye bırakın (ya da "Dosya seç…"). Birden çok PDF birlikte bırakılabilir.
4. Tek sayfa SWIFT PDF'i girdinin bulunduğu klasöre kaydedilir:
   `YYYY-MM-DD_Kuveyt Turk SWIFT Giden Transfer <referans>_(<tutar> <döviz>).pdf`

Program "Adobe PDF'e dönüştür"ün ürettiği **PDF Portföyü**nü de, doğrudan mailin PDF'ini de okur. Bir portföyde
birden çok SWIFT maili varsa her biri için ayrı PDF üretir.

## Nasıl çalışır

- PDF, [PdfPig](https://github.com/UglyToad/PdfPig) ile okunur; portföyse içindeki mail PDF'i alınır.
- Mailde SWIFT metni üç sütunlu bir tabloda durur (etiket, ":", değer). Dar sütunda alt satıra kayan etiketler
  ("Financial Institution Identification") ve değerler harflerin konumlarına bakılarak yeniden birleştirilir;
  satırlar bankanın SWIFT metnindeki düzene döner.
- PDF, Windows'taki Microsoft Edge **WebView2** motoruyla basılır. İnternete bağlanılmaz; her şey bilgisayarda olur.

## Gereksinimler

- Windows 10 (1809) ya da Windows 11, 64 bit
- Microsoft Edge WebView2 Runtime (Windows 11'de hazır gelir; Windows 10'da Edge ile birlikte kurulur)

Kurulum paketi .NET'i içinde taşır; ayrıca .NET kurmak gerekmez.

## Derleme

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" setup\SwiftPdf.iss
```

Sınama kipi (pencere açmaz): `SwiftPdf.exe --test girdi.pdf cikti-klasoru` → çıktı PDF'i ve satır dökümü
`dokum.txt`. `SWIFTPDF_DEBUG=1` ortam değişkeniyle harf konumları `ayiklama.txt`'ye yazılır.

## Sınama

```powershell
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File tests\run.ps1
```

`tests\vakalar\` içindeki uydurma mailler (bütün veriler hayalidir) dört sayfa düzeninde (serbest, 370, 320 ve 280 pt
genişlik; biri sayfa sınırına denk gelir) Microsoft Edge ile PDF'e basılır, program `--test` kipinde çalıştırılır ve
satırlar `tests\beklenen\` ile birebir karşılaştırılır. Olumsuz vakalar (boş, bozuk, PDF olmayan dosya, SWIFT'siz
mail) anlaşılır bir uyarı beklemelidir. Vakaları yeniden üretmek için: `py -3 tests\uretec\uret.py`.

## Bilinen sınır

Boşluksuz çok uzun bir değer (ör. 60 harflik bitişik açıklama) mail tablosunu sayfadan taşırırsa, PDF'e çeviren
program sayfa kenarındaki harfleri hiç yazmayabilir; o harfler PDF'te olmadığı için geri getirilemez. Program bu
belirtiyi görünce sonuç ekranında uyarır.

## Not

Bu proje Kuveyt Türk Katılım Bankası ile bağlantılı değildir. Program bankanın gönderdiği bildirimin içeriğini
değiştirmez; yalnız SWIFT metnini yeniden sayfaya dizer. Depoda gerçek SWIFT mesajı ya da hesap bilgisi bulunmaz.

## Lisans

MIT. Üçüncü taraf bileşenler: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
