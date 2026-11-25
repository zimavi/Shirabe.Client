using Avalonia.Controls;
using Avalonia.Interactivity;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class AudioSettingsView : UserControl
{
    public AudioSettingsView()
    {
        InitializeComponent();
    }

    // Devices come and go: look again each time the page is opened.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as AudioSettingsViewModel)?.RefreshCommand.Execute(null);
    }
}
