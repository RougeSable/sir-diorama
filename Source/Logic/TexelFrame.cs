using System;

namespace SirDiorama
{
    // A vector in double precision, without any dependency on the game: world
    // positions in Space Engineers reach millions of metres, far beyond what a
    // float keeps to the centimetre.
    public struct Vec3d
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d UnitX = new Vec3d(1, 0, 0);
        public static readonly Vec3d UnitY = new Vec3d(0, 1, 0);
        public static readonly Vec3d UnitZ = new Vec3d(0, 0, 1);

        public static Vec3d operator +(Vec3d a, Vec3d b) { return new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3d operator -(Vec3d a, Vec3d b) { return new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3d operator -(Vec3d a) { return new Vec3d(-a.X, -a.Y, -a.Z); }
        public static Vec3d operator *(Vec3d a, double k) { return new Vec3d(a.X * k, a.Y * k, a.Z * k); }

        public static double Dot(Vec3d a, Vec3d b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static Vec3d Cross(Vec3d a, Vec3d b)
        {
            return new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        public double Length() { return Math.Sqrt(Dot(this, this)); }

        // The unit vector, or zero for a vector too small to have a direction.
        public Vec3d Normalized()
        {
            var length = Length();
            return length > 1e-12 ? this * (1.0 / length) : Zero;
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }
    }

    // What the texels are fastened to. Texels are squares drawn on every
    // surface along the three axes of a frame; they must stay still on what
    // the player looks at. Two frames are handed to the shader:
    //
    // - The surroundings: on a planet, a frame whose Y axis points up, so that
    //   the ground carries square texels (Gravity); in space, the world axes
    //   (World). Their origin is the world's: the ground, the asteroids and
    //   everything else that does not move keep their texels, whatever flies.
    // - The grid the player is in or next to, if any (Grid). Only the points
    //   inside its box, one block of margin included, take its frame: texel
    //   edges fall exactly on block edges, as in Minecraft, and the ship's own
    //   surfaces stay still while it flies. Everything else keeps the frame of
    //   the surroundings.
    public enum AnchorKind
    {
        World = 0,
        Gravity = 1,
        Grid = 2,
    }

    // The grid part of an anchor.
    public sealed class GridAnchor
    {
        // Its render object, whose matrix the render thread reads for the
        // very frame being drawn. The origin and axes below are then only a
        // fallback, from the main thread.
        public readonly uint RenderObjectId;

        public readonly Vec3d Origin;
        public readonly Vec3d AxisX;
        public readonly Vec3d AxisY;
        public readonly Vec3d AxisZ;

        // The grid's box in its own axes (LocalAABB), margin included.
        public readonly Vec3d BoxMin;
        public readonly Vec3d BoxMax;

        public GridAnchor(uint renderObjectId, Vec3d origin, Vec3d axisX, Vec3d axisY, Vec3d axisZ,
            Vec3d boxMin, Vec3d boxMax, double margin)
        {
            RenderObjectId = renderObjectId;
            Origin = origin;
            AxisX = axisX;
            AxisY = axisY;
            AxisZ = axisZ;
            var m = new Vec3d(margin, margin, margin);
            BoxMin = boxMin - m;
            BoxMax = boxMax + m;
        }
    }

    // One anchor, chosen on the main thread and read by the render thread.
    // Immutable: it is replaced as a whole.
    public sealed class Anchor
    {
        // Grid when a grid is near, else what the surroundings are.
        public readonly AnchorKind Kind;

        // The axes of the surroundings (gravity or world); their origin is
        // the world's origin.
        public readonly Vec3d AroundX;
        public readonly Vec3d AroundY;
        public readonly Vec3d AroundZ;

        // Null when no grid is near.
        public readonly GridAnchor Grid;

        // Below this distance (metres), texels follow the camera instead: the
        // tool held in first person then keeps still texels, like the hand of
        // a Minecraft player. Zero when the camera is not a character's eyes.
        public readonly float NearLimit;

        private Anchor(AnchorKind kind, Vec3d aroundX, Vec3d aroundY, Vec3d aroundZ, GridAnchor grid, float nearLimit)
        {
            Kind = kind;
            AroundX = aroundX;
            AroundY = aroundY;
            AroundZ = aroundZ;
            Grid = grid;
            NearLimit = nearLimit;
        }

        public static Anchor World(float nearLimit)
        {
            return new Anchor(AnchorKind.World, Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ, null, nearLimit);
        }

        // The planet's vertical frame; the world's when it is not set.
        public static Anchor Gravity(GravityFrame frame, float nearLimit)
        {
            if (frame == null || !frame.IsSet)
                return World(nearLimit);
            return new Anchor(AnchorKind.Gravity, frame.AxisX, frame.AxisY, frame.AxisZ, null, nearLimit);
        }

        // The same surroundings, with a grid near.
        public Anchor WithGrid(GridAnchor grid)
        {
            if (grid == null)
                return this;
            return new Anchor(AnchorKind.Grid, AroundX, AroundY, AroundZ, grid, NearLimit);
        }

        public static readonly Anchor Default = World(0);
    }

    // On a planet, the frame whose Y axis points up. It is kept as long as
    // the vertical does not turn by more than a few degrees: a frame that
    // turned with every step would make the ground's texels crawl. It moves
    // at once, by a small step, every few kilometres of walk.
    public sealed class GravityFrame
    {
        public const double MaxTiltDegrees = 3.0;

        private static readonly double MinCosine = Math.Cos(MaxTiltDegrees * Math.PI / 180.0);

        public bool IsSet { get; private set; }
        public Vec3d AxisX { get; private set; }
        public Vec3d AxisY { get; private set; }
        public Vec3d AxisZ { get; private set; }

        // True when the frame changed.
        public bool Update(Vec3d up)
        {
            var y = up.Normalized();
            if (Vec3d.Dot(y, y) < 0.5)
                return false;

            if (IsSet && Vec3d.Dot(y, AxisY) >= MinCosine)
                return false;

            // The world axis least aligned with the vertical gives the
            // horizontal axes.
            var reference = Math.Abs(y.X) <= Math.Abs(y.Y) && Math.Abs(y.X) <= Math.Abs(y.Z) ? Vec3d.UnitX
                : Math.Abs(y.Y) <= Math.Abs(y.Z) ? Vec3d.UnitY : Vec3d.UnitZ;
            var z = Vec3d.Cross(reference, y).Normalized();
            var x = Vec3d.Cross(y, z).Normalized();

            AxisX = x;
            AxisY = y;
            AxisZ = z;
            IsSet = true;
            return true;
        }

        public void Reset()
        {
            IsSet = false;
        }
    }

    // The texel grid, as the shader draws it. A surface is cut into squares
    // of the texel size along the two anchor axes that lie best in it. Far
    // away, the size doubles as many times as needed for a texel to cover at
    // least the smallest size on screen; since sizes are powers of two, the
    // coarse grids fall exactly on the fine one.
    public static class TexelGrid
    {
        // Largest doubling the shader applies (texel size times 2^14).
        public const int MaxLevel = 14;

        // The shader works in floats, relative to the camera. The camera's
        // position in the anchor frame is handed to it modulo this period: a
        // multiple of every texel size in use, so that the grid stays exactly
        // the same, and small enough to keep float precision near the camera.
        public static double Period(double texelSize)
        {
            return texelSize * (1 << MaxLevel);
        }

        public static double Wrap(double value, double period)
        {
            var r = value - Math.Floor(value / period) * period;
            return r >= period ? r - period : r;
        }

        // A position in the axes of a frame, from its origin.
        public static Vec3d InFrame(Vec3d point, Vec3d origin, Vec3d axisX, Vec3d axisY, Vec3d axisZ)
        {
            var d = point - origin;
            return new Vec3d(Vec3d.Dot(d, axisX), Vec3d.Dot(d, axisY), Vec3d.Dot(d, axisZ));
        }

        // Camera position in the anchor frame, wrapped by the period.
        public static Vec3d CameraInFrame(Vec3d camera, Vec3d origin, Vec3d axisX, Vec3d axisY, Vec3d axisZ, double period)
        {
            var local = InFrame(camera, origin, axisX, axisY, axisZ);
            return new Vec3d(Wrap(local.X, period), Wrap(local.Y, period), Wrap(local.Z, period));
        }

        // Doubling applied at this distance for a surface facing the camera,
        // as the shader computes it (a surface seen askew counts as farther):
        // pixelAngle is the angle of one screen pixel, in radians.
        public static int Level(double distance, double pixelAngle, double smallestTexel, double texelSize)
        {
            var needed = smallestTexel * distance * pixelAngle;
            if (needed <= texelSize)
                return 0;
            var level = (int)Math.Ceiling(Math.Log(needed / texelSize, 2) - 1e-9);
            return Math.Max(0, Math.Min(MaxLevel, level));
        }

        // Index of the texel holding a coordinate, as the shader cuts it.
        public static long TexelIndex(double coordinate, double size)
        {
            return (long)Math.Floor(coordinate / size);
        }
    }

    // The shader's constant buffer, packed as ten float4 (160 bytes):
    //   0  grid AxisX.xyz, texel size (m)
    //   1  grid AxisY.xyz, smallest texel on screen (px)
    //   2  grid AxisZ.xyz, colour boost (0 to 1)
    //   3  camera in the grid frame, wrapped; near limit (m)
    //   4  grid box min, relative to the camera, in the grid axes; 1 if a grid
    //   5  grid box max, relative to the camera, in the grid axes; 0
    //   6  surroundings AxisX.xyz; 0
    //   7  surroundings AxisY.xyz; 0
    //   8  surroundings AxisZ.xyz; 0
    //   9  camera in the surroundings frame, wrapped; 0
    // Without a grid, the grid slots hold the frame of the surroundings and
    // an empty box: no point takes them.
    public static class LookConstants
    {
        public const int FloatCount = 40;
        public const int ByteSize = FloatCount * sizeof(float);

        // A frame as the shader sees it: its axes, and the camera in it.
        public struct Frame
        {
            public readonly Vec3d AxisX;
            public readonly Vec3d AxisY;
            public readonly Vec3d AxisZ;
            public readonly Vec3d Camera;

            public Frame(Vec3d axisX, Vec3d axisY, Vec3d axisZ, Vec3d camera)
            {
                AxisX = axisX;
                AxisY = axisY;
                AxisZ = axisZ;
                Camera = camera;
            }
        }

        // Everything from double precision positions: the camera and, for the
        // grid, its origin and axes in the frame being drawn.
        public static void Pack(float[] target, Anchor anchor, Vec3d camera, Vec3d gridOrigin,
            Vec3d gridX, Vec3d gridY, Vec3d gridZ, LookSettings settings)
        {
            var s = settings.Normalized();
            var period = TexelGrid.Period(s.TexelSize);
            var around = new Frame(anchor.AroundX, anchor.AroundY, anchor.AroundZ,
                TexelGrid.CameraInFrame(camera, Vec3d.Zero, anchor.AroundX, anchor.AroundY, anchor.AroundZ, period));

            if (anchor.Grid == null)
            {
                Pack(target, around, around, false, Vec3d.Zero, Vec3d.Zero, s, anchor.NearLimit);
                return;
            }

            var grid = new Frame(gridX, gridY, gridZ,
                TexelGrid.CameraInFrame(camera, gridOrigin, gridX, gridY, gridZ, period));
            var local = TexelGrid.InFrame(camera, gridOrigin, gridX, gridY, gridZ);
            Pack(target, grid, around, true, anchor.Grid.BoxMin - local, anchor.Grid.BoxMax - local, s, anchor.NearLimit);
        }

        public static void Pack(float[] target, Frame grid, Frame around, bool hasGrid, Vec3d boxMin, Vec3d boxMax,
            LookSettings settings, float nearLimit)
        {
            var s = settings.Normalized();
            Put(target, 0, grid.AxisX, (float)s.TexelSize);
            Put(target, 4, grid.AxisY, s.SmallestTexel);
            Put(target, 8, grid.AxisZ, s.ColourBoost / 100f);
            Put(target, 12, grid.Camera, Math.Max(0f, nearLimit));
            Put(target, 16, hasGrid ? boxMin : Vec3d.Zero, hasGrid ? 1f : 0f);
            Put(target, 20, hasGrid ? boxMax : Vec3d.Zero, 0f);
            Put(target, 24, around.AxisX, 0f);
            Put(target, 28, around.AxisY, 0f);
            Put(target, 32, around.AxisZ, 0f);
            Put(target, 36, around.Camera, 0f);
        }

        // What the shader decides for a point seen at this position relative
        // to the camera (world axes), with the same float arithmetic: true
        // when the point takes the grid's frame, false for the surroundings.
        public static bool TakesGridFrame(float[] constants, Vec3d position)
        {
            if (!(constants[19] > 0.5f))
                return false;
            var x = (float)position.X;
            var y = (float)position.Y;
            var z = (float)position.Z;
            for (var i = 0; i < 3; i++)
            {
                var local = constants[4 * i] * x + constants[4 * i + 1] * y + constants[4 * i + 2] * z;
                if (!(local >= constants[16 + i] && local <= constants[20 + i]))
                    return false;
            }
            return true;
        }

        private static void Put(float[] target, int index, Vec3d v, float w)
        {
            target[index] = (float)v.X;
            target[index + 1] = (float)v.Y;
            target[index + 2] = (float)v.Z;
            target[index + 3] = w;
        }
    }
}
