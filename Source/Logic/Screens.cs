using System;
using System.Collections.Generic;

namespace SirDiorama
{
    // An LCD screen near the player: the texels leave it alone, so that its
    // text reads exactly as in the game. A box in the axes of the grid that
    // carries it, from the grid's origin, around the screen's own mesh.
    public sealed class ScreenBox
    {
        // The grid's render object, whose matrix the render thread reads for
        // the very frame being drawn; the origin and axes below are only a
        // fallback, from the main thread.
        public readonly uint GridRenderObjectId;

        public readonly Vec3d GridOrigin;
        public readonly Vec3d GridX;
        public readonly Vec3d GridY;
        public readonly Vec3d GridZ;

        public readonly Vec3d Min;
        public readonly Vec3d Max;

        // Distance from the camera when it was chosen, in metres.
        public readonly double Distance;

        public ScreenBox(uint gridRenderObjectId, Vec3d gridOrigin, Vec3d gridX, Vec3d gridY, Vec3d gridZ,
            Vec3d min, Vec3d max, double distance)
        {
            GridRenderObjectId = gridRenderObjectId;
            GridOrigin = gridOrigin;
            GridX = gridX;
            GridY = gridY;
            GridZ = gridZ;
            Min = min;
            Max = max;
            Distance = distance;
        }
    }

    // The screens handed to the render thread. Immutable: it is replaced as
    // a whole by the main thread.
    public sealed class ScreenSet
    {
        public static readonly ScreenSet Empty = new ScreenSet(new ScreenBox[0]);

        public readonly ScreenBox[] Screens;

        private ScreenSet(ScreenBox[] screens)
        {
            Screens = screens;
        }

        // The nearest ones, at most as many as the shader takes.
        public static ScreenSet Nearest(IEnumerable<ScreenBox> candidates)
        {
            if (candidates == null)
                return Empty;
            var list = new List<ScreenBox>();
            foreach (var c in candidates)
            {
                if (c != null)
                    list.Add(c);
            }
            if (list.Count == 0)
                return Empty;
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (list.Count > LookConstants.MaxScreens)
                list.RemoveRange(LookConstants.MaxScreens, list.Count - LookConstants.MaxScreens);
            return new ScreenSet(list.ToArray());
        }

        // Screen materials of the game's models: CockpitScreen_01,
        // ScreenArea, TransparentScreenArea90, VendingScreen_01, ATM_Screen...
        // (the ScreenAreas of the block definitions).
        public static bool IsScreenMaterial(string materialName)
        {
            return materialName != null
                && materialName.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
