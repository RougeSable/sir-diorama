using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SirDiorama
{
    // Chooses, on the main thread, what the texels are fastened to (see
    // Anchor): the planet's vertical, else the world, for the surroundings;
    // and the grid the player is in or next to, with its box, for what lies
    // inside it. Called a few times per second; the render thread reads the
    // grid's matrix itself, frame by frame.
    internal sealed class AnchorPicker
    {
        // Grids farther than this from the camera are not candidates.
        private const double SearchRadius = 60;

        // The current grid is kept until another one is clearly closer: no
        // flicker between two grids side by side.
        private const double KeepMargin = 5;

        // In first person on foot, what is closer than this follows the
        // camera (the tool in hand).
        private const float HandReach = 1.0f;

        // Margin around the grid's box, in blocks: what stands on its hull
        // (a character, a tool, a light's glow) keeps the grid's texels.
        private const double BoxMargin = 1.0;

        private readonly GravityFrame m_gravity = new GravityFrame();
        private long m_gridId;

        public Anchor Pick()
        {
            var session = MyAPIGateway.Session;
            var camera = session == null ? null : session.Camera;
            if (camera == null)
                return Anchor.Default;

            var eye = camera.WorldMatrix.Translation;
            var controlled = session.ControlledObject;

            var near = 0f;
            var controller = session.CameraController;
            if (controlled is IMyCharacter && controller != null && controller.IsInFirstPersonView)
                near = HandReach;

            // The surroundings: the planet's vertical, else the world. Kept
            // even with a grid near, for everything outside its box (the
            // ground, the asteroids seen through the cockpit glass).
            Anchor anchor;
            float interference;
            var gravity = MyAPIGateway.Physics == null ? Vector3.Zero : MyAPIGateway.Physics.CalculateNaturalGravityAt(eye, out interference);
            if (gravity.LengthSquared() > 1e-4f)
            {
                m_gravity.Update(-ToVec(gravity));
                anchor = Anchor.Gravity(m_gravity, near);
            }
            else
            {
                m_gravity.Reset();
                anchor = Anchor.World(near);
            }

            // In a seat or a cockpit: the ship itself. Else the grid next to
            // the camera, if any. Only what lies in its box takes its frame.
            var block = controlled as IMyCubeBlock;
            var grid = block != null ? block.CubeGrid : NearestGrid(eye);
            if (grid == null || grid.Render == null)
            {
                m_gridId = 0;
                return anchor;
            }

            m_gridId = grid.EntityId;
            var m = grid.WorldMatrix;
            var box = grid.LocalAABB;
            return anchor.WithGrid(new GridAnchor(grid.Render.GetRenderObjectID(), ToVec(m.Translation),
                ToVec(Vector3D.Normalize(m.Right)), ToVec(Vector3D.Normalize(m.Up)), ToVec(Vector3D.Normalize(m.Backward)),
                ToVec(box.Min), ToVec(box.Max), BoxMargin * grid.GridSize));
        }

        // The grid whose box is closest to the camera, within reach. Grids
        // without physics (projections) are left out.
        private IMyCubeGrid NearestGrid(Vector3D eye)
        {
            var sphere = new BoundingSphereD(eye, SearchRadius);
            var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            if (entities == null)
                return null;

            IMyCubeGrid best = null, current = null;
            double bestDistance = double.MaxValue, currentDistance = double.MaxValue;
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose || grid.Closed || grid.Physics == null)
                    continue;

                var distance = DistanceToGrid(grid, eye);
                if (distance > SearchRadius)
                    continue;
                if (distance < bestDistance)
                {
                    best = grid;
                    bestDistance = distance;
                }
                if (grid.EntityId == m_gridId)
                {
                    current = grid;
                    currentDistance = distance;
                }
            }

            if (current != null && currentDistance <= bestDistance + KeepMargin)
                return current;
            return best;
        }

        // Distance from a point to the grid's box, in the grid's own axes.
        private static double DistanceToGrid(IMyCubeGrid grid, Vector3D point)
        {
            var local = Vector3D.Transform(point, grid.WorldMatrixNormalizedInv);
            var box = grid.LocalAABB;
            var dx = Math.Max(0, Math.Max(box.Min.X - local.X, local.X - box.Max.X));
            var dy = Math.Max(0, Math.Max(box.Min.Y - local.Y, local.Y - box.Max.Y));
            var dz = Math.Max(0, Math.Max(box.Min.Z - local.Z, local.Z - box.Max.Z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static Vec3d ToVec(Vector3D v)
        {
            return new Vec3d(v.X, v.Y, v.Z);
        }
    }
}
