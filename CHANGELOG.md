# Değişiklikler

## 1.1.0 — 2026-10-13

- Program içi güncelleme (GitHub Releases): açılışta en çok 12 saatte bir denetim, pencerenin üstünde tek satırlık
  "Yeni sürüm var" şeridi, kullanıcı onayı olmadan indirme ve kurma yok. İndirilen dosya `SHA256SUMS.txt` ile
  doğrulanır; kurulum sessiz çalışır, program kapanıp yeni sürümle açılır. Taşınabilir exe kendini değiştirir
  (Türkçe harfli klasör yollarında da).
- Güncelleme ekranı pencerenin içinde: notlar kendi alanında kaydırılır, düğmeler alta sabit; küçük ekranda ve
  yüksek ölçekte de görünür kalır. Notlar GitHub sürüm açıklamasından değil `SURUM-NOTLARI.md`'den okunur.
- "Güncellemeleri denetle" ve sürüm bilgisi pencerenin altında; güncellemeden sonraki ilk açılışta bilgi şeridi.
- Pencere açılışta ekranın çalışma alanına sığdırılır.
- Kurulum hep kullanıcıya özel (yönetici istemez); sessiz kurulumdan sonra program kendiliğinden açılır.
- GitHub Actions: derleme, testler (`tests\run.ps1`), kurulum paketi, `SHA256SUMS.txt` ve sürüm; etiket ile csproj
  sürümü ve sürüm notu denetlenir.

## 1.0.1 — 2026-10-13

Kapsamlı sınama sonrası düzeltmeler (gerçek mailler ve uydurma vakalar; Edge, Word ve Adobe PDFMaker çıktıları,
portföyler ve bozuk dosyalarla toplam 298 girdi).

- Dar basımda ikiye bölünen bölüm ayraçları (`---- Message Header ----` + alt satırda `------`) tanınır.
- Etiket kaymaları bilinen ISO 20022 (pacs.008) eleman adları sözlüğüyle birleştirilir: "FI To FI Customer Credit"
  + "Transfer" birleşir, "Swift INPUT FIN …" + "From" ayrı kalır.
- Dikeyde ortalanmış hücreler: değeri birkaç satıra bölünmüş satırlarda etiketin üstünde kalan değer parçası doğru
  satıra bağlanır; ":" ayrı satıra düşse de, etiket üç satıra bölünse de satır doğru kurulur.
- Etikete bitişik ":" ("Message Identification: 1…") ile değerin içindeki ":" ("17T13:10:44") karışmaz.
- "(" ile başlayan açıklama yalnız BICFI altındaysa banka adı sayılır.
- Word çizimindeki harf üstüne binen boşluklar sözcükleri bölmez.
- Sayfa sınırına denk gelen satırlar doğru birleşir (sayfalar tek dikey eksende okunur).
- Çok uzun satır 5 puntonun altına inmeden değer sütununa hizalı alt satıra kaydırılır; yine sığmazsa uyarı verilir.
- Girdide sayfa kenarında kesilmiş satır belirtisi varsa sonuç ekranında uyarı gösterilir.
- Birden çok PDF birlikte pencereye ya da exe simgesinin üstüne bırakılabilir; olmayan dosyalar ayrıca listelenir.
- Girdinin klasörüne yazılamazsa çıktı `Belgeler\SWIFT PDF` klasörüne kaydedilir.
- Boş, bozuk, parolalı, PDF olmayan ve yalnız resim içeren dosyalar için anlaşılır uyarılar.
- `tests\`: uydurma vakalar ve `run.ps1` sınama betiği.

## 1.0.0 — 2026-10-13

- İlk sürüm: Outlook'un "Adobe PDF'e dönüştür" çıktısından (PDF Portföyü ya da düz PDF) Kuveyt Türk SWIFT mesajını
  okuyup yalnız ham SWIFT metnini içeren tek sayfa A4 PDF üretir.
- Sürükle bırak ve "Dosya seç…"; sonuçta tarih, tutar, alıcı ve referans gösterilir.
- Sınama kipi: `--test girdi.pdf cikti-klasoru`.
