using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>
/// Adlandırılmış oturumlar: açık sekmeler ".zdp" dosyasına kaydedilir ve o dosya açılınca geri gelir.
/// (Ayarlardaki "son oturumu geri yükle" bundan bağımsızdır; o, kapanıştaki sekmeleri kendiliğinden hatırlar.)
/// </summary>
public sealed partial class MainWindow
{
    private string? _sessionPath;

    private string? SessionName => _sessionPath is null ? null : Path.GetFileNameWithoutExtension(_sessionPath);

    private SessionFile CollectSession()
    {
        var session = new SessionFile { Active = _active?.FilePath ?? string.Empty };
        foreach (var tab in _tabs)
        {
            if (tab.FilePath is null) continue; // kaydedilmemiş sekmeler oturuma girmez
            session.Tabs.Add(new SessionTab
            {
                Path = tab.FilePath,
                Mode = tab.Data is not null ? "data" : tab.Doc is not null ? "document" : "text",
                Sql = tab.Data is { SqlText: { Length: > 0 } sql } && !string.IsNullOrWhiteSpace(sql) ? sql : null,
            });
        }
        return session;
    }

    private async Task SaveSessionFileAsync(bool saveAs)
    {
        var session = CollectSession();
        if (session.Tabs.Count == 0)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Oturum kaydedilemedi"),
                Loc.T("Oturuma girecek kayıtlı dosya yok. Önce sekmeleri dosyaya kaydet."));
            return;
        }

        string? path = _sessionPath;
        if (saveAs || path is null)
        {
            try
            {
                var picker = new FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
                picker.FileTypeChoices[Loc.T("Zero Data Peek oturumu") + " (*.zdp)"] = new List<string> { SessionFile.Extension };
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.SuggestedFileName = SessionName ?? Loc.T("oturum");
                var file = await picker.PickSaveFileAsync();
                path = file?.Path;
            }
            catch (Exception ex)
            {
                CrashLog.Write("Oturum kaydetme seçici", ex);
                return;
            }
            if (string.IsNullOrEmpty(path)) return;
        }

        try
        {
            string target = path;
            await Task.Run(() => session.Save(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Oturum kaydedilemedi"), path + "\n\n" + ex.Message);
            return;
        }

        int skipped = _tabs.Count(t => t.FilePath is null);
        SetSession(path);
        if (skipped == 0) Notify(Loc.F("Oturum kaydedildi: {0}", path));
        else Notify(Loc.F("Oturum kaydedildi: {0} ({1} kaydedilmemiş sekme dahil edilmedi)", path, skipped), NoticeKind.Warning);
    }

    private async Task PickSessionAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add(SessionFile.Extension);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            var file = await picker.PickSingleFileAsync();
            if (file is not null && !string.IsNullOrEmpty(file.Path)) await OpenSessionAsync(file.Path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Oturum seçici", ex);
        }
    }

    /// <summary>Oturum dosyasını açar: mevcut sekmeler varsa değiştirmek mi eklemek mi istendiğini sorar.</summary>
    private async Task OpenSessionAsync(string path)
    {
        SessionFile session;
        try
        {
            session = await Task.Run(() => SessionFile.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _settings.RecentSessions = _settings.RecentSessions.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
            RefreshSessions();
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Oturum açılamadı"), path + "\n\n" + ex.Message);
            return;
        }

        if (_tabs.Count > 0)
        {
            var dialog = Dialogs.Create(Root, Theme, Loc.F("\"{0}\" oturumu", session.Name),
                new TextBlock { Text = Loc.T("Açık sekmeler kapatılıp yerine oturumdakiler mi gelsin, yoksa oturum mevcut sekmelerin yanına mı eklensin?"), TextWrapping = TextWrapping.Wrap },
                Loc.T("Sekmeleri değiştir"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
            dialog.SecondaryButtonText = Loc.T("Yanına ekle");
            var choice = await Dialogs.ShowAsync(dialog);
            if (choice == ContentDialogResult.None) return;
            if (choice == ContentDialogResult.Primary)
            {
                await CloseManyAsync(_tabs.ToList());
                if (_tabs.Count > 0) return; // kaydedilmemiş değişiklik sorusunda vazgeçildi
            }
        }

        var missing = new List<string>();
        foreach (var tab in session.Tabs)
        {
            if (!File.Exists(tab.Path))
            {
                missing.Add(tab.Path);
                continue;
            }
            await OpenFilesAsync(new[] { tab.Path }, reportMissing: false, asText: tab.Mode == "text" && DataEngine.Supports(tab.Path));
            if (!string.IsNullOrWhiteSpace(tab.Sql) &&
                _tabs.FirstOrDefault(t => string.Equals(t.FilePath, tab.Path, StringComparison.OrdinalIgnoreCase))?.Data is { } data)
            {
                data.SqlText = tab.Sql; // sorgu panele yazılır; çalıştırmak kullanıcıya kalır
            }
        }

        var active = _tabs.FirstOrDefault(t => string.Equals(t.FilePath, session.Active, StringComparison.OrdinalIgnoreCase));
        if (active is not null) Activate(active);
        SetSession(path);
        if (missing.Count == 0) Notify(Loc.F("Oturum açıldı: {0}", session.Name), NoticeKind.Info);

        if (missing.Count > 0)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Bazı dosyalar bulunamadı"),
                Loc.T("Oturumdaki şu dosyalar taşınmış ya da silinmiş:") + "\n\n" + string.Join("\n", missing.Take(15)));
        }
    }

    private void SetSession(string? path)
    {
        _sessionPath = path;
        if (path is not null) _settings.AddRecentSession(path);
        RefreshSessions();
        UpdateUi();
    }

    /// <summary>Oturum menüsündeki ve karşılama sayfasındaki son oturumlar listesi.</summary>
    private void RefreshSessions()
    {
        var recent = _settings.RecentSessions;
        MenuRecentSessions.Items.Clear();
        SessionPanel.Children.Clear();
        SessionHeader.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MenuSessionClose.IsEnabled = _sessionPath is not null;

        if (recent.Count == 0)
        {
            MenuRecentSessions.Items.Add(new MenuFlyoutItem { Text = Loc.T("(boş)"), IsEnabled = false });
            return;
        }
        foreach (var path in recent)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var item = new MenuFlyoutItem { Text = name };
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => _ = OpenSessionAsync(path);
            MenuRecentSessions.Items.Add(item);

            var link = new HyperlinkButton { Content = name, Padding = new Thickness(6, 4, 6, 4) };
            ToolTipService.SetToolTip(link, path);
            link.Click += (_, _) => _ = OpenSessionAsync(path);
            SessionPanel.Children.Add(link);
        }
    }
}
