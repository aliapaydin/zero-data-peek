using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>Ana pencerenin belge modu (zengin metin) işlemleri: yeni belge, açma, kaydetme, dışa aktarma, yazdırma.</summary>
public sealed partial class MainWindow
{
    private int _untitledDocuments;

    private RichDocumentView CreateDocumentView()
    {
        var view = new RichDocumentView(_editorShortcuts);
        view.ApplySettings(_settings);
        view.ShortcutPressed += (_, id) => Run(id);
        return view;
    }

    private void HookDocument(DocumentTab tab, RichDocumentView view)
    {
        view.Modified += (_, _) => tab.IsDirty = true;
        view.StatusChanged += (_, _) =>
        {
            if (tab == _active) UpdateStatus();
        };
    }

    private void NewDocument()
    {
        var view = CreateDocumentView();
        _untitledDocuments++;
        var tab = new DocumentTab(Loc.F("Belge {0}", _untitledDocuments), null, view);
        HookDocument(tab, view);
        AddTab(tab);
        _ = LoadDocumentAsync(tab, view, null);
    }

    /// <summary>Sekme eklendikten sonra içeriği yükler (düzenleyici sayfası görünür olunca hazırlanır).</summary>
    private async Task LoadDocumentAsync(DocumentTab tab, RichDocumentView view, string? path)
    {
        try
        {
            await view.LoadAsync(path);
            tab.IsDirty = false;
            if (view.Losses.Count > 0 && tab == _active)
            {
                await Dialogs.MessageAsync(Root, Theme, Loc.T("Belgenin bir kısmı gösterilemiyor"),
                    Loc.F("Bu belgede düzenleyicinin taşıyamadığı öğeler var: {0}.", DescribeLosses(view.Losses)) + "\n\n" +
                    Loc.T("Belgeyi buradan aynı dosyanın üzerine kaydedersen bu öğeler kaybolur. Özgün dosyayı korumak için \"Farklı kaydet\" kullan."));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge açma", ex);
            if (_tabs.Contains(tab))
            {
                await Dialogs.MessageAsync(Root, Theme, Loc.T("Dosya açılamadı"), (path ?? tab.Title) + "\n\n" + ex.Message);
                await CloseTabAsync(tab, prompt: false);
            }
        }
    }

    private static string DescribeLosses(IReadOnlyList<string> losses)
    {
        var names = losses.Select(l => l switch
        {
            "headers" => Loc.T("üstbilgi ve altbilgi"),
            "footnotes" => Loc.T("dipnotlar"),
            "comments" => Loc.T("yorumlar"),
            "textboxes" => Loc.T("metin kutuları ve şekiller"),
            _ => Loc.T("gömülü nesneler"),
        }).Distinct();
        return string.Join(", ", names);
    }

    /// <summary>Belge sekmesini DOCX olarak kaydeder. Kaydedilemezse ya da vazgeçilirse false döner.</summary>
    private async Task<bool> SaveDocumentAsync(DocumentTab tab, RichDocumentView view, bool saveAs)
    {
        string? path = tab.FilePath;
        if (!saveAs && path is not null && view.Losses.Count > 0)
        {
            bool overwrite = await Dialogs.ConfirmAsync(Root, Theme, Loc.T("Üzerine kaydedilsin mi?"),
                Loc.F("Kaydedince şu öğeler belgeden silinir: {0}.", DescribeLosses(view.Losses)) + "\n\n" +
                Loc.T("Özgün dosyayı korumak için vazgeçip \"Farklı kaydet\" kullanabilirsin."), Loc.T("Üzerine kaydet"));
            if (!overwrite) return false;
        }

        if (saveAs || path is null)
        {
            Activate(tab);
            path = await PickDocumentPathAsync(tab, export: false);
            if (path is null) return false;
        }

        try
        {
            await view.SaveAsync(path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge kaydetme", ex);
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Kaydedilemedi"), path + "\n\n" + ex.Message);
            return false;
        }

        view.ClearLosses();
        tab.FilePath = Path.GetFullPath(path);
        tab.IsDirty = false;
        tab.TouchDiskStamp();
        _settings.AddRecent(tab.FilePath!);
        RefreshRecent();
        UpdateUi();
        Notify(Loc.F("Kaydedildi: {0}", tab.Title));
        return true;
    }

    /// <summary>Belgeyi PDF, HTML ya da düz metin olarak dışa aktarır (sekmenin kendi dosyası değişmez).</summary>
    private async Task ExportDocumentAsync(DocumentTab tab, RichDocumentView view)
    {
        string? path = await PickDocumentPathAsync(tab, export: true);
        if (path is null) return;
        try
        {
            await view.SaveAsync(path);
            Notify(Loc.F("Dışa aktarıldı: {0}", path));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge dışa aktarma", ex);
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Dışa aktarılamadı"), path + "\n\n" + ex.Message);
        }
    }

    private async Task<string?> PickDocumentPathAsync(DocumentTab tab, bool export)
    {
        try
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            if (export)
            {
                picker.FileTypeChoices["PDF (*.pdf)"] = new List<string> { ".pdf" };
                picker.FileTypeChoices["HTML (*.html)"] = new List<string> { ".html" };
                picker.FileTypeChoices[Loc.T("Metin dosyası") + " (*.txt)"] = new List<string> { ".txt" };
            }
            else
            {
                picker.FileTypeChoices[Loc.T("Word belgesi") + " (*.docx)"] = new List<string> { ".docx" };
            }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = tab.FilePath is null ? tab.Title : Path.GetFileNameWithoutExtension(tab.FilePath);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge kaydetme seçici", ex);
            return null;
        }
    }

    /// <summary>Etkin sekmenin yakınlaştırmasını değiştirir (kod ya da belge). delta 0 ise %100'e döner.</summary>
    private void ZoomActive(int delta)
    {
        if (_active?.Editor is { } editor)
        {
            if (delta == 0) editor.SetZoom(100);
            else editor.ZoomBy(delta);
        }
        else if (_active?.Doc is { } document)
        {
            if (delta == 0) document.SetZoom(100);
            else document.ZoomBy(delta);
        }
    }
}
