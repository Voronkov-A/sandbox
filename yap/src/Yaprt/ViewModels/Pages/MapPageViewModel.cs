using CommunityToolkit.Mvvm.ComponentModel;
using OpenTK.Mathematics;
using Yaprt.Domain;
using Yaprt.Miscellaneous.Geometry;
using Yaprt.Miscellaneous.Geometry.BoundingVolumes;

namespace Yaprt.ViewModels.Pages;

internal partial class MapPageViewModel : ViewModelBase
{
    private readonly IRouter _router;

    [ObservableProperty]
    private World _world;

    public MapPageViewModel(IRouter router)
    {
        _router = router;

        World = new World(new Vector2(256, 256));

        var firstParticipant = new Participant("first");

        World.AddObject(new Pawn(
            firstParticipant,
            new Position(new Vector3(0, 0, 0), Angle.FromRadians(0)),
            new BoundingCircle(20),
            new BoundingSector(60, Angle.FromRadians(45))));
    }

    public string Title => "Map";
}
