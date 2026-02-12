namespace Yaprt.Domain.Visibility;

public interface IVisibleObject
{
    bool IsTransparent { get; }

    ObjectVisibilityMode VisibilityMode { get; }
}
