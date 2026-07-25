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
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Modification.Operations;

namespace Sledge.BspEditor.Tools.Displacement
{
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("V")]
    [DefaultHotkey("Shift+Y")]
    public class DisplacementTool : BaseTool
    {
        public DisplacementTool()
        {
            Usage = ToolUsage.View3D;
        }

        public override Image GetIcon() => Properties.Resources.Tool_Displacement;
        public override string GetName() => "Displacement Tool";

        public Face SelectedFace { get; set; }
        public Solid SelectedSolid { get; set; }

        private Face _originalFace;
        private Face _paintingFace;
        private Solid _paintingSolid;

        private Vector3? GetDisplacementIntersection(Face face, Line ray)
        {
            return face.GetIntersectionPoint(ray);
        }

        public int PaintRadius { get; set; } = 32;
        public float PaintAmount { get; set; } = 5f;
        public bool IsPainting { get; set; } = false;

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            var clicked = document.Map.Root.GetBoudingBoxIntersectionsForVisibleObjects(ray)
                .OfType<Solid>()
                .SelectMany(a => a.Faces.Select(f => new { Face = f, Solid = a }))
                .Select(x => new { x.Face, x.Solid, Intersection = GetDisplacementIntersection(x.Face, ray) })
                .Where(x => x.Intersection != null)
                .OrderBy(x => (x.Intersection.Value - ray.Start).LengthSquared())
                .FirstOrDefault();

            if (clicked == null) return;

            var liveFace = clicked.Solid.Faces.FirstOrDefault(f => f.ID == clicked.Face.ID);
            if (liveFace == null) return;

            if (IsPainting && liveFace.Displacement != null && liveFace.Vertices.Count >= 4)
            {
                _originalFace = (Face)liveFace.Clone();
                _paintingFace = liveFace;
                _paintingSolid = clicked.Solid;

                PaintDisplacement(_paintingFace, clicked.Intersection.Value, e.Button == MouseButtons.Left ? PaintAmount : -PaintAmount);
                _paintingSolid.DescendantsChanged();

                e.Handled = true;
                viewport.AquireInputLock(this);
            }
            else if (e.Button == MouseButtons.Left)
            {
                SelectedFace = liveFace;
                SelectedSolid = clicked.Solid;
                Oy.Publish("DisplacementTool:FaceSelected", this);
                e.Handled = true;
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (!IsPainting || _paintingFace == null || _paintingSolid == null) return;
            if (!Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) && !Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.RButton)) return;

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            var intersection = GetDisplacementIntersection(_paintingFace, ray);
            if (intersection != null)
            {
                PaintDisplacement(_paintingFace, intersection.Value, Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) ? PaintAmount : -PaintAmount);
                _paintingSolid.DescendantsChanged();
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_paintingFace != null && _paintingSolid != null)
            {
                var finalFace = (Face)_paintingFace.Clone();

                // Restore original face so Sledge's delta calculator operates correctly
                _paintingSolid.Data.Remove(_paintingFace);
                _paintingSolid.Data.Add(_originalFace);

                // Commit the entire paint stroke as a single undoable transaction
                MapDocumentOperation.Perform(document, new Transaction(
                    new RemoveMapObjectData(_paintingSolid.ID, _originalFace),
                    new AddMapObjectData(_paintingSolid.ID, finalFace)
                ));

                SelectedFace = finalFace;
                _paintingFace = null;
                _paintingSolid = null;
                _originalFace = null;
            }
            viewport.ReleaseInputLock(this);
        }

        private void PaintDisplacement(Face face, Vector3 hit, float amount)
        {
            int power = face.Displacement.Power;
            int side = (1 << power) + 1;
            var corners = face.Displacement.Corners.ToList();

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    float fr_x = (float)x / (side - 1);
                    float fr_y = (float)y / (side - 1);
                    var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                    var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                    var pos = Vector3.Lerp(top, bot, fr_y) + face.Plane.Normal * face.Displacement.Distances[y * side + x];

                    float dist = (pos - hit).Length();
                    if (dist < PaintRadius)
                    {
                        float falloff = 1.0f - (dist / PaintRadius);
                        face.Displacement.Distances[y * side + x] += amount * falloff;
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
                            var pos = Vector3.Lerp(top, bot, fr_y) + SelectedFace.Plane.Normal * SelectedFace.Displacement.Distances[y * side + x];

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
                    for (uint y = 0; y < side - 1; y++)
                    {
                        for (uint x = 0; x < side - 1; x++)
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
                    for (uint y = 0; y < side; y++)
                    {
                        for (uint x = 0; x < side - 1; x++)
                        {
                            indices.Add(d_offs + (y * (uint)side + x));
                            indices.Add(d_offs + (y * (uint)side + (x + 1)));
                        }
                    }
                    for (uint y = 0; y < side - 1; y++)
                    {
                        for (uint x = 0; x < side; x++)
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
                    for (uint i = 2; i < SelectedFace.Vertices.Count; i++)
                    {
                        indices.Add(d_offs);
                        indices.Add(d_offs + i - 1);
                        indices.Add(d_offs + i);
                    }
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, SelectedFace.Origin, si_start, (uint)(indices.Count - si_start)));

                    uint wi_start = (uint)indices.Count;
                    for (uint i = 0; i < SelectedFace.Vertices.Count; i++)
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