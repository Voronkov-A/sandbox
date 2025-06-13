namespace Yap.Miscellaneous.Numerics;

public readonly record struct Vector2i
{
    public Vector2i(int x, int y)
    {
        X = x;
        Y = y;
    }

    public readonly int X;

    public readonly int Y;

    public static Vector2i operator +(Vector2i left, Vector2i right)
    {
        return new Vector2i(left.X + right.X, left.Y + right.Y);
    }
}
