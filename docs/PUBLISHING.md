# Microsoft Store'da yayımlama

Zero File Commander ve Zero File Extractor ile aynı adımlar; farklar burada.

1. **Adı ayırt:** Partner Center → Uygulamalar → Yeni ürün → "Zero Data Peek".
2. **Kimliği eşle:** Ürün kimliği sayfasındaki `Package/Identity/Name` ve `Publisher` değerlerini `src/ZeroDataPeek/Package.appxmanifest` içindeki `Identity` ile karşılaştır. Şu an `AliApaydin.ZeroDataPeek` varsayıldı; Partner Center farklı bir ad verdiyse manifesti ona göre düzelt.
3. **Paketi üret:** GitHub → Actions → **store-package** → Run workflow. Çıkan `.msixupload` dosyasını Partner Center'a yükle.
4. **Sertifikasyon notları** ("Notes for certification" alanına):
   - `runFullTrust`: masaüstü uygulaması; kullanıcının seçtiği dosyaları okur ve yazar.
   - Uygulama internete bağlanmaz, veri toplamaz.
5. **Gizlilik politikası adresi:** depo herkese açık olduğunda `PRIVACY.md` bağlantısı.
