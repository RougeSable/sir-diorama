using System;
using System.Linq;
using Xunit;

namespace SirDiorama.Tests
{
    public class TexelGridTests
    {
        // What the shader does with the camera it receives: a world point is
        // seen at (point - camera), in floats, then put back in the frame by
        // adding the wrapped camera. Its texel must not depend on where the
        // camera is, even millions of metres from the origin.
        private static long ShaderTexel(Vec3d point, Vec3d camera, double size, double period)
        {
            var relative = (float)(point.X - camera.X);
            var wrapped = (float)TexelGrid.CameraInFrame(camera, Vec3d.Zero, Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ, period).X;
            var inFrame = relative + wrapped;
            // The same texel, up to the period.
            return Modulo(TexelGrid.TexelIndex(inFrame, size), (long)Math.Round(period / size));
        }

        private static long Modulo(long value, long count)
        {
            return ((value % count) + count) % count;
        }

        [Fact]
        public void ATexelStaysOnTheWorldWhenTheCameraMoves()
        {
            var size = 1.0 / 16;
            var period = TexelGrid.Period(size);
            var point = new Vec3d(1234567.3031, 0, 0);
            var expected = Modulo(TexelGrid.TexelIndex(point.X, size), (long)Math.Round(period / size));

            for (var step = 0; step < 200; step++)
            {
                var camera = new Vec3d(point.X - 7.0 + step * 0.0731, 3, -2);
                Assert.Equal(expected, ShaderTexel(point, camera, size, period));
            }
        }

        [Fact]
        public void ThePeriodHoldsEveryTexelSizeTheShaderUses()
        {
            foreach (var density in LookSettings.TexelDensities)
            {
                var size = 1.0 / density;
                var period = TexelGrid.Period(size);
                for (var level = 0; level <= TexelGrid.MaxLevel; level++)
                {
                    var texel = size * Math.Pow(2, level);
                    var ratio = period / texel;
                    Assert.Equal(Math.Round(ratio), ratio);
                }
                // Small enough for float precision near the camera: under a
                // millimetre of error at the period.
                Assert.True(period * 1.2e-7 < 0.001, "period " + period);
            }
        }

        [Fact]
        public void TexelEdgesFallOnBlockEdges()
        {
            // Grid frame: origin at the centre of block (0, 0, 0), so block
            // edges sit at half a block from it.
            foreach (var blockSize in new[] { 0.5, 2.5 })
            {
                foreach (var density in LookSettings.TexelDensities)
                {
                    var size = 1.0 / density;
                    var edge = blockSize / 2;
                    var ratio = edge / size;
                    Assert.Equal(Math.Round(ratio), ratio, 9);
                }
            }
        }

        [Fact]
        public void WrapIsAlwaysWithinThePeriod()
        {
            foreach (var value in new[] { -1e7, -2048.0, -0.001, 0.0, 0.001, 1024.0, 2048.0, 3.3e6 })
            {
                var wrapped = TexelGrid.Wrap(value, 1024);
                Assert.InRange(wrapped, 0, 1024 - 1e-12);
                var turns = (value - wrapped) / 1024;
                Assert.Equal(Math.Round(turns), turns, 6);
            }
        }

        [Fact]
        public void FarTexelsCoverTheSmallestSizeOnScreen()
        {
            var size = 1.0 / 16;
            var pixelAngle = 1.2 / 1080; // about 70 degrees over 1080 lines
            Assert.Equal(0, TexelGrid.Level(2, pixelAngle, 4, size));

            foreach (var distance in new[] { 10.0, 100.0, 1000.0, 20000.0 })
            {
                var level = TexelGrid.Level(distance, pixelAngle, 4, size);
                var texel = size * Math.Pow(2, level);
                Assert.True(texel >= 4 * distance * pixelAngle * 0.999, distance + " m: " + texel);
                Assert.True(level == 0 || texel / 2 < 4 * distance * pixelAngle, distance + " m: one level too many");
            }

            Assert.Equal(TexelGrid.MaxLevel, TexelGrid.Level(1e9, pixelAngle, 8, size));
        }

        [Fact]
        public void TheGravityFrameHoldsUntilTheVerticalTurns()
        {
            var frame = new GravityFrame();
            Assert.True(frame.Update(new Vec3d(0.2, 0.9, 0.1)));
            var first = frame.AxisX;

            // Unit, orthogonal axes, Y up.
            Assert.Equal(1, frame.AxisX.Length(), 9);
            Assert.Equal(1, frame.AxisY.Length(), 9);
            Assert.Equal(1, frame.AxisZ.Length(), 9);
            Assert.Equal(0, Vec3d.Dot(frame.AxisX, frame.AxisY), 9);
            Assert.Equal(0, Vec3d.Dot(frame.AxisY, frame.AxisZ), 9);
            Assert.Equal(0, Vec3d.Dot(frame.AxisZ, frame.AxisX), 9);
            Assert.Equal(1, Vec3d.Dot(Vec3d.Cross(frame.AxisX, frame.AxisY), frame.AxisZ), 9);

            // A step on the planet: the frame does not move.
            Assert.False(frame.Update(new Vec3d(0.21, 0.9, 0.1)));
            Assert.Equal(first.X, frame.AxisX.X);

            // Kilometres further: it moves, once.
            Assert.True(frame.Update(new Vec3d(0.5, 0.8, 0.1)));
            Assert.False(frame.Update(new Vec3d(0.5, 0.8, 0.1)));

            // No gravity: nothing changes.
            Assert.False(frame.Update(Vec3d.Zero));
        }

        [Fact]
        public void TheConstantsCarryTheSettings()
        {
            var values = new float[LookConstants.FloatCount];
            var settings = new LookSettings { TexelDensity = 8, SmallestTexel = 5, ColourBoost = 40 };
            LookConstants.Pack(values, Anchor.World(1.5f), new Vec3d(1, 2, 3), Vec3d.Zero,
                Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ, settings);

            Assert.Equal(new float[]
            {
                1, 0, 0, 0.125f, 0, 1, 0, 5, 0, 0, 1, 0.4f, 1, 2, 3, 1.5f,
                0, 0, 0, 0, 0, 0, 0, 0,
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 0,
            }, values.Take(LookConstants.ScreenStart).ToArray());
            // No screen.
            Assert.Equal(0, values[LookConstants.ScreenCountIndex]);
            // No grid: no point takes the grid's frame.
            Assert.False(LookConstants.TakesGridFrame(values, new Vec3d(0, 0, -1)));
        }

        // A ship flying over a planet, the player in its cockpit.
        private static readonly Vec3d ShipX = new Vec3d(0.6, 0.8, 0);
        private static readonly Vec3d ShipY = new Vec3d(-0.8, 0.6, 0);
        private static readonly Vec3d ShipZ = Vec3d.UnitZ;

        private static Anchor ShipOverPlanet(GravityFrame gravity)
        {
            // A large grid ten blocks long, one block of margin.
            var grid = new GridAnchor(7, new Vec3d(1000, 2000, 3000), ShipX, ShipY, ShipZ,
                new Vec3d(-1.25, -1.25, -11.25), new Vec3d(1.25, 3.75, 1.25), 2.5);
            return Anchor.Gravity(gravity, 0).WithGrid(grid);
        }

        private static Vec3d ToWorld(Vec3d origin, Vec3d local)
        {
            return origin + ShipX * local.X + ShipY * local.Y + ShipZ * local.Z;
        }

        [Fact]
        public void OnlyWhatLiesInTheShipsBoxTakesTheShipsFrame()
        {
            var gravity = new GravityFrame();
            gravity.Update(new Vec3d(0, 1, 0));
            var anchor = ShipOverPlanet(gravity);
            Assert.Equal(AnchorKind.Grid, anchor.Kind);
            // The surroundings keep the planet's vertical with a ship near.
            Assert.Equal(1, anchor.AroundY.Y, 9);

            var origin = anchor.Grid.Origin;
            var camera = ToWorld(origin, new Vec3d(0, 0.5, -2)); // the cockpit
            var values = new float[LookConstants.FloatCount];
            LookConstants.Pack(values, anchor, camera, origin, ShipX, ShipY, ShipZ, new LookSettings());

            // The ship's hull, and just outside it (within the margin).
            Assert.True(LookConstants.TakesGridFrame(values, ToWorld(origin, new Vec3d(1.2, 0, -5)) - camera));
            Assert.True(LookConstants.TakesGridFrame(values, ToWorld(origin, new Vec3d(0, -3.5, -5)) - camera));

            // The ground below, the asteroid ahead, another ship aside.
            Assert.False(LookConstants.TakesGridFrame(values, ToWorld(origin, new Vec3d(0, -30, -5)) - camera));
            Assert.False(LookConstants.TakesGridFrame(values, ToWorld(origin, new Vec3d(0, 0, -800)) - camera));
            Assert.False(LookConstants.TakesGridFrame(values, ToWorld(origin, new Vec3d(4.5, 0, -5)) - camera));

            // The surroundings' frame is the planet's, not the ship's.
            Assert.Equal(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, values.Skip(24).Take(12).ToArray());
        }

        [Fact]
        public void TheGroundKeepsItsTexelsWhileTheShipFlies()
        {
            var gravity = new GravityFrame();
            gravity.Update(new Vec3d(0, 1, 0));
            var size = new LookSettings().TexelSize;
            var ground = new Vec3d(12345.678, -40.21, 6789.012);

            long? expected = null;
            for (var step = 0; step < 100; step++)
            {
                // The ship, and the camera in its cockpit, move and turn.
                var anchor = ShipOverPlanet(gravity);
                var origin = new Vec3d(12000 + step * 3.7, 10 + step * 0.2, 6700 - step * 1.3);
                var camera = ToWorld(origin, new Vec3d(0, 0.5, -2));
                var values = new float[LookConstants.FloatCount];
                LookConstants.Pack(values, anchor, camera, origin, ShipX, ShipY, ShipZ, new LookSettings());

                var relative = ground - camera;
                Assert.False(LookConstants.TakesGridFrame(values, relative));

                // As the shader does it: axes of the surroundings, in floats,
                // plus the wrapped camera.
                var inFrame = (float)(values[24] * (float)relative.X + values[25] * (float)relative.Y + values[26] * (float)relative.Z)
                    + values[36];
                var period = TexelGrid.Period(size);
                var texel = Modulo(TexelGrid.TexelIndex(inFrame, size), (long)Math.Round(period / size));
                if (expected == null)
                    expected = texel;
                Assert.Equal(expected.Value, texel);
            }
        }

        [Fact]
        public void TheGridBoxCarriesItsMargin()
        {
            var grid = new GridAnchor(1, Vec3d.Zero, Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ,
                new Vec3d(-0.25, -0.25, -0.25), new Vec3d(0.25, 0.75, 0.25), 0.5);
            Assert.Equal(-0.75, grid.BoxMin.X, 9);
            Assert.Equal(1.25, grid.BoxMax.Y, 9);

            // Without gravity, the surroundings are the world.
            var anchor = Anchor.Gravity(new GravityFrame(), 0).WithGrid(grid);
            Assert.Equal(AnchorKind.Grid, anchor.Kind);
            Assert.Equal(1, anchor.AroundX.X, 9);
            Assert.Same(anchor.Grid, grid);
        }
    }
}
