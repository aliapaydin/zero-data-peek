using Microsoft.UI.Xaml;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>
/// Kaydedilmemiş sekmelerin saklanması ve diskte değişen dosyaların fark edilmesi.
/// Saklama açıkken (Ayarlar → Oturum) kapanışta metin sekmeleri için kaydetme sorulmaz: içerikleri yedeklenir,
/// bir sonraki açılışta sekmeler değişiklikleriyle geri gelir. Çökmeye karşı yedek yarım dakikada bir de yazılır.
/// </summary>
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _backupTimer;
    private bool _backupBusy;
    private bool _backupHadEntries;
    private bool _checkingDisk;

    private void InitializeBackup()
    {
        _backupTimer = DispatcherQueue.CreateTimer();
        _backupTimer.Interval = TimeSpan.FromSeconds(30);
        _backupTimer.IsRepeating = true;
        _backupTimer.Tick += (_, _) => _ = WriteBackupsAsync();
        _backupTimer.Start();

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) _ = CheckDiskChangesAsync();
        };
    }

    /// <summary>Kaydedilmemiş metin sekmelerini yedekler. Başarılıysa true döner.</summary>
    private async Task<bool> WriteBackupsAsync()
    {
        if (!_settings.KeepUnsaved || _backupBusy) return false;
        _backupBusy = true;
        try
        {
            var entries = new List<BackupEntry>();
            foreach (var tab in _tabs.ToList())
            {
                if (!tab.IsDirty || tab.Editor is not { } editor) continue;
                entries.Add(new BackupEntry
                {
                    Title = tab.Title,
                    Path = tab.FilePath ?? string.Empty,
                    Encoding = editor.Encoding.Id,
                    Eol = editor.Eol,
                    Text = await editor.GetTextAsync(),
                });
            }
            if (entries.Count == 0 && !_backupHadEntries) return true; // yazacak ya da silecek bir şey yok
            await Task.Run(() => TabBackup.Save(entries));
            _backupHadEntries = entries.Count > 0;
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Yedekleme", ex);
            return false;
        }
        finally
        {
            _backupBusy = false;
        }
    }

    /// <summary>Açılışta: önceki oturumdan kalan kaydedilmemiş sekmeleri geri getirir.</summary>
    private async Task RestoreBackupsAsync()
    {
        if (!_settings.KeepUnsaved) return;
        var entries = await Task.Run(TabBackup.Load);
        if (entries.Count == 0) return;
        _backupHadEntries = true;

        int restored = 0;
        foreach (var entry in entries)
        {
            try
            {
                var encoding = TextFileIO.Choices.FirstOrDefault(c => c.Id == entry.Encoding) ?? TextFileIO.Utf8;
                var loaded = new LoadedText(entry.Text, encoding, entry.Eol is "\n" or "\r" ? entry.Eol : "\r\n");
                DocumentTab? tab = null;

                if (entry.Path.Length > 0 && File.Exists(entry.Path))
                {
                    await OpenFilesAsync(new[] { entry.Path }, reportMissing: false, asText: true);
                    tab = _tabs.FirstOrDefault(t => string.Equals(t.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase));
                    if (tab?.Editor is { } existing) existing.Load(loaded);
                    else tab = null;
                }
                if (tab is null)
                {
                    // Hiç kaydedilmemiş sekme ya da dosyası artık yerinde olmayan sekme: adsız olarak geri gelir.
                    var editor = CreateEditor();
                    string title = entry.Title.Length > 0 ? entry.Title : Loc.F("Adsız {0}", ++_untitledCount);
                    editor.SetFileName(title);
                    editor.Load(loaded);
                    tab = new DocumentTab(title, null, editor);
                    HookEditor(tab, editor);
                    AddTab(tab);
                }
                tab.IsDirty = true;
                restored++;
            }
            catch (Exception ex)
            {
                CrashLog.Write("Yedekten geri yükleme", ex);
            }
        }
        if (restored > 0) Notify(Loc.F("{0} kaydedilmemiş sekme geri getirildi", restored), NoticeKind.Info);
    }

    /// <summary>Pencere öne gelince: açık dosyalar başka bir programca değiştirilmiş mi.</summary>
    private async Task CheckDiskChangesAsync()
    {
        if (_checkingDisk) return;
        _checkingDisk = true;
        try
        {
            foreach (var tab in _tabs.ToList())
            {
                if (tab.FilePath is not { } path || tab.Editor is not { } editor || tab.DiskStamp is not { } known) continue;
                DateTime current;
                try
                {
                    if (!File.Exists(path)) continue;
                    current = File.GetLastWriteTimeUtc(path);
                }
                catch (Exception)
                {
                    continue;
                }
                if (current == known) continue;
                tab.DiskStamp = current; // aynı değişiklik için ikinci kez sorma

                if (tab.IsDirty)
                {
                    Activate(tab);
                    bool reload = await Dialogs.ConfirmAsync(Root, Theme, Loc.T("Dosya diskte değişti"),
                        Loc.F("\"{0}\" başka bir program tarafından değiştirildi. Yeniden yüklersen buradaki kaydedilmemiş değişikliklerin kaybolur.", tab.Title),
                        Loc.T("Yeniden yükle"));
                    if (!reload) continue;
                }

                try
                {
                    var loaded = await Task.Run(() => TextFileIO.Load(path));
                    if (loaded is null || !_tabs.Contains(tab)) continue;
                    editor.Load(loaded);
                    tab.IsDirty = false;
                    Notify(Loc.F("Diskte değişti, yeniden yüklendi: {0}", tab.Title), NoticeKind.Info);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Notify(Loc.F("Yeniden yüklenemedi: {0}", tab.Title), NoticeKind.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Disk denetimi", ex);
        }
        finally
        {
            _checkingDisk = false;
            UpdateUi();
        }
    }
}
