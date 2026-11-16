using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class AiDuplicateDialogView : UserControl, ILazyActivated
{
    public AiDuplicateDialogView() => InitializeComponent();

    public void OnLazyActivated() => Dispatcher.UIThread.Post(() => PromptTextBox.Focus(), DispatcherPriority.Loaded);

    private void Dialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.BrowserBack)) return;
        if (DataContext is MainViewModel model) model.CancelAiDuplicateDialogCommand.Execute(null);
        e.Handled = true;
    }
}
