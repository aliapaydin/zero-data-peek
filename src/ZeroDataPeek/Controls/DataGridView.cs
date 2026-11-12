using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Controls;

/// <summary>
/// Salt okunur veri tablosu. Satır sayısından bağımsız çalışır: yalnızca ekrana sığan satır ve kolonlar için
/// hücre oluşturur, veriyi 200 satırlık bloklar halinde ister ve önbellekte tutar. Kaydırma satır satırdır;
/// bu sayede milyarlarca satırda da kaydırma çubuğu hassasiyetini yitirmez.
/// </summary>
public sealed partial class DataGridView : UserControl
{
    private const double RowHeight = 28, HeaderHeight = 34, CellPadding = 10;
    private const double MinColumnWidth = 48, MaxAutoWidth = 420, DefaultWidth = 130;
    private const int BlockSize = 200, MaxBlocks = 80, MaxDrawnRows = 300;

    private readonly Canvas _surface = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Canvas _lineLayer = new();
    private readonly Canvas _scrollLayer = new();
    private readonly Canvas _gutterLayer = new();
    private readonly Rectangle _headerBack = new();
    private readonly Rectangle _rowHighlight = new();
    private readonly Border _cellOutline = new();
    private readonly ScrollBar _vbar;
    private readonly ScrollBar _hbar;
    private readonly List<TextBlock> _cellPool = new();
    private readonly List<TextBlock> _headerPool = new();
    private readonly List<TextBlock> _gutterPool = new();
    private readonly List<Rectangle> _rowLinePool = new();
    private readonly List<Rectangle> _separatorPool = new();
    private readonly Brush _lineBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.22 };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _loadTimer;

    private readonly Dictionary<long, string?[][]> _blocks = new();
    private readonly HashSet<long> _pending = new();
    private readonly HashSet<long> _needed = new();

    private IReadOnlyList<DataColumn> _columns = Array.Empty<DataColumn>();
    private double[] _widths = Array.Empty<double>();
    private double[] _offsets = { 0 };
    private long _rowCount;
    private Func<long, int, Task<string?[][]>>? _loader;
    private int _generation;
    private bool _autoSized;

    private long _firstRow;
    private double _hOffset;
    private int _visibleRows = 1;
    private long _selectedRow;
    private int _selectedColumn;
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _rendering;
    private double _wheelRemainder;

    private int _resizeColumn = -1;
    private double _resizeStartX, _resizeStartWidth;
    private int _pressedHeader = -1;

    public DataGridView()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;

        _vbar = new ScrollBar { Orientation = Orientation.Vertical, IndicatorMode = ScrollingIndicatorMode.MouseIndicator, SmallChange = 1 };
        _hbar = new ScrollBar { Orientation = Orientation.Horizontal, IndicatorMode = ScrollingIndicatorMode.MouseIndicator, SmallChange = 40 };
        _vbar.ValueChanged += (_, e) =>
        {
            if (_rendering) return;
            _firstRow = (long)Math.Round(e.NewValue);
            Render();
        };
        _hbar.ValueChanged += (_, e) =>
        {
            if (_rendering) return;
            _hOffset = e.NewValue;
            Render();
        };

        _headerBack.Fill = new SolidColorBrush(Colors.Gray) { Opacity = 0.12 };
        _headerBack.Height = HeaderHeight;
        _headerBack.IsHitTestVisible = false;

        var accent = AccentBrush();
        _rowHighlight.Fill = accent;
        _rowHighlight.Opacity = 0.14;
        _rowHighlight.Height = RowHeight;
        _rowHighlight.IsHitTestVisible = false;
        _cellOutline.BorderBrush = accent;
        _cellOutline.BorderThickness = new Thickness(1.5);
        _cellOutline.CornerRadius = new CornerRadius(3);
        _cellOutline.Height = RowHeight;
        _cellOutline.IsHitTestVisible = false;

        _lineLayer.IsHitTestVisible = false;
        _scrollLayer.IsHitTestVisible = false;
        _gutterLayer.IsHitTestVisible = false;
        _scrollLayer.Children.Add(_cellOutline);
        _surface.Children.Add(_headerBack);
        _surface.Children.Add(_rowHighlight);
        _surface.Children.Add(_lineLayer);
        _surface.Children.Add(_scrollLayer);
        _surface.Children.Add(_gutterLayer);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_surface);
        Grid.SetColumn(_vbar, 1);
        root.Children.Add(_vbar);
        Grid.SetRow(_hbar, 1);
        root.Children.Add(_hbar);
        Content = root;

        _loadTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _loadTimer.Interval = TimeSpan.FromMilliseconds(40);
        _loadTimer.IsRepeating = false;
        _loadTimer.Tick += (_, _) => LoadNeededBlocks();

        _surface.SizeChanged += (_, _) => Render();
        _surface.PointerPressed += Surface_PointerPressed;
        _surface.PointerMoved += Surface_PointerMoved;
        _surface.PointerReleased += Surface_PointerReleased;
        _surface.PointerCaptureLost += (_, _) => _resizeColumn = -1;
        PointerWheelChanged += Grid_PointerWheelChanged;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Loc.T("Veri tablosu"));
    }

    /// <summary>Kolon başlığına tıklandı (kolon dizini).</summary>
    public event EventHandler<int>? HeaderClicked;

    /// <summary>Seçili hücre değişti.</summary>
    public event EventHandler? CurrentCellChanged;

    /// <summary>Bir veri bloğu okunamadı (hata metni).</summary>
    public event EventHandler<string>? LoadFailed;

    public long CurrentRow => _selectedRow;

    public int CurrentColumn => _selectedColumn;

    /// <summary>Seçili hücrenin değeri; henüz yüklenmediyse ya da null ise null.</summary>
    public string? CurrentValue => RowData(_selectedRow, out _) is { } row && _selectedColumn < row.Length ? row[_selectedColumn] : null;

    /// <summary>Tabloyu yeni veriyle kurar. Kolonlar aynı kalırsa genişlikler ve yatay konum korunur.</summary>
    public void SetData(IReadOnlyList<DataColumn> columns, long rowCount, Func<long, int, Task<string?[][]>> loader)
    {
        bool sameColumns = columns.Count == _columns.Count && columns.Zip(_columns).All(p => p.First.Name == p.Second.Name);

        _generation++;
        _blocks.Clear();
        _pending.Clear();
        _needed.Clear();
        _columns = columns;
        _rowCount = Math.Max(0, rowCount);
        _loader = loader;
        _firstRow = 0;
        _selectedRow = 0;

        if (!sameColumns)
        {
            _widths = Enumerable.Repeat(DefaultWidth, columns.Count).ToArray();
            _autoSized = false;
            _hOffset = 0;
            _selectedColumn = 0;
            _sortColumn = -1;
            RecomputeOffsets();
        }
        Render();
        CurrentCellChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Başlıkta sıralama okunu gösterir (-1: sıralama yok).</summary>
    public void SetSort(int column, bool descending)
    {
        _sortColumn = column;
        _sortDescending = descending;
        Render();
    }

    // ================================================================== çizim

    private void Render()
    {
        if (_rendering) return;
        _rendering = true;
        int cells = 0, headers = 0, gutters = 0, rowLines = 0, separators = 0;
        try
        {
            double viewWidth = _surface.ActualWidth, viewHeight = _surface.ActualHeight;
            bool visible = viewWidth > 8 && viewHeight > HeaderHeight + 4 && _columns.Count > 0;
            _headerBack.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _rowHighlight.Visibility = Visibility.Collapsed;
            _cellOutline.Visibility = Visibility.Collapsed;
            _needed.Clear();
            if (!visible) return;

            int fullRows = Math.Max(1, (int)Math.Floor((viewHeight - HeaderHeight) / RowHeight));
            _visibleRows = fullRows;
            long maxFirst = Math.Max(0, _rowCount - fullRows);
            _firstRow = Math.Clamp(_firstRow, 0, maxFirst);
            int drawRows = (int)Math.Min(Math.Min(fullRows + 1, MaxDrawnRows), _rowCount - _firstRow);

            double gutterWidth = GutterWidth();
            double scrollWidth = Math.Max(0, viewWidth - gutterWidth);
            double totalWidth = _offsets[^1];
            double maxOffset = Math.Max(0, totalWidth - scrollWidth);
            _hOffset = Math.Clamp(_hOffset, 0, maxOffset);

            _surface.Clip = new RectangleGeometry { Rect = new Rect(0, 0, viewWidth, viewHeight) };
            _scrollLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, scrollWidth, viewHeight) };
            Canvas.SetLeft(_scrollLayer, gutterWidth);
            _headerBack.Width = viewWidth;

            _vbar.Maximum = maxFirst;
            _vbar.ViewportSize = fullRows;
            _vbar.LargeChange = fullRows;
            _vbar.Value = _firstRow;
            _vbar.IsEnabled = maxFirst > 0;
            _hbar.Maximum = maxOffset;
            _hbar.ViewportSize = scrollWidth;
            _hbar.LargeChange = Math.Max(40, scrollWidth - 80);
            _hbar.Value = _hOffset;
            _hbar.Visibility = maxOffset > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Satır numaraları ve satır çizgileri
            for (int r = 0; r < drawRows; r++)
            {
                long row = _firstRow + r;
                double y = HeaderHeight + r * RowHeight;

                var number = NextText(_gutterPool, ref gutters, _gutterLayer);
                number.Text = (row + 1).ToString(CultureInfo.InvariantCulture);
                number.Width = gutterWidth - 12;
                number.TextAlignment = TextAlignment.Right;
                number.Opacity = row == _selectedRow ? 1 : 0.5;
                number.FontSize = 12;
                Canvas.SetLeft(number, 0);
                Canvas.SetTop(number, y + 6);

                var line = NextRectangle(_rowLinePool, ref rowLines, _lineLayer);
                line.Width = viewWidth;
                line.Height = 1;
                Canvas.SetLeft(line, 0);
                Canvas.SetTop(line, y + RowHeight - 1);

                if (row == _selectedRow)
                {
                    _rowHighlight.Visibility = Visibility.Visible;
                    _rowHighlight.Width = viewWidth;
                    Canvas.SetTop(_rowHighlight, y);
                }
            }

            // Görünen kolonlar: başlık, ayırıcı ve hücreler
            int count = _columns.Count;
            int firstColumn = 0;
            while (firstColumn < count - 1 && _offsets[firstColumn + 1] <= _hOffset) firstColumn++;

            for (int c = firstColumn; c < count && _offsets[c] - _hOffset < scrollWidth; c++)
            {
                double x = _offsets[c] - _hOffset;
                double width = _widths[c];
                double textWidth = Math.Max(0, width - 2 * CellPadding);
                var column = _columns[c];
                bool numeric = column.IsNumeric;

                var header = NextText(_headerPool, ref headers, _scrollLayer);
                header.Text = c == _sortColumn ? column.Name + (_sortDescending ? "  ▼" : "  ▲") : column.Name;
                header.FontWeight = FontWeights.SemiBold;
                header.Width = textWidth;
                header.TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left;
                Canvas.SetLeft(header, x + CellPadding);
                Canvas.SetTop(header, 8);

                var separator = NextRectangle(_separatorPool, ref separators, _scrollLayer);
                separator.Width = 1;
                separator.Height = HeaderHeight + drawRows * RowHeight;
                Canvas.SetLeft(separator, x + width - 1);
                Canvas.SetTop(separator, 0);

                for (int r = 0; r < drawRows; r++)
                {
                    long row = _firstRow + r;
                    var cell = NextText(_cellPool, ref cells, _scrollLayer);
                    var data = RowData(row, out bool loaded);
                    string? value = data is not null && c < data.Length ? data[c] : null;
                    if (!loaded)
                    {
                        cell.Text = string.Empty;
                    }
                    else if (value is null)
                    {
                        cell.Text = "null";
                        cell.Opacity = 0.38;
                        cell.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    }
                    else
                    {
                        cell.Text = Display(value);
                        cell.Opacity = 1;
                        cell.FontStyle = Windows.UI.Text.FontStyle.Normal;
                    }
                    cell.Width = textWidth;
                    cell.TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left;
                    Canvas.SetLeft(cell, x + CellPadding);
                    Canvas.SetTop(cell, HeaderHeight + r * RowHeight + 5);
                }

                if (c == _selectedColumn && _selectedRow >= _firstRow && _selectedRow < _firstRow + drawRows)
                {
                    _cellOutline.Visibility = Visibility.Visible;
                    _cellOutline.Width = width;
                    Canvas.SetLeft(_cellOutline, x);
                    Canvas.SetTop(_cellOutline, HeaderHeight + (_selectedRow - _firstRow) * RowHeight);
                }
            }

            if (_needed.Count > 0 && !_loadTimer.IsRunning) _loadTimer.Start();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Tablo çizimi", ex);
        }
        finally
        {
            HideRest(_cellPool, cells);
            HideRest(_headerPool, headers);
            HideRest(_gutterPool, gutters);
            HideRest(_rowLinePool, rowLines);
            HideRest(_separatorPool, separators);
            _rendering = false;
        }
    }

    private static TextBlock NextText(List<TextBlock> pool, ref int used, Canvas layer)
    {
        TextBlock block;
        if (used < pool.Count)
        {
            block = pool[used];
        }
        else
        {
            block = new TextBlock
            {
                FontSize = 13,
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            pool.Add(block);
            layer.Children.Add(block);
        }
        used++;
        block.Visibility = Visibility.Visible;
        return block;
    }

    private Rectangle NextRectangle(List<Rectangle> pool, ref int used, Canvas layer)
    {
        Rectangle rectangle;
        if (used < pool.Count)
        {
            rectangle = pool[used];
        }
        else
        {
            rectangle = new Rectangle { Fill = _lineBrush, IsHitTestVisible = false };
            pool.Add(rectangle);
            layer.Children.Add(rectangle);
        }
        used++;
        rectangle.Visibility = Visibility.Visible;
        return rectangle;
    }

    private static void HideRest<T>(List<T> pool, int used) where T : UIElement
    {
        for (int i = used; i < pool.Count; i++) pool[i].Visibility = Visibility.Collapsed;
    }

    /// <summary>Hücrede tek satır gösterilecek metin: satır sonları boşluk olur, çok uzun değerler kısaltılır.</summary>
    private static string Display(string value)
    {
        if (value.Length > 300) value = value[..300] + "…";
        return value.IndexOfAny(new[] { '\r', '\n', '\t' }) < 0
            ? value
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    private double GutterWidth() =>
        Math.Max(3, _rowCount.ToString(CultureInfo.InvariantCulture).Length) * 8 + 24;

    private void RecomputeOffsets()
    {
        _offsets = new double[_widths.Length + 1];
        for (int i = 0; i < _widths.Length; i++) _offsets[i + 1] = _offsets[i] + _widths[i];
    }

    private static Brush AccentBrush()
    {
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1F, 0x6F, 0xD6));
    }

    // ================================================================== veri

    private string?[]? RowData(long row, out bool loaded)
    {
        loaded = false;
        if (row < 0 || row >= _rowCount) return null;
        long block = row / BlockSize;
        if (_blocks.TryGetValue(block, out var rows))
        {
            loaded = true;
            int index = (int)(row - block * BlockSize);
            return index < rows.Length ? rows[index] : null;
        }
        _needed.Add(block);
        return null;
    }

    private void LoadNeededBlocks()
    {
        _loadTimer.Stop();
        var loader = _loader;
        if (loader is null) return;
        foreach (long block in _needed.ToList())
        {
            if (_blocks.ContainsKey(block) || !_pending.Add(block)) continue;
            _ = LoadBlockAsync(block, loader, _generation);
        }
        _needed.Clear();
    }

    private async Task LoadBlockAsync(long block, Func<long, int, Task<string?[][]>> loader, int generation)
    {
        string?[][] rows;
        try
        {
            rows = await loader(block * BlockSize, BlockSize);
        }
        catch (Exception ex)
        {
            rows = Array.Empty<string?[]>();
            if (generation == _generation) LoadFailed?.Invoke(this, ex.Message);
        }
        if (generation != _generation) return;

        _pending.Remove(block);
        _blocks[block] = rows;
        TrimCache();
        if (!_autoSized && block == 0) AutoSize(rows);
        Render();
        if (block == _selectedRow / BlockSize) CurrentCellChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Önbellek büyüyünce görünen bölgeye en uzak blokları atar.</summary>
    private void TrimCache()
    {
        if (_blocks.Count <= MaxBlocks) return;
        long current = _firstRow / BlockSize;
        foreach (long key in _blocks.Keys.OrderByDescending(k => Math.Abs(k - current)).Take(_blocks.Count - MaxBlocks).ToList())
        {
            _blocks.Remove(key);
        }
    }

    /// <summary>Kolon genişliklerini başlığa ve ilk satırlara göre ayarlar.</summary>
    private void AutoSize(string?[][] rows)
    {
        _autoSized = true;
        for (int c = 0; c < _columns.Count; c++)
        {
            int longest = _columns[c].Name.Length + 3;
            for (int r = 0; r < rows.Length && r < 100; r++)
            {
                if (c < rows[r].Length && rows[r][c] is { } value) longest = Math.Max(longest, Math.Min(value.Length, 60));
            }
            _widths[c] = Math.Clamp(longest * 7.4 + 2 * CellPadding + 6, 64, MaxAutoWidth);
        }
        RecomputeOffsets();
    }

    // ================================================================== seçim ve gezinme

    private void Select(long row, int column, bool ensureVisible = true)
    {
        if (_rowCount == 0 || _columns.Count == 0) return;
        _selectedRow = Math.Clamp(row, 0, _rowCount - 1);
        _selectedColumn = Math.Clamp(column, 0, _columns.Count - 1);

        if (ensureVisible)
        {
            if (_selectedRow < _firstRow) _firstRow = _selectedRow;
            else if (_selectedRow >= _firstRow + _visibleRows) _firstRow = _selectedRow - _visibleRows + 1;

            double scrollWidth = Math.Max(0, _surface.ActualWidth - GutterWidth());
            double left = _offsets[_selectedColumn], right = _offsets[_selectedColumn + 1];
            if (left < _hOffset) _hOffset = left;
            else if (right > _hOffset + scrollWidth) _hOffset = Math.Max(0, right - scrollWidth);
        }
        Render();
        CurrentCellChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        bool control = IsDown(VirtualKey.Control);
        switch (e.Key)
        {
            case VirtualKey.Down:
                Select(control ? _rowCount - 1 : _selectedRow + 1, _selectedColumn);
                break;
            case VirtualKey.Up:
                Select(control ? 0 : _selectedRow - 1, _selectedColumn);
                break;
            case VirtualKey.Right:
                Select(_selectedRow, control ? _columns.Count - 1 : _selectedColumn + 1);
                break;
            case VirtualKey.Left:
                Select(_selectedRow, control ? 0 : _selectedColumn - 1);
                break;
            case VirtualKey.PageDown:
                Select(_selectedRow + _visibleRows, _selectedColumn);
                break;
            case VirtualKey.PageUp:
                Select(_selectedRow - _visibleRows, _selectedColumn);
                break;
            case VirtualKey.Home:
                Select(control ? 0 : _selectedRow, control ? _selectedColumn : 0);
                break;
            case VirtualKey.End:
                Select(control ? _rowCount - 1 : _selectedRow, control ? _selectedColumn : _columns.Count - 1);
                break;
            case VirtualKey.C when control:
                CopySelection(wholeRow: IsDown(VirtualKey.Shift));
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    /// <summary>Seçili hücreyi (ya da sekmeyle ayrılmış tüm satırı) panoya kopyalar.</summary>
    public void CopySelection(bool wholeRow)
    {
        var row = RowData(_selectedRow, out bool loaded);
        if (!loaded || row is null) return;
        string text = wholeRow
            ? string.Join('\t', row.Select(v => v ?? string.Empty))
            : (_selectedColumn < row.Length ? row[_selectedColumn] : null) ?? string.Empty;
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Pano", ex);
        }
    }

    // ================================================================== fare

    private int ColumnAt(double x)
    {
        for (int c = 0; c < _columns.Count; c++)
        {
            if (x >= _offsets[c] && x < _offsets[c + 1]) return c;
        }
        return -1;
    }

    /// <summary>Sağ kenarı bu noktaya 5 pikselden yakın olan kolon (yeniden boyutlandırma için); yoksa -1.</summary>
    private int EdgeColumnAt(double x)
    {
        for (int c = 0; c < _columns.Count; c++)
        {
            if (Math.Abs(x - _offsets[c + 1]) <= 5) return c;
        }
        return -1;
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(_surface);
        if (!point.Properties.IsLeftButtonPressed || _columns.Count == 0) return;
        var position = point.Position;
        double gutterWidth = GutterWidth();

        if (position.Y < HeaderHeight)
        {
            if (position.X < gutterWidth) return;
            double x = position.X - gutterWidth + _hOffset;
            int edge = EdgeColumnAt(x);
            if (edge >= 0)
            {
                _resizeColumn = edge;
                _resizeStartX = position.X;
                _resizeStartWidth = _widths[edge];
                _surface.CapturePointer(e.Pointer);
            }
            else
            {
                _pressedHeader = ColumnAt(x);
            }
            e.Handled = true;
            return;
        }

        long row = _firstRow + (long)((position.Y - HeaderHeight) / RowHeight);
        if (row >= _rowCount) return;
        int column = position.X < gutterWidth ? _selectedColumn : ColumnAt(position.X - gutterWidth + _hOffset);
        Select(row, column < 0 ? _selectedColumn : column, ensureVisible: false);
        e.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_surface).Position;
        if (_resizeColumn >= 0)
        {
            _widths[_resizeColumn] = Math.Clamp(_resizeStartWidth + position.X - _resizeStartX, MinColumnWidth, 2000);
            _autoSized = true;
            RecomputeOffsets();
            Render();
            return;
        }

        double gutterWidth = GutterWidth();
        bool onEdge = position.Y < HeaderHeight && position.X >= gutterWidth && EdgeColumnAt(position.X - gutterWidth + _hOffset) >= 0;
        ProtectedCursor = InputSystemCursor.Create(onEdge ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.Arrow);
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn >= 0)
        {
            _resizeColumn = -1;
            _surface.ReleasePointerCapture(e.Pointer);
            return;
        }

        int pressed = _pressedHeader;
        _pressedHeader = -1;
        if (pressed < 0) return;
        var position = e.GetCurrentPoint(_surface).Position;
        if (position.Y < HeaderHeight && ColumnAt(position.X - GutterWidth() + _hOffset) == pressed)
        {
            HeaderClicked?.Invoke(this, pressed);
        }
    }

    private void Grid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        int delta = properties.MouseWheelDelta;
        if (delta == 0) return;

        if (properties.IsHorizontalMouseWheel)
        {
            _hOffset += delta * 0.5;
        }
        else if (IsDown(VirtualKey.Shift))
        {
            _hOffset -= delta * 0.5;
        }
        else
        {
            // Dokunmatik yüzey küçük adımlar gönderir: artanı biriktir.
            _wheelRemainder -= delta / 120.0 * 3;
            long whole = (long)Math.Truncate(_wheelRemainder);
            if (whole != 0)
            {
                _firstRow += whole;
                _wheelRemainder -= whole;
            }
        }
        Render();
        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
