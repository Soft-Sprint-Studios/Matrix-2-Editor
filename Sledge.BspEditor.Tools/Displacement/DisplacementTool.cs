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
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Threading;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Modification.Operations.Selection;
using System.Threading.Tasks;

namespace Sledge.BspEditor.Tools.Displacement
{
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("V")]
    [AutoTranslate]
    [DefaultHotkey("Shift+Y")]
    public class DisplacementTool : BaseTool
    {
        public enum DisplacementPaintAxis
        {
            FaceNormal,
            X,
            Y,
            Z
        }

        public enum DisplacementSculptMode
        {
            RaiseLower,
            RaiseTo,
            Smooth,
            Alpha
        }

        public DisplacementTool()
        {
            Usage = ToolUsage.Both;
        }

        public override Image GetIcon() => Properties.Resources.Tool_Displacement;
        public override string GetName() => "Displacement Tool";

        public Face SelectedFace { get; set; }
        public Solid SelectedSolid { get; set; }

        private Face _originalFaceState;
        private Face _activePaintingFace;
        private Solid _activePaintingSolid;

        public int PaintRadius { get; set; } = 64;
        public float PaintAmount { get; set; } = 5f;
        public bool IsPainting { get; set; } = false;
        public DisplacementPaintAxis PaintAxis { get; set; } = DisplacementPaintAxis.FaceNormal;
        public DisplacementSculptMode SculptMode { get; set; } = DisplacementSculptMode.RaiseLower;

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<RightClickMenuBuilder>("MapViewport:RightClick", b =>
            {
                b.Intercepted = true;
            });
        }
        public override async Task ToolSelected()
        {
            var document = GetDocument();
            if (document != null)
            {
                await MapDocumentOperation.Bypass(document, new Deselect(document.Selection));
            }

            SelectedFace = null;
            SelectedSolid = null;
            IsPainting = false;
            Oy.Publish("DisplacementTool:FaceSelected", this);

            await base.ToolSelected();
        }

        public override async Task ToolDeselected()
        {
            SelectedFace = null;
            SelectedSolid = null;
            IsPainting = false;
            Oy.Publish("DisplacementTool:FaceSelected", this);

            await base.ToolDeselected();
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            var clicked = document.Map.Root.GetBoudingBoxIntersectionsForVisibleObjects(ray)
                .OfType<Solid>()
                .SelectMany(a => a.Faces.Select(f => new { Face = f, Solid = a }))
                .Select(x => new { x.Face, x.Solid, Intersection = x.Face.GetIntersectionPoint(ray) })
                .Where(x => x.Intersection != null)
                .OrderBy(x => (x.Intersection.Value - ray.Start).LengthSquared())
                .FirstOrDefault();

            if (clicked == null) return;

            if (IsPainting && clicked.Face.Displacement != null)
            {
                _originalFaceState = (Face)clicked.Face.Clone();
                _activePaintingFace = clicked.Face;
                _activePaintingSolid = clicked.Solid;

                PaintDisplacement(_activePaintingFace, clicked.Intersection.Value, e.Button == MouseButtons.Left ? PaintAmount : -PaintAmount);
                _activePaintingSolid.DescendantsChanged();

                Oy.Publish("MapDocument:Changed", new Change(document).Update(_activePaintingSolid));
                e.Handled = true;
                viewport.AquireInputLock(this);
            }
            else if (e.Button == MouseButtons.Left)
            {
                SelectedFace = clicked.Face;
                SelectedSolid = clicked.Solid;
                Oy.Publish("DisplacementTool:FaceSelected", this);
                e.Handled = true;
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (!IsPainting || _activePaintingFace == null) return;
            if (!Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) && !Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.RButton)) return;

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            var intersection = _activePaintingFace.GetIntersectionPoint(ray);
            if (intersection != null)
            {
                PaintDisplacement(_activePaintingFace, intersection.Value, Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) ? PaintAmount : -PaintAmount);
                _activePaintingSolid.DescendantsChanged();
                Oy.Publish("MapDocument:Changed", new Change(document).Update(_activePaintingSolid));
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_activePaintingFace != null && _activePaintingSolid != null)
            {
                var finishedFace = (Face)_activePaintingFace.Clone();

                _activePaintingFace.Texture = _originalFaceState.Texture.Clone();
                _activePaintingFace.Displacement = _originalFaceState.Displacement?.Clone();
                _activePaintingFace.Vertices.Clear();
                _activePaintingFace.Vertices.AddRange(_originalFaceState.Vertices);

                MapDocumentOperation.Perform(document, new Transaction(
                    new RemoveMapObjectData(_activePaintingSolid.ID, _activePaintingFace),
                    new AddMapObjectData(_activePaintingSolid.ID, finishedFace)
                ));

                SelectedFace = finishedFace;
                _activePaintingFace = null;
                _activePaintingSolid = null;
                _originalFaceState = null;
            }
            viewport.ReleaseInputLock(this);
        }

        private void PaintDisplacement(Face face, Vector3 hit, float amount)
        {
            int power = face.Displacement.Power;
            int side = (1 << power) + 1;
            var corners = face.Displacement.Corners.ToList();

            int totalVerts = side * side;
            if (face.Displacement.Alphas == null || face.Displacement.Alphas.Length != totalVerts)
            {
                var newAlphas = new float[totalVerts];
                if (face.Displacement.Alphas != null)
                {
                    Array.Copy(face.Displacement.Alphas, newAlphas, Math.Min(face.Displacement.Alphas.Length, totalVerts));
                }
                face.Displacement.Alphas = newAlphas;
            }

            Vector3 paintDirection = face.Plane.Normal;
            if (PaintAxis == DisplacementPaintAxis.X) paintDirection = Vector3.UnitX;
            else if (PaintAxis == DisplacementPaintAxis.Y) paintDirection = Vector3.UnitY;
            else if (PaintAxis == DisplacementPaintAxis.Z) paintDirection = Vector3.UnitZ;

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    float fr_x = (float)x / (side - 1);
                    float fr_y = (float)y / (side - 1);
                    var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                    var bot = Vector3.Lerp(corners[3], corners[2], fr_x);

                    Vector3 currentDir = face.Displacement.Vectors[y * side + x];
                    float currentDist = face.Displacement.Distances[y * side + x];
                    Vector3 pos = Vector3.Lerp(top, bot, fr_y) + currentDir * currentDist;

                    float distToMouse = (pos - hit).Length();
                    if (distToMouse < PaintRadius)
                    {
                        float falloff = 1.0f - (distToMouse / PaintRadius);

                        if (SculptMode == DisplacementSculptMode.RaiseLower)
                        {
                            Vector3 totalOffset = (currentDir * currentDist) + (paintDirection * (amount * falloff));
                            face.Displacement.Distances[y * side + x] = totalOffset.Length();
                            if (face.Displacement.Distances[y * side + x] > 0.001f)
                                face.Displacement.Vectors[y * side + x] = Vector3.Normalize(totalOffset);
                        }
                        else if (SculptMode == DisplacementSculptMode.RaiseTo)
                        {
                            face.Displacement.Distances[y * side + x] = PaintAmount;
                            face.Displacement.Vectors[y * side + x] = paintDirection;
                        }
                        else if (SculptMode == DisplacementSculptMode.Smooth)
                        {
                            float avg = 0;
                            int count = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx >= 0 && nx < side && ny >= 0 && ny < side)
                                    {
                                        avg += face.Displacement.Distances[ny * side + nx];
                                        count++;
                                    }
                                }
                            }
                            avg /= count;
                            face.Displacement.Distances[y * side + x] = currentDist + (avg - currentDist) * falloff;
                        }
                        else if (SculptMode == DisplacementSculptMode.Alpha)
                        {
                            float currentAlpha = face.Displacement.Alphas[y * side + x];
                            float newAlpha = Math.Clamp(currentAlpha + (amount * falloff * 10f), 0f, 255f);
                            face.Displacement.Alphas[y * side + x] = newAlpha;
                        }
                    }
                }
            }
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);
            if (SelectedFace != null)
            {
                var verts = new List<VertexStandard>();
                var indices = new List<uint>();
                var groups = new List<BufferGroup>();

                var selectionColour = Color.FromArgb(64, Color.Red).ToVector4();

                if (SelectedFace.Displacement != null && SelectedFace.Vertices.Count >= 4)
                {
                    int power = SelectedFace.Displacement.Power;
                    int side = (1 << power) + 1;
                    var corners = SelectedFace.Displacement.Corners.ToList();

                    uint d_offs = (uint)verts.Count;

                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            float fr_x = (float)x / (side - 1);
                            float fr_y = (float)y / (side - 1);
                            var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                            var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                            var pos = Vector3.Lerp(top, bot, fr_y) + SelectedFace.Displacement.Vectors[y * side + x] * SelectedFace.Displacement.Distances[y * side + x];

                            verts.Add(new VertexStandard
                            {
                                Position = pos,
                                Colour = Color.Red.ToVector4(),
                                Tint = selectionColour,
                                Flags = VertexFlags.FlatColour
                            });
                        }
                    }

                    uint si_start = (uint)indices.Count;
                    for (uint y = 0; y < (uint)side - 1; y++)
                    {
                        for (uint x = 0; x < (uint)side - 1; x++)
                        {
                            indices.Add(d_offs + (y * (uint)side + x));
                            indices.Add(d_offs + (y * (uint)side + (x + 1)));
                            indices.Add(d_offs + ((y + 1) * (uint)side + x));

                            indices.Add(d_offs + (y * (uint)side + (x + 1)));
                            indices.Add(d_offs + ((y + 1) * (uint)side + (x + 1)));
                            indices.Add(d_offs + ((y + 1) * (uint)side + x));
                        }
                    }
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, SelectedFace.Origin, si_start, (uint)(indices.Count - si_start)));

                    uint wi_start = (uint)indices.Count;
                    for (uint y = 0; y < (uint)side; y++)
                    {
                        for (uint x = 0; x < (uint)side - 1; x++)
                        {
                            indices.Add(d_offs + (y * (uint)side + x));
                            indices.Add(d_offs + (y * (uint)side + (x + 1)));
                        }
                    }
                    for (uint y = 0; y < (uint)side - 1; y++)
                    {
                        for (uint x = 0; x < (uint)side; x++)
                        {
                            indices.Add(d_offs + (y * (uint)side + x));
                            indices.Add(d_offs + ((y + 1) * (uint)side + x));
                        }
                    }
                    groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, wi_start, (uint)(indices.Count - wi_start)));
                }
                else
                {
                    uint d_offs = (uint)verts.Count;
                    verts.AddRange(SelectedFace.Vertices.Select(x => new VertexStandard
                    {
                        Position = x,
                        Colour = Color.Red.ToVector4(),
                        Tint = selectionColour,
                        Flags = VertexFlags.FlatColour
                    }));

                    uint si_start = (uint)indices.Count;
                    for (uint i = 2; i < (uint)SelectedFace.Vertices.Count; i++)
                    {
                        indices.Add(d_offs);
                        indices.Add(d_offs + i - 1);
                        indices.Add(d_offs + i);
                    }
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, SelectedFace.Origin, si_start, (uint)(indices.Count - si_start)));

                    uint wi_start = (uint)indices.Count;
                    for (uint i = 0; i < (uint)SelectedFace.Vertices.Count; i++)
                    {
                        indices.Add(d_offs + i);
                        indices.Add(d_offs + (i + 1) % (uint)SelectedFace.Vertices.Count);
                    }
                    groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, wi_start, (uint)(indices.Count - wi_start)));
                }

                builder.Append(verts, indices, groups);
            }
        }
    }
}