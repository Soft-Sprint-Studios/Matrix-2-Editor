using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.BspEditor.Grid;
using Sledge.Rendering.Viewports;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Tools.PolygonTool
{
    [Export(typeof(ITool))]
    [OrderHint("T")]
    [AutoTranslate]
    [DefaultHotkey("Shift+G")]
    public class PolygonTool : BaseTool
    {
        private List<Vector3> _points;
        private bool _isConvex;

        public PolygonTool()
        {
            Usage = ToolUsage.View2D;
            _points = new List<Vector3>();
            _isConvex = true;
        }

        public override Image GetIcon() => Resources.Tool_Polygon;
        public override string GetName() => "Polygon Tool";

        protected override void MouseDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) 
                return;

            var point = SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
            
            // If clicking near the first point, try to complete
            if (_points.Count > 2)
            {
                var firstScreen = camera.WorldToScreen(_points[0]);
                if (Vector2.Distance(new Vector2(e.X, e.Y), new Vector2(firstScreen.X, firstScreen.Y)) < 10)
                {
                    CreatePolygon(document, camera);
                    return;
                }
            }

            _points.Add(point);
            ValidatePolygon();
        }

        private void ValidatePolygon()
        {
            if (_points.Count < 3)
            {
                _isConvex = true;
                return;
            }
            var poly = new Sledge.DataStructures.Geometric.Polygon(_points);
            _isConvex = poly.IsConvex();
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Enter && _points.Count >= 3 && _isConvex)
            {
                CreatePolygon(document, camera);
            }
            else if (e.KeyCode == Keys.Escape || (e.KeyCode == Keys.Back && _points.Count == 0))
            {
                _points.Clear();
            }
            else if (e.KeyCode == Keys.Back && _points.Count > 0)
            {
                _points.RemoveAt(_points.Count - 1);
                ValidatePolygon();
            }
        }

        private async void CreatePolygon(MapDocument document, OrthographicCamera camera)
        {
            if (!_isConvex || _points.Count < 3)
                return;

            var gridData = document.Map.Data.GetOne<GridData>();
            float thickness = gridData?.Grid is SquareGrid sg ? sg.Step : 16f;

            var center = _points.Aggregate(Vector3.Zero, (a, b) => a + b) / _points.Count;
            var depthAxis = camera.GetUnusedCoordinate(Vector3.One);
            var offset = depthAxis * (thickness / 2);

            var bottomPoints = _points.Select(x => x - offset).ToList();
            var topPoints = _points.Select(x => x + offset).ToList();

            var solid = new Solid(document.Map.NumberGenerator.Next("MapObject"));
            solid.Data.Add(new ObjectColor(Sledge.Common.Colour.GetRandomBrushColour()));

            var texture = document.Map.Data.GetOne<ActiveTexture>()?.Name ?? "AAATRIGGER";

            // Bottom and Top faces
            AddFace(solid, document, bottomPoints, texture);
            AddFace(solid, document, topPoints.AsEnumerable().Reverse(), texture);

            // Side faces
            for (int i = 0; i < _points.Count; i++)
            {
                int next = (i + 1) % _points.Count;
                var sidePoints = new[] { bottomPoints[next], bottomPoints[i], topPoints[i], topPoints[next] };
                AddFace(solid, document, sidePoints, texture);
            }

            solid.DescendantsChanged();
            
            var tran = new Transaction(new Attach(document.Map.Root.ID, solid), new Select(solid));
            await MapDocumentOperation.Perform(document, tran);

            _points.Clear();
        }

        private void AddFace(Solid solid, MapDocument doc, IEnumerable<Vector3> verts, string tex)
        {
            var vList = verts.ToList();
            var face = new Face(doc.Map.NumberGenerator.Next("Face"))
            {
                Plane = new Sledge.DataStructures.Geometric.Plane(vList[0], vList[1], vList[2]),
                Texture = { Name = tex, XScale = (float)doc.Environment.DefaultTextureScale, YScale = (float)doc.Environment.DefaultTextureScale }
            };
            face.Vertices.AddRange(vList);
            face.Texture.AlignToNormal(face.Plane.Normal);
            solid.Data.Add(face);
        }

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            if (_points.Count == 0)
                return;

            var color = _isConvex ? Color.FromArgb(255, 128, 255, 128) : Color.Red;
            var screenPoints = _points.Select(x => camera.WorldToScreen(x).ToVector2()).ToList();

            for (int i = 0; i < screenPoints.Count; i++)
            {
                var p = screenPoints[i];
                im.AddRectFilled(p - new Vector2(2, 2), p + new Vector2(2, 2), color);
                
                if (i > 0) 
                    im.AddLine(screenPoints[i - 1], p, color, 2);
            }

            if (screenPoints.Count > 2)
            {
                im.AddLine(screenPoints.Last(), screenPoints[0], Color.FromArgb(128, color), 1);
            }
        }
    }
}