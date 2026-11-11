using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Picshare.ViewModels;

namespace Picshare.Views;

public partial class PhotoPropertiesDialogView : UserControl, ILazyActivated
{
    public PhotoPropertiesDialogView() => InitializeComponent();

    public void OnLazyActivated()
        => Dispatcher.UIThread.Post(() => OkButton.Focus(), DispatcherPriority.Loaded);

    private void Dialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape or Key.BrowserBack or Key.Back)
        {
            if (DataContext is MainViewModel model) model.ClosePhotoPropertiesCommand.Execute(null);
            e.Handled = true;
        }
    }
}
