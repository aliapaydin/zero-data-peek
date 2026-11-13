# Gizlilik Politikası — Zero Data Peek

*Son güncelleme: 13 Kasım 2026*

*(English version below.)*

Zero Data Peek ("uygulama"), Ali Apaydın tarafından geliştirilen açık kaynaklı bir dosya görüntüleyici ve düzenleyicidir.

## Kısaca
**Uygulama kişisel veri toplamaz, kimseyle paylaşmaz ve geliştiriciye hiçbir veri göndermez.** Telemetri, reklam, analiz ya da izleme yoktur.

## Bilgisayarınızda saklananlar
Aşağıdakiler yalnızca sizin bilgisayarınızda, uygulamanın kendi veri klasöründe tutulur:
- **Ayarlar:** tema, dil, sekme düzeni, yazı tipi, pencere ve panel konumları.
- **Son açılanlar ve oturum:** açtığınız dosyaların yolları (içerikleri değil).
- **Kaydedilmemiş sekmeler:** Metin ve kod sekmelerinde henüz kaydetmediğiniz içerik, uygulama kapanınca kaybolmasın diye uygulamanın veri klasöründe yedeklenir ve bir sonraki açılışta geri getirilir. Sekmeyi kaydettiğinizde ya da kapattığınızda yedeği silinir. Ayarlar → Oturum bölümünden kapatılabilir.
- **SQL geçmişi:** SQL panelinde çalıştırdığınız son sorgular.
- **Hata günlüğü:** Uygulama hata verirse teknik ayrıntılar yerel bir metin dosyasına yazılır. Otomatik olarak hiçbir yere gönderilmez.
- **Geçici veritabanı:** Veri dosyaları açıkken içerikleri, hızlı kaydırma ve sorgu için bilgisayarınızın geçici klasöründe bir çalışma dosyasında tutulur. Uygulama kapanırken silinir; silinemezse bir sonraki açılışta temizlenir.

Uygulamayı kaldırdığınızda ya da Windows Ayarlar'dan sıfırladığınızda bu veriler silinir.

## İnternet bağlantısı
Uygulama internete bağlanmaz. Tek istisna, Yardım menüsündeki ve Hakkında penceresindeki bağlantılara (GitHub sayfası, geliştiricinin sitesi) tıklamanızdır; bunlar varsayılan tarayıcınızı açar.

## Dosyalarınız
Dosyalarınız yalnızca siz açtığınızda okunur, yalnızca siz kaydettiğinizde değiştirilir ve hiçbir yere yüklenmez.

## İletişim
[GitHub Issues](https://github.com/aliapaydin/zero-data-peek/issues)

---

# Privacy Policy — Zero Data Peek

*Last updated: November 13, 2026*

Zero Data Peek is an open-source file viewer and editor developed by Ali Apaydın.

**The app collects no personal data, shares nothing, and sends nothing to the developer.** There is no telemetry, advertising, analytics or tracking.

## Stored on your computer
The following are kept only on your computer, in the app's own data folder:
- **Settings:** theme, language, tab layout, font, window and pane positions.
- **Recent files and session:** the paths of files you opened (not their contents).
- **Unsaved tabs:** content you have not yet saved in text and code tabs is backed up in the app's data folder so it is not lost when the app closes, and is restored on the next launch. The backup is deleted when you save or close the tab. This can be turned off under Settings → Session.
- **SQL history:** the most recent queries you ran in the SQL panel.
- **Error log:** if the app hits an error, technical details are written to a local text file. Nothing is sent anywhere automatically.
- **Temporary database:** while data files are open, their contents are kept in a working file in your temp folder for fast scrolling and queries. It is deleted when the app closes, or cleaned up on the next launch if that fails.

Uninstalling the app or resetting it from Windows Settings deletes this data.

## Internet access
The app does not connect to the internet. The only exception is clicking the links in the Help menu and the About window (the GitHub page, the developer's website), which open your default browser.

## Your files
Your files are read only when you open them, changed only when you save them, and never uploaded anywhere.

## Contact
[GitHub Issues](https://github.com/aliapaydin/zero-data-peek/issues)
