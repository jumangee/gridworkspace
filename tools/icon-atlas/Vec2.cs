using System;

namespace Vgs.IconAtlasBuilder
{
    /// <summary>
    /// Minimal 2D vector for the build tool.
    ///
    /// The tool deliberately does not reference the game's <c>VRage.Math.dll</c>: it runs on a
    /// build machine, not inside Space Engineers, and a build tool that only works where the
    /// game is installed is a build tool that stops working on a CI box.
    /// </summary>
    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Vec2 operator *(Vec2 a, float k)
        {
            return new Vec2(a.X * k, a.Y * k);
        }

        public float LengthSquared
        {
            get { return X * X + Y * Y; }
        }

        public static Vec2 Mid(Vec2 a, Vec2 b)
        {
            return new Vec2((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
        }

        public override string ToString()
        {
            return X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Axis-aligned bounding box of an icon's ink, in viewBox units.</summary>
    public struct InkBox
    {
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public float Width
        {
            get { return MaxX - MinX; }
        }

        public float Height
        {
            get { return MaxY - MinY; }
        }
    }
}
