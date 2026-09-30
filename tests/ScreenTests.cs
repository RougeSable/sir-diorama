using System.Linq;
using Xunit;

namespace SirDiorama.Tests
{
    // The LCD screens near the player keep the game's own pixels, so that
    // their text reads exactly as without the plugin. What the shader decides
    // is reproduced by LookConstants.CoversScreen, with the same floats.
    public class ScreenTests
    {
        // A ship, turned, far from the world's origin (float precision).
        private static readonly Vec3d ShipX = new Vec3d(0.6, 0.8, 0);
        private static readonly Vec3d ShipY = new Vec3d(-0.8, 0.6, 0);
        private static readonly Vec3d ShipZ = Vec3d.UnitZ;
        private static readonly Vec3d Origin = new Vec3d(3000000.5, -1200000.25, 450000.75);

        // A large LCD panel on a wall of the ship, facing +Z: its screen is a
        // flat 2.4 m square, 5 cm in front of the wall at z = -1.25.
        private static readonly Vec3d ScreenMin = new Vec3d(-1.2, -1.2, -1.2);
        private static readonly Vec3d ScreenMax = new Vec3d(1.2, 1.2, -1.2);

        private static Vec3d ToWorld(Vec3d local)
        {
            return Origin + ShipX * local.X + ShipY * local.Y + ShipZ * local.Z;
        }

        private static float[] Pack(Vec3d camera, params ScreenBox[] screens)
        {
            var values = new float[LookConstants.FloatCount];
            LookConstants.Pack(values, Anchor.World(0), camera, Vec3d.Zero, Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ,
                new LookSettings());
            var set = ScreenSet.Nearest(screens);
            var count = 0;
            foreach (var s in set.Screens)
            {
                if (LookConstants.PackScreen(values, count, s.GridOrigin, s.GridX, s.GridY, s.GridZ, s.Min, s.Max, camera))
                    count++;
            }
            LookConstants.SetScreenCount(values, count);
            return values;
        }

        private static ScreenBox Panel(double distance = 3)
        {
            return new ScreenBox(7, Origin, ShipX, ShipY, ShipZ, ScreenMin, ScreenMax, distance);
        }

        private static bool Covers(float[] values, Vec3d camera, Vec3d local)
        {
            return LookConstants.CoversScreen(values, ToWorld(local) - camera);
        }

        [Fact]
        public void TheScreenKeepsTheGamePixelsAndNothingAroundIt()
        {
            // The player reads the panel from two metres.
            var camera = ToWorld(new Vec3d(0.3, 0.2, 0.8));
            var values = Pack(camera, Panel());
            Assert.Equal(1, values[LookConstants.ScreenCountIndex]);

            // Its text, up to its corners.
            Assert.True(Covers(values, camera, new Vec3d(0, 0, -1.2)));
            Assert.True(Covers(values, camera, new Vec3d(-1.19, 1.19, -1.2)));
            Assert.True(Covers(values, camera, new Vec3d(1.19, -1.19, -1.2)));

            // The wall beside it, the floor below it, the air before it.
            Assert.False(Covers(values, camera, new Vec3d(1.6, 0, -1.25)));
            Assert.False(Covers(values, camera, new Vec3d(0, -1.5, -0.5)));
            Assert.False(Covers(values, camera, new Vec3d(0.2, 0.1, 0)));
        }

        [Fact]
        public void TheScreenIsReadableFromAfar()
        {
            // Sixty metres away, askew: the text still keeps the game's pixels.
            var camera = ToWorld(new Vec3d(20, 5, 55));
            var values = Pack(camera, Panel(60));
            Assert.True(Covers(values, camera, new Vec3d(0.5, 0.5, -1.2)));
            Assert.False(Covers(values, camera, new Vec3d(3, 0.5, -1.25)));
        }

        [Fact]
        public void WhatIsSeenThroughASeeThroughScreenKeepsItsPixelsToo()
        {
            var camera = ToWorld(new Vec3d(0, 0, 2));
            var values = Pack(camera, Panel());
            // Behind the screen, along a view that crosses it.
            Assert.True(Covers(values, camera, new Vec3d(0.1, 0.1, -6)));
            // Behind the wall, along a view that misses it.
            Assert.False(Covers(values, camera, new Vec3d(8, 0, -6)));
        }

        [Fact]
        public void WithTheNoseOnTheScreenTheRestOfTheViewKeepsItsTexels()
        {
            // The camera inside the screen's thin box.
            var camera = ToWorld(new Vec3d(0, 0, -1.19));
            var values = Pack(camera, Panel(0));
            Assert.True(Covers(values, camera, new Vec3d(0.3, 0.3, -1.2)));
            Assert.False(Covers(values, camera, new Vec3d(0, 0, -6)));
            Assert.False(Covers(values, camera, new Vec3d(0, -3, 3)));
        }

        [Fact]
        public void WithoutScreensNothingIsLeftAlone()
        {
            var camera = ToWorld(new Vec3d(0, 0, 2));
            var values = Pack(camera);
            Assert.Equal(0, values[LookConstants.ScreenCountIndex]);
            Assert.False(Covers(values, camera, new Vec3d(0, 0, -1.2)));
            Assert.Same(ScreenSet.Empty, ScreenSet.Nearest(null));
        }

        [Fact]
        public void OnlyTheNearestScreensAreKept()
        {
            var many = Enumerable.Range(0, 40)
                .Select(i => new ScreenBox(1, Origin, ShipX, ShipY, ShipZ, ScreenMin, ScreenMax, 40 - i))
                .ToArray();
            var set = ScreenSet.Nearest(many);
            Assert.Equal(LookConstants.MaxScreens, set.Screens.Length);
            Assert.Equal(1, set.Screens[0].Distance);
            Assert.Equal(LookConstants.MaxScreens, set.Screens.Last().Distance);

            var values = new float[LookConstants.FloatCount];
            Assert.False(LookConstants.PackScreen(values, LookConstants.MaxScreens, Origin, ShipX, ShipY, ShipZ,
                ScreenMin, ScreenMax, Origin));
        }

        [Fact]
        public void ScreenMaterialsAreTheGameScreenAreas()
        {
            foreach (var name in new[] { "ScreenArea", "ScreenArea90", "TransparentScreenArea270", "CockpitScreen_01",
                "VendingScreen_02", "ATM_Screen" })
                Assert.True(ScreenSet.IsScreenMaterial(name), name);

            foreach (var name in new[] { "ATM_Keyboard", "Armor", "CockpitGlass", "", null })
                Assert.False(ScreenSet.IsScreenMaterial(name), name ?? "null");
        }
    }
}
