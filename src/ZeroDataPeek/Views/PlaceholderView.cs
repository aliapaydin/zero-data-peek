using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZeroDataPeek.Views;

/// <summary>Henüz açılamayan dosyalar için bilgi sayfası (ikili biçimler, çok büyük dosyalar).</summary>
public sealed partial class PlaceholderView : Grid
{
    public PlaceholderView(string glyph, string title, string message)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.65,
        });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
        });
        Children.Add(panel);
    }
}
