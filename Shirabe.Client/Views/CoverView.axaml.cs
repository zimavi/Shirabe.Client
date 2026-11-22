using Avalonia;
using Avalonia.Controls;
using Shirabe.Client.Services;

namespace Shirabe.Client.Views;

public partial class CoverView : UserControl
{
    public static readonly StyledProperty<string?> ArtworkPathProperty =
        AvaloniaProperty.Register<CoverView, string?>(nameof(ArtworkPath));

    public CoverView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Full path of the image file; null shows the placeholder.</summary>
    public string? ArtworkPath
    {
        get => GetValue(ArtworkPathProperty);
        set => SetValue(ArtworkPathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Width is also set while the XAML loads, before the named parts exist.
        if (Picture is not null && (change.Property == ArtworkPathProperty || change.Property == WidthProperty))
            Refresh();
    }

    private void Refresh()
    {
        var size = double.IsNaN(Width) ? 56 : Width;
        Placeholder.Width = Placeholder.Height = Math.Round(size * 0.43);

        var picture = ArtworkBitmaps.Load(ArtworkPath, (int)(size * 2));
        Picture.Source = picture;
        Placeholder.IsVisible = picture is null;
    }
}
