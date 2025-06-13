using OpenTK.Mathematics;

namespace Yaprt.Miscellaneous.Geometry;

public sealed class Position
{
    public Position(Vector3 translation, Angle rotation)
    {
        ModelMatrix = Matrix4.CreateTranslation(translation) * Matrix4.CreateRotationZ(rotation.Radians);
    }

    public Matrix4 ModelMatrix { get; }

    public Angle Rotation => Angle.FromRadians(ModelMatrix.ExtractRotation().ToEulerAngles().Z);

    public Vector3 Translation => ModelMatrix.ExtractTranslation();

    public Vector3 Scale => ModelMatrix.ExtractScale();
}
