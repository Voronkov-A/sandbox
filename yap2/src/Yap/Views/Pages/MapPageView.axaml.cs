using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using Yap.Miscellaneous.Numerics;
using Yap.ViewModels.Pages;

namespace Yap.Views.Pages;

public partial class MapPageView : UserControl
{
    public MapPageView()
    {
        InitializeComponent();
    }

    public async void MoveTopLeftOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(0, -1));
        await CopyWorldToClipboard();
    }

    public async void MoveTopRightOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(1, -1));
        await CopyWorldToClipboard();
    }

    public async void MoveRightOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(1, 0));
        await CopyWorldToClipboard();
    }

    public async void MoveBottomRightOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(0, 1));
        await CopyWorldToClipboard();
    }

    public async void MoveBottomLeftOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(-1, 1));
        await CopyWorldToClipboard();
    }

    public async void MoveLeftOnClick(object sender, RoutedEventArgs args)
    {
        Move(new Vector2i(-1, 0));
        await CopyWorldToClipboard();
    }

    private void Move(Vector2i axialMovement)
    {
        if (DataContext is not MapPageViewModel viewModel)
        {
            return;
        }

        /*var field = viewModel.World.Map.Fields
            .First(x => x.Pawn?.PlayerIndex == viewModel.World.Map.CurrentPlayerIndex);
        viewModel.World.Map.MovePawn(field.Pawn!, field.Position.AddAxial(axialMovement));*/
    }

    private async Task CopyWorldToClipboard()
    {
        await Task.CompletedTask;
        /*var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(WorldToString());
        }*/
    }

    /*private string? WorldToString()
    {
        if (DataContext is not MapPageViewModel viewModel)
        {
            return null;
        }

        var result = viewModel.World.Map.Fields.Select(x =>
        {
            return new
            {
                q = x.Position.AxialCoordinates.X,
                r = x.Position.AxialCoordinates.Y,
                pawn = x.Pawn == null ? 0 : x.Pawn.PlayerIndex + 1
            };
        });

        return JsonSerializer.Serialize(result);
    }*/
}