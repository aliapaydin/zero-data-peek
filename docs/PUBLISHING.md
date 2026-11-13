# Microsoft Store'da yayımlama

Zero File Commander ve Zero File Extractor ile aynı adımlar. Mağaza sayfasına girilecek metinler [STORE.md](STORE.md) içinde.

## İlk yayın

1. **Adı ayırt:** Partner Center → Uygulamalar ve oyunlar → Yeni ürün → MSIX ya da PWA uygulaması → "Zero Data Peek".
2. **Kimliği eşle:** Ürün yönetimi → Ürün kimliği sayfasındaki `Package/Identity/Name`, `Package/Identity/Publisher` ve `Package/Properties/PublisherDisplayName` değerlerini `src/ZeroDataPeek/Package.appxmanifest` ile karşılaştır. Manifestte şu an `AliApaydin.ZeroDataPeek`, `CN=BA57EB18-640B-433D-88C6-EB1D10E9A905` ve `Ali Apaydin` yazıyor. Biri bile farklıysa manifesti düzelt; Store farklı değerli paketi reddeder.
3. **Paketi üret:** GitHub → Actions → **store-package** → Run workflow (dal: yayımlanacak dal). Bitince sayfanın altındaki **ZeroDataPeek-StoreUpload** dosyasını indir, içinden `.msixupload` çıkar. `v1.0.0` gibi bir etiket gönderince de kendiliğinden çalışır.
4. **Gönderimi doldur:** Fiyatlandırma (ücretsiz), Özellikler (kategori, gizlilik politikası adresi), Yaş derecelendirmesi anketi, Paketler (`.msixupload` dosyasını yükle), Mağaza listeleri (Türkçe ve İngilizce; metinler ve ekran görüntüsü listesi STORE.md'de), Sertifikasyon notları (STORE.md'nin sonunda).
5. **Gönder.** Sertifikasyon genellikle birkaç iş günü sürer; `runFullTrust` yüzünden elle inceleme yapılır.

## Sonraki sürümler

1. `ZeroDataPeek.csproj` içindeki `<Version>` ile `Package.appxmanifest` içindeki `Identity Version` değerini birlikte artır (dördüncü hane hep 0 kalır; Store onu kendine ayırır). README rozetini de güncelle.
2. store-package iş akışını çalıştır, yeni `.msixupload` dosyasını Partner Center'da yeni gönderime yükle.

## Göndermeden önce elle denenecekler

Derleme hattı yalnızca derlendiğini doğrular; şunları paketlenmiş kurulumda (Release) dene:

- Açılış, karşılama sayfası, açık ve koyu tema
- CSV, Parquet, Excel açma; SQL sorgusu; dışa aktarma
- Kod dosyası açma, düzenleme, kaydetme; kapatıp açınca kaydedilmemiş sekmenin geri gelmesi
- DOCX açma, kaydetme, PDF'e aktarma
- Dosya Gezgini'nde sağ tık → "Zero Data Peek ile aç" (kurulumdan sonra Gezgin'i yeniden başlatmak gerekebilir)
- `.zdp` oturum dosyasına çift tıklama
- İngilizce arayüz (Ayarlar → dil)
