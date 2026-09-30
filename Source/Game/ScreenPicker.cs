using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Models;
using VRage.ModAPI;
using VRageMath;
using IMyTextPanel = Sandbox.ModAPI.Ingame.IMyTextPanel;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace SirDiorama
{
    // Finds, on the main thread, the LCD screens near the player: the texels
    // leave them alone, so that their text reads as in the game. A screen is
    // the part of a block's model drawn with a screen material (the block
    // definition's ScreenAreas: ScreenArea, CockpitScreen_01...), so only the
    // screen itself keeps the game's pixels, not the whole cockpit around it.
    // Called twice per second; the render thread reads the grids' matrices
    // itself, frame by frame.
    internal sealed class ScreenPicker
    {
        // Screens farther than this from the camera are not candidates: past
        // it, a text is too small to read anyway.
        private const double SearchRadius = 100;

        private static readonly BoundingBox[] NoBox = new BoundingBox[0];

        // Screen boxes of each model, in the block's own axes, found once.
        private readonly Dictionary<string, BoundingBox[]> m_modelScreens = new Dictionary<string, BoundingBox[]>();
        private readonly List<ScreenBox> m_found = new List<ScreenBox>();

        public ScreenSet Pick()
        {
            var session = MyAPIGateway.Session;
            var camera = session == null ? null : session.Camera;
            if (camera == null || MyAPIGateway.Entities == null)
                return ScreenSet.Empty;

            var eye = camera.WorldMatrix.Translation;
            var forward = camera.WorldMatrix.Forward;
            var sphere = new BoundingSphereD(eye, SearchRadius);
            var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            if (entities == null)
                return ScreenSet.Empty;

            m_found.Clear();
            foreach (var entity in entities)
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose || grid.Closed || grid.Physics == null || grid.Render == null)
                    continue;
                AddScreens(grid, eye, forward);
            }
            return ScreenSet.Nearest(m_found);
        }

        private void AddScreens(MyCubeGrid grid, Vector3D eye, Vector3D forward)
        {
            var gridMatrix = grid.WorldMatrix;
            var toGrid = grid.PositionComp.WorldMatrixNormalizedInv;
            var origin = ToVec(gridMatrix.Translation);
            var axisX = ToVec(Vector3D.Normalize(gridMatrix.Right));
            var axisY = ToVec(Vector3D.Normalize(gridMatrix.Up));
            var axisZ = ToVec(Vector3D.Normalize(gridMatrix.Backward));
            var renderId = grid.Render.GetRenderObjectID();

            foreach (var block in grid.GetFatBlocks())
            {
                if (block == null || block.Closed || !(block is IMyTextSurfaceProvider))
                    continue;

                var bounds = block.PositionComp.WorldAABB;
                var distance = Math.Sqrt(bounds.DistanceSquared(eye));
                if (distance > SearchRadius)
                    continue;
                // Behind the camera: not seen.
                if (Vector3D.Dot(bounds.Center - eye, forward) < -bounds.HalfExtents.Length())
                    continue;

                var boxes = ScreenBoxes(block.Model);
                if (boxes.Length == 0 && block is IMyTextPanel && block.Model != null)
                    boxes = new[] { block.Model.BoundingBox }; // a panel is all screen

                // From the block's axes to the grid's.
                var blockToGrid = block.WorldMatrix * toGrid;
                foreach (var box in boxes)
                {
                    var inGrid = BoxInGrid(box, ref blockToGrid);
                    m_found.Add(new ScreenBox(renderId, origin, axisX, axisY, axisZ,
                        ToVec(inGrid.Min), ToVec(inGrid.Max), distance));
                }
            }
        }

        private static BoundingBoxD BoxInGrid(BoundingBox box, ref MatrixD blockToGrid)
        {
            var result = BoundingBoxD.CreateInvalid();
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3D(
                    (i & 1) == 0 ? box.Min.X : box.Max.X,
                    (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (i & 4) == 0 ? box.Min.Z : box.Max.Z);
                result.Include(Vector3D.Transform(corner, blockToGrid));
            }
            return result;
        }

        // The boxes of the screen materials of a model, in the block's axes.
        // Read once per model, from the game's own model data.
        private BoundingBox[] ScreenBoxes(MyModel model)
        {
            if (model == null || model.AssetName == null)
                return NoBox;

            BoundingBox[] boxes;
            if (m_modelScreens.TryGetValue(model.AssetName, out boxes))
                return boxes;

            try
            {
                boxes = ReadScreenBoxes(model.LoadedData ? model : MyModels.GetModelOnlyData(model.AssetName));
            }
            catch (Exception e)
            {
                DioramaPlugin.Log("screens of " + model.AssetName + " not read: " + e.Message);
                boxes = NoBox;
            }
            m_modelScreens[model.AssetName] = boxes;
            return boxes;
        }

        private static BoundingBox[] ReadScreenBoxes(MyModel model)
        {
            if (model == null || model.Triangles == null)
                return NoBox;

            var meshes = model.GetMeshList();
            if (meshes == null)
                return NoBox;

            var triangles = model.Triangles;
            var vertices = model.GetVerticesCount();
            var result = new List<BoundingBox>();
            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.Material == null || !ScreenSet.IsScreenMaterial(mesh.Material.Name))
                    continue;

                var box = BoundingBox.CreateInvalid();
                var end = Math.Min(mesh.TriStart + mesh.TriCount, triangles.Length);
                for (var t = Math.Max(0, mesh.TriStart); t < end; t++)
                {
                    var triangle = triangles[t];
                    if (triangle.I0 < 0 || triangle.I1 < 0 || triangle.I2 < 0
                        || triangle.I0 >= vertices || triangle.I1 >= vertices || triangle.I2 >= vertices)
                        continue;
                    box.Include(model.GetVertex(triangle.I0));
                    box.Include(model.GetVertex(triangle.I1));
                    box.Include(model.GetVertex(triangle.I2));
                }
                if (box.Min.X <= box.Max.X)
                    result.Add(box);
            }
            return result.Count == 0 ? NoBox : result.ToArray();
        }

        private static Vec3d ToVec(Vector3D v)
        {
            return new Vec3d(v.X, v.Y, v.Z);
        }
    }
}
