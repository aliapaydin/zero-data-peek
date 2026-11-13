using System.ComponentModel;
using Microsoft.UI.Xaml;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Models;

/// <summary>Gezgin panelindeki bir satır: klasör ya da dosya. Ağaç, girintili düz liste olarak tutulur.</summary>
public sealed partial class ExplorerItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isActive;

    public ExplorerItem(string path, bool isFolder, int depth)
    {
        FullPath = path;
        IsFolder = isFolder;
        Depth = depth;
        Name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (Name.Length == 0) Name = path;
        CanOpen = isFolder || FileKinds.CanOpen(path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public int Depth { get; }

    /// <summary>Dosya uygulamada açılabiliyor mu (klasörler için her zaman true).</summary>
    public bool CanOpen { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>Etkin sekmenin dosyası mı (ağaçta kalın gösterilir).</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public Windows.UI.Text.FontWeight NameWeight => _isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public double RowHeight => Density.ExplorerRow;
    public double FontSize => Density.ListFont;
    public double IconSize => Density.ListIcon;

    /// <summary>Derinliğe göre soldan girinti.</summary>
    public Thickness Indent => new(Depth * (Density.Compact ? 11 : 14), 0, 0, 0);

    /// <summary>Klasörlerde açma oku; dosyalarda boş.</summary>
    public string Chevron => !IsFolder ? string.Empty : _isExpanded ? "" : "";

    public string Glyph => IsFolder ? (_isExpanded ? "" : "") : FileKinds.Glyph(FileKinds.KindOf(FullPath));

    /// <summary>Açılamayan dosyalar soluk görünür.</summary>
    public double ItemOpacity => CanOpen ? 1.0 : 0.38;

    public string ToolTip => CanOpen ? FullPath : FullPath + "\n" + Loc.T("Bu dosya türü açılamıyor");
}
