using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Yap.Domain;
using Yap.Miscellaneous.Numerics;

namespace Yap.ViewModels.Pages;

internal partial class MapPageViewModel : ViewModelBase
{
    private readonly IRouter _router;

    [ObservableProperty]
    private World _world;

    public MapPageViewModel(IRouter router)
    {
        _router = router;

        var random = new Random();
        var map = new MapGenerator(random).Generate(new Vector2i(15, 15), playerCount: 2);

        World = new World(map);

        /*map.AddPawn(new Pawn(0), new HexagonalPosition(0, 0));
        map.AddPawn(new Pawn(1), new HexagonalPosition(10, 10));*/
    }

    public string Title => "Map";
}
