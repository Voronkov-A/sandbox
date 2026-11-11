using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Picshare.ViewModels;
using Picshare.Views;

namespace Picshare.Tests;

public sealed partial class PhotoViewerSwipeTests
{
    [Fact]
    public void PropertiesOkButton_ClosesDialogAndCancelsPendingMetadata()
    {
        var model = CreateModel();
        model.PhotoProperties = new PhotoPropertiesViewModel("Album_00001.jpg", 40, 25);
        using var cancellation = new CancellationTokenSource();
        typeof(MainViewModel).GetField("_photoPropertiesCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, cancellation);
        var dialog = new PhotoPropertiesDialogView { DataContext = model };
        var ok = dialog.FindControl<Button>("OkButton")!;
        Assert.Equal("OK", ok.Content);
        Assert.Same(model.ClosePhotoPropertiesCommand, ok.Command);
        ok.Command!.Execute(null);
        Assert.False(model.IsPhotoPropertiesVisible);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    [InlineData(Key.BrowserBack)]
    [InlineData(Key.Back)]
    public void PropertiesKeyboardDismissal_ClosesOnlyTheDialog(Key key)
    {
        var model = CreateModel();
        model.PhotoProperties = new PhotoPropertiesViewModel("Album_00001.jpg", 40, 25);
        model.IsPhotoViewerVisible = true;
        var dialog = new PhotoPropertiesDialogView { DataContext = model };
        var args = new KeyEventArgs { Key = key };
        typeof(PhotoPropertiesDialogView).GetMethod("Dialog_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [dialog, args]);
        Assert.False(model.IsPhotoPropertiesVisible);
        Assert.True(model.IsPhotoViewerVisible);
        Assert.True(args.Handled);
    }
}
