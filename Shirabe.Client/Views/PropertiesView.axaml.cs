using Avalonia.Controls;
using Avalonia.Input;
using Shirabe.Client.ViewModels;

namespace Shirabe.Client.Views;

public partial class PropertiesView : UserControl
{
    public PropertiesView()
    {
        InitializeComponent();
        Focusable = true;
        Loaded += (_, _) => Focus(); // so that Escape works at once
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not PropertiesViewModel dialog) return;

        dialog.CancelCommand.Execute(null);
        e.Handled = true;
    }
}
