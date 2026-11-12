# Tasarım notları

Zero Data Peek, [Zero File Commander](https://github.com/aliapaydin/zero-file-commander) ve [Zero File Extractor](https://github.com/aliapaydin/Zero-File-Extractor) ile aynı ailenin üyesi. Üçü yan yana açıldığında tek bir ürünün pencereleri gibi durmalı.

## Ortak görsel dil

- **Zemin:** Mica. İçerik, 10 px köşeli kart yüzeylerde (`CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`).
- **Yerleşim sırası:** başlık çubuğu (sekmeler) → uygulama menüsü → içerik → durum çubuğu.
- **Simgeler:** Segoe Fluent Icons.
- **Hareket:** animasyon yok; hızlı hissettirmek her şeyden önemli.

## Sekmeler

- **Yatay:** sekme şeridi başlık çubuğunun içinde. Seçili sekme, altındaki menü bandıyla aynı yüzeyde birleşir. Sekmelerin sağındaki boşluk pencereyi sürükler.
- **Dikey:** solda boyutlandırılabilir kart içinde liste. Başlık çubuğu etkin dosyanın adını gösterir.
- İki görünüm de aynı sekme listesinden beslenir; kaydedilmemiş sekmenin başında `●` olur.

## Renkler

| Ad | Hex | Kullanım |
|---|---|---|
| Zero Mavi | `#1F6FD6` | Logo zemini, vurgu |
| Sinyal Amber | `#F2B84B` | Logodaki veri satırları, uyarılar |
| Gece | `#0D1016` | Karanlık tema zemini |
| Kâğıt | `#EEF1F5` | Aydınlık tema zemini |

Uygulama içinde mümkün olduğunca WinUI tema kaynakları kullanılır; sistem vurgu rengi ve yüksek karşıtlık modu kendiliğinden çalışır.

## Logo

Ailenin sıfırı; içinde amber üç veri satırı: sıfırın içinden veriye bakıyorsun.

- Ana: `assets/brand/zdp-logo.svg`
- 16–32 px için: `assets/brand/zdp-logo-small.svg`

## İlkeler

1. **Önce bak.** Dosya, türüne uygun görünümde hemen açılır; düzenleme ikinci adım.
2. **Zarar verme.** Kodlama ve satır sonu korunur; ikili dosya metin olarak açılmaz; yazılamayan karakter sessizce bozulmaz.
3. **Tek pencere.** Her dosya bir sekme; dışarıdan açılanlar da aynı pencereye gelir.
4. **Klavye önce gelir.** Her komut komut paletinde, sık kullanılanların kısayolu var.
