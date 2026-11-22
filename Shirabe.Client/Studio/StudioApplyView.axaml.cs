using Avalonia.Controls;
using Avalonia.Input;

namespace Shirabe.Client.Studio;

public partial class StudioApplyView : UserControl
{
    public StudioApplyView()
    {
        InitializeComponent();
        Focusable = true;
        Loaded += (_, _) => Focus(); // so that Escape works at once
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not StudioApplyViewModel dialog) return;

        dialog.CloseCommand.Execute(null);
        e.Handled = true;
    }
}
