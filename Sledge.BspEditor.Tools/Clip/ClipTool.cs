using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Clip
{
    [Export(typeof(ITool))]
    [OrderHint("N")]
    [DefaultHotkey("Shift+X")]
    public class ClipTool : BaseTool
    {
        public enum ClipState
        {
            None,
            Drawing,
            Drawn,
            MovingPoint1,
            MovingPoint2,
            MovingPoint3
        }

        public enum ClipSide
        {
            Both,
            Front,
            Back
        }

        private Vector3? _clipPlanePoint1;
        private Vector3? _clipPlanePoint2;
        private Vector3? _clipPlanePoint3;
        private Vector3? _drawingPoint;
        private ClipState _prevState;
        private ClipState _state;
        private ClipSide _side;

        // ------------------------------------------------------------------
        // 3D clipping state
        // ------------------------------------------------------------------
        private const int PerspPickWidth = 7;

        /// <summary>True while point 3 was generated automatically from the clicked face normal.</summary>
        private bool _autoThirdPoint;
        /// <summary>Surface normal of the first face clicked in 3D, used for the automatic third point.</summary>
        private Vector3 _firstHitNormal = Vector3.UnitZ;

        private bool _dragging3D;
        private int _draggingIndex3D = -1;
        private Plane _dragPlane3D;

        public ClipTool()
        {
            Usage = ToolUsage.Both;
            _clipPlanePoint1 = _clipPlanePoint2 = _clipPlanePoint3 = _drawingPoint = null;
            _state = _prevState = ClipState.None;
            _side = ClipSide.Both;
        }

        public override Image GetIcon()
        {
            return Resources.Tool_Clip;
        }

        public override string GetName()
        {
            return "Clip Tool";
        }

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<ClipTool>("Tool:Activated", t => CycleClipSide());
            yield return Oy.Subscribe<string>("ClipTool:SetClipSide", v => SetClipSide(v));
            yield return Oy.Subscribe<RightClickMenuBuilder>("MapViewport:RightClick", b =>
            {
                // Right-click commits the clip once the plane is defined
                if (_state == ClipState.Drawn
                    && _clipPlanePoint1.HasValue && _clipPlanePoint2.HasValue && _clipPlanePoint3.HasValue)
                {
                    b.Intercepted = true;
                    var doc = GetDocument();
                    if (doc != null)
                    {
                        PerformClip(doc);
                        ResetPoints();
                    }
                }
            });
        }

        private void SetClipSide(string visiblePoints)
        {
            if (Enum.TryParse(visiblePoints, true, out ClipSide s) && s != _side)
            {
                _side = s;
            }
        }

        private void CycleClipSide()
        {
            var side = (int)_side;
            side = (side + 1) % Enum.GetValues(typeof(ClipSide)).Length;
            _side = (ClipSide)side;
        }

        private void ResetPoints()
        {
            _clipPlanePoint1 = _clipPlanePoint2 = _clipPlanePoint3 = _drawingPoint = null;
            _state = _prevState = ClipState.None;
            _autoThirdPoint = false;
            _dragging3D = false;
            _draggingIndex3D = -1;
        }

        private static ClipState StateForIndex(int index)
        {
            switch (index)
            {
                case 0: return ClipState.MovingPoint1;
                case 1: return ClipState.MovingPoint2;
                default: return ClipState.MovingPoint3;
            }
        }

        #region 2D interaction

        private ClipState GetStateAtPoint(int x, int y, OrthographicCamera camera)
        {
            if (_clipPlanePoint1 == null || _clipPlanePoint2 == null || _clipPlanePoint3 == null) return ClipState.None;

            var p = camera.Flatten(camera.ScreenToWorld(new Vector3(x, y, 0)));
            var p1 = camera.Flatten(_clipPlanePoint1.Value);
            var p2 = camera.Flatten(_clipPlanePoint2.Value);
            var p3 = camera.Flatten(_clipPlanePoint3.Value);

            var d = 5 / camera.Zoom;

            if (p.X >= p1.X - d && p.X <= p1.X + d && p.Y >= p1.Y - d && p.Y <= p1.Y + d) return ClipState.MovingPoint1;
            if (p.X >= p2.X - d && p.X <= p2.X + d && p.Y >= p2.Y - d && p.Y <= p2.Y + d) return ClipState.MovingPoint2;
            if (p.X >= p3.X - d && p.X <= p3.X + d && p.Y >= p3.Y - d && p.Y <= p3.Y + d) return ClipState.MovingPoint3;

            return ClipState.None;
        }

        protected override void MouseDown(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            _prevState = _state;

            var point = SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
            var st = GetStateAtPoint(e.X, e.Y, camera);
            if (_state == ClipState.None || st == ClipState.None)
            {
                _state = ClipState.Drawing;
                _drawingPoint = point;
                _autoThirdPoint = false;
            }
            else if (_state == ClipState.Drawn)
            {
                _state = st;
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            _state = _state == ClipState.Drawing ? _prevState : ClipState.Drawn;
        }

        protected override void MouseMove(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            var viewport = vp;

            var point = SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
            var st = GetStateAtPoint(e.X, e.Y, camera);
            if (_state == ClipState.Drawing)
            {
                _state = ClipState.MovingPoint2;
                _clipPlanePoint1 = _drawingPoint;
                _clipPlanePoint2 = point;
                _clipPlanePoint3 = _clipPlanePoint1 + SnapIfNeeded(camera.GetUnusedCoordinate(new Vector3(128, 128, 128)));
                _autoThirdPoint = false;
            }
            else if (_state == ClipState.MovingPoint1)
            {
                // Move point 1
                var cp1 = camera.GetUnusedCoordinate(_clipPlanePoint1.Value) + point;
                if (KeyboardState.Ctrl)
                {
                    var diff = _clipPlanePoint1 - cp1;
                    _clipPlanePoint2 -= diff;
                    _clipPlanePoint3 -= diff;
                }
                _clipPlanePoint1 = cp1;
            }
            else if (_state == ClipState.MovingPoint2)
            {
                // Move point 2
                var cp2 = camera.GetUnusedCoordinate(_clipPlanePoint2.Value) + point;
                if (KeyboardState.Ctrl)
                {
                    var diff = _clipPlanePoint2 - cp2;
                    _clipPlanePoint1 -= diff;
                    _clipPlanePoint3 -= diff;
                }
                _clipPlanePoint2 = cp2;
            }
            else if (_state == ClipState.MovingPoint3)
            {
                // Move point 3
                var cp3 = camera.GetUnusedCoordinate(_clipPlanePoint3.Value) + point;
                if (KeyboardState.Ctrl)
                {
                    var diff = _clipPlanePoint3 - cp3;
                    _clipPlanePoint1 -= diff;
                    _clipPlanePoint2 -= diff;
                }
                _clipPlanePoint3 = cp3;
                _autoThirdPoint = false;
            }

            if (st != ClipState.None || _state != ClipState.None && _state != ClipState.Drawn)
            {
                viewport.Control.Cursor = Cursors.Cross;
            }
            else
            {
                viewport.Control.Cursor = Cursors.Default;
            }
        }

        #endregion

        #region 3D interaction

        private static Vector3? IntersectHorizontalPlane(Line ray, float z)
        {
            var dir = ray.End - ray.Start;
            if (Math.Abs(dir.Z) < 0.000001f) return null;
            var t = (z - ray.Start.Z) / dir.Z;
            if (t < 0) return null;
            return ray.Start + dir * t;
        }

        /// <summary>
        /// Screen-space pick of the clip handles in a perspective view.
        /// Returns the index of the closest handle within tolerance, or -1.
        /// </summary>
        private int GetPointNearScreen(PerspectiveCamera camera, MapViewport viewport, int x, int y)
        {
            var points = new[] { _clipPlanePoint1, _clipPlanePoint2, _clipPlanePoint3 };
            var best = -1;
            var bestDist = float.MaxValue;

            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].HasValue) continue;
                var sp = camera.WorldToScreen(points[i].Value);
                if (sp.Z > 1) continue;

                var dx = Math.Abs(sp.X - x);
                var dy = Math.Abs(sp.Y - y);
                if (dx > PerspPickWidth || dy > PerspPickWidth) continue;

                var dist = dx + dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Find the face plane of the hit object that contains the given point (within tolerance).
        /// </summary>
        private static Plane GetFaceNormalAt(IMapObject obj, Vector3 point)
        {
            var solid = obj as Solid;
            if (solid == null) return null;

            Plane best = null;
            var bestDist = float.MaxValue;
            foreach (var f in solid.Faces)
            {
                var dist = Math.Abs(f.Plane.EvalAtPoint(point));
                if (dist < bestDist && dist < 1f)
                {
                    bestDist = dist;
                    best = f.Plane;
                }
            }
            return best;
        }

        /// <summary>Set the next free point, managing the automatic third point.</summary>
        private void PlaceNextPoint(Vector3 point, Vector3 normal)
        {
            if (!_clipPlanePoint1.HasValue)
            {
                _clipPlanePoint1 = point;
                _firstHitNormal = normal;
                _state = ClipState.Drawing;
                return;
            }

            if (!_clipPlanePoint2.HasValue)
            {
                _clipPlanePoint2 = point;

                // Automatic third point: push out along the first clicked face normal
                _clipPlanePoint3 = _clipPlanePoint1.Value + normal * 128;
                _autoThirdPoint = true;
                _state = ClipState.Drawn;
                return;
            }

            if (_autoThirdPoint)
            {
                // Third click replaces the automatic point with a manual one
                _clipPlanePoint3 = point;
                _autoThirdPoint = false;
                _state = ClipState.Drawn;
                return;
            }

            // All three points are manual: start a brand new clip here
            _clipPlanePoint1 = point;
            _clipPlanePoint2 = _clipPlanePoint3 = null;
            _state = ClipState.Drawing;
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!viewport.IsUnlocked(this)) return;

            // Grab an existing handle to drag it
            var idx = GetPointNearScreen(camera, viewport, e.X, e.Y);
            if (idx >= 0 && _state == ClipState.Drawn)
            {
                _dragging3D = true;
                _draggingIndex3D = idx;
                _prevState = _state;
                _state = StateForIndex(idx);

                var p = PointByIndex(idx);
                _dragPlane3D = new Plane(camera.Direction.Normalise(), p);

                viewport.AquireInputLock(this);
                e.Handled = true;
                return;
            }

            // Otherwise place the next clip point
            var (rayStart, rayEnd) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(rayStart, rayEnd);

            var tf = document.Map.Data.GetOne<DisplayFlags>() ?? new DisplayFlags();
            var iopt = (tf.HideClipTextures ? MapObjectExtensions.IgnoreOptions.IgnoreClip : MapObjectExtensions.IgnoreOptions.None)
                     | (tf.HideNullTextures ? MapObjectExtensions.IgnoreOptions.IgnoreNull : MapObjectExtensions.IgnoreOptions.None);

            var hit = document.Map.Root.GetIntersectionsForVisibleObjects(ray, iopt).FirstOrDefault();

            Vector3 point;
            var normal = Vector3.UnitZ;
            if (hit != null)
            {
                point = hit.Intersection;
                var facePlane = GetFaceNormalAt(hit.Object, point);
                if (facePlane != null) normal = facePlane.Normal;
            }
            else
            {
                point = IntersectHorizontalPlane(ray, 0f) ?? (ray.Start + (ray.End - ray.Start) * 512);
                normal = Vector3.UnitZ;
            }

            point = SnapIfNeeded(point);

            // Starting a fresh stroke clears any previous clip
            if (_clipPlanePoint1.HasValue && !_autoThirdPoint && _clipPlanePoint3.HasValue)
            {
                _clipPlanePoint1 = _clipPlanePoint2 = _clipPlanePoint3 = null;
            }

            PlaceNextPoint(point, normal);
            e.Handled = true;
        }

        private Vector3 PointByIndex(int index)
        {
            switch (index)
            {
                case 0: return _clipPlanePoint1 ?? Vector3.Zero;
                case 1: return _clipPlanePoint2 ?? Vector3.Zero;
                default: return _clipPlanePoint3 ?? Vector3.Zero;
            }
        }

        private void SetPointByIndex(int index, Vector3 value)
        {
            switch (index)
            {
                case 0: _clipPlanePoint1 = value; break;
                case 1: _clipPlanePoint2 = value; break;
                default: _clipPlanePoint3 = value; break;
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_dragging3D)
            {
                var (rs, re) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
                var ray = new Line(rs, re);

                var isect = _dragPlane3D.GetIntersectionPoint(ray, ignoreDirection: true);
                if (isect.HasValue)
                {
                    var np = KeyboardState.Alt ? isect.Value : SnapIfNeeded(isect.Value);
                    var old = PointByIndex(_draggingIndex3D);

                    // Ctrl moves all three points together, matching the 2D behaviour
                    if (KeyboardState.Ctrl)
                    {
                        var diff = old - np;
                        for (var i = 0; i < 3; i++)
                        {
                            if (i == _draggingIndex3D) continue;
                            var p = PointByIndex(i);
                            if (_clipPlanePoint3.HasValue || i < 2) SetPointByIndex(i, p - diff);
                        }
                    }

                    SetPointByIndex(_draggingIndex3D, np);
                    if (_draggingIndex3D == 2) _autoThirdPoint = false;
                }
                e.Handled = true;
                return;
            }

            // Hover feedback
            var hover = GetPointNearScreen(camera, viewport, e.X, e.Y);
            viewport.Control.Cursor = hover >= 0 ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_dragging3D)
            {
                _dragging3D = false;
                _draggingIndex3D = -1;
                _state = ClipState.Drawn;
                _prevState = ClipState.Drawn;
                viewport.ReleaseInputLock(this);
                e.Handled = true;
            }
        }

        #endregion

        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            OnKeyDown(document, viewport, e);
            base.KeyDown(document, viewport, camera, e);
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            OnKeyDown(document, viewport, e);
            base.KeyDown(document, viewport, camera, e);
        }

        private void OnKeyDown(MapDocument document, MapViewport viewport, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Enter && _state != ClipState.None)
            {
                if (!_clipPlanePoint1.Value.EquivalentTo(_clipPlanePoint2.Value)
                    && !_clipPlanePoint2.Value.EquivalentTo(_clipPlanePoint3.Value)
                    && !_clipPlanePoint1.Value.EquivalentTo(_clipPlanePoint3.Value)) // Don't clip if the points are too close together
                {
                    PerformClip(document);
                }
            }
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) // Escape cancels, Enter commits and resets
            {
                ResetPoints();
            }
        }

        private void PerformClip(MapDocument document)
        {
            var objects = document.Selection.OfType<Solid>().ToList();
            if (!objects.Any()) return;

            var plane = new Plane(_clipPlanePoint1.Value, _clipPlanePoint2.Value, _clipPlanePoint3.Value);
            var clip = new Transaction();
            var found = false;
            foreach (var solid in objects)
            {
                solid.Split(document.Map.NumberGenerator, plane, out var backSolid, out var frontSolid);
                found = true;

                // Remove the clipped solid
                clip.Add(new Detatch(solid.Hierarchy.Parent.ID, solid));

                if (_side != ClipSide.Back && frontSolid != null)
                {
                    // Add front solid
                    clip.Add(new Attach(solid.Hierarchy.Parent.ID, frontSolid));
                }

                if (_side != ClipSide.Front && backSolid != null)
                {
                    // Add back solid
                    clip.Add(new Attach(solid.Hierarchy.Parent.ID, backSolid));
                }
            }
            if (found)
            {
                MapDocumentOperation.Perform(document, clip);
            }
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            if (_state != ClipState.None && _clipPlanePoint1 != null && _clipPlanePoint2 != null && _clipPlanePoint3 != null)
            {
                // Draw the lines
                var p1 = _clipPlanePoint1.Value;
                var p2 = _clipPlanePoint2.Value;
                var p3 = _clipPlanePoint3.Value;

                builder.Append(
                    new[]
                    {
                        new VertexStandard { Position = p1, Colour = Vector4.One, Tint = Vector4.One },
                        new VertexStandard { Position = p2, Colour = Vector4.One, Tint = Vector4.One },
                        new VertexStandard { Position = p3, Colour = Vector4.One, Tint = Vector4.One },
                    },
                    new uint[] { 0, 1, 1, 2, 2, 0 },
                    new[]
                    {
                        new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, 6)
                    }
                );

                if (!p1.EquivalentTo(p2)
                    && !p2.EquivalentTo(p3)
                    && !p1.EquivalentTo(p3)
                    && !document.Selection.IsEmpty)
                {
                    var plane = new Plane(p1, p2, p3);
                    var pp = plane.ToPrecisionPlane();

                    // Draw the clipped solids
                    var faces = new List<Polygon>();
                    foreach (var solid in document.Selection.OfType<Solid>().ToList())
                    {
                        var s = solid.ToPolyhedron().ToPrecisionPolyhedron();
                        s.Split(pp, out var back, out var front);

                        if (_side != ClipSide.Front && back != null) faces.AddRange(back.Polygons.Select(x => x.ToStandardPolygon()));
                        if (_side != ClipSide.Back && front != null) faces.AddRange(front.Polygons.Select(x => x.ToStandardPolygon()));
                    }

                    var verts = new List<VertexStandard>();
                    var indices = new List<int>();

                    foreach (var polygon in faces)
                    {
                        var c = verts.Count;
                        verts.AddRange(polygon.Vertices.Select(x => new VertexStandard { Position = x, Colour = Vector4.One, Tint = Vector4.One }));
                        for (var i = 0; i < polygon.Vertices.Count; i++)
                        {
                            indices.Add(c + i);
                            indices.Add(c + (i + 1) % polygon.Vertices.Count);
                        }
                    }

                    builder.Append(
                        verts, indices.Select(x => (uint)x),
                        new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, (uint)indices.Count) }
                    );

                    // Draw the clipping plane

                    var poly = new DataStructures.Geometric.Precision.Polygon(pp);
                    var bbox = document.Selection.GetSelectionBoundingBox();
                    var point = bbox.Center;
                    foreach (var boxPlane in bbox.GetBoxPlanes())
                    {
                        var proj = boxPlane.Project(point);
                        var dist = (point - proj).Length() * 0.1f;
                        var pln = new Plane(boxPlane.Normal, proj + boxPlane.Normal * Math.Max(dist, 100)).ToPrecisionPlane();
                        if (poly.Split(pln, out var b, out _)) poly = b;
                    }

                    verts.Clear();
                    indices.Clear();

                    var clipPoly = poly.ToStandardPolygon();
                    var colour = Color.FromArgb(64, Color.Turquoise).ToVector4();

                    // Add the face in both directions so it renders on both sides
                    var polies = new[] { clipPoly.Vertices.ToList(), clipPoly.Vertices.Reverse().ToList() };
                    foreach (var p in polies)
                    {
                        var offs = verts.Count;
                        verts.AddRange(p.Select(x => new VertexStandard
                        {
                            Position = x,
                            Colour = Vector4.One,
                            Tint = colour,
                            Flags = VertexFlags.FlatColour
                        }));

                        for (var i = 2; i < clipPoly.Vertices.Count; i++)
                        {
                            indices.Add(offs);
                            indices.Add(offs + i - 1);
                            indices.Add(offs + i);
                        }
                    }

                    builder.Append(
                        verts, indices.Select(x => (uint)x),
                        new[] { new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, p1, 0, (uint)indices.Count) }
                    );
                }
            }
        }

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            base.Render(document, viewport, camera, worldMin, worldMax, im);

            if (_state != ClipState.None && _clipPlanePoint1 != null && _clipPlanePoint2 != null && _clipPlanePoint3 != null)
            {
                var p1 = _clipPlanePoint1.Value;
                var p2 = _clipPlanePoint2.Value;
                var p3 = _clipPlanePoint3.Value;
                var points = new[] { p1, p2, p3 };

                foreach (var p in points)
                {
                    const int size = 4;
                    var spos = camera.WorldToScreen(p);

                    im.AddRectOutlineOpaque(new Vector2(spos.X - size, spos.Y - size), new Vector2(spos.X + size, spos.Y + size), Color.Black, Color.White);
                }
            }
        }

        protected override void Render(MapDocument document, IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            base.Render(document, viewport, camera, im);

            if (_state == ClipState.None || _clipPlanePoint1 == null || _clipPlanePoint2 == null || _clipPlanePoint3 == null) return;

            // Draw the handles in 3D so they can be seen and grabbed
            var points = new[] { _clipPlanePoint1.Value, _clipPlanePoint2.Value, _clipPlanePoint3.Value };
            foreach (var p in points)
            {
                const int size = 5;
                var spos = camera.WorldToScreen(p);
                if (spos.Z > 1) continue;

                im.AddRectOutlineOpaque(new Vector2(spos.X - size, spos.Y - size), new Vector2(spos.X + size, spos.Y + size), Color.Black, Color.White);
            }
        }
    }
}