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

        public List<(Solid Solid, Face Face)> SelectedFaces { get; set; } = new List<(Solid Solid, Face Face)>();

        public Face SelectedFace
        {
            get => SelectedFaces.FirstOrDefault().Face;
            set
            {
                SelectedFaces.Clear();
                if (value != null && SelectedSolid != null) SelectedFaces.Add((SelectedSolid, value));
            }
        }

        public Solid SelectedSolid
        {
            get => SelectedFaces.FirstOrDefault().Solid;
            set
            {
                if (SelectedFaces.Count > 0 && value != null)
                {
                    var face = SelectedFaces[0].Face;
                    SelectedFaces[0] = (value, face);
                }
            }
        }

        private List<(Solid Solid, Face Face, Face Original)> _originalFaceStates;
        private List<(Solid Solid, Face Face)> _activePaintingFaces;
        private Vector3? _hitPoint;

        public int PaintRadius { get; set; } = 64;
        public float PaintAmount { get; set; } = 5f;
        public bool IsPainting { get; set; } = false;
        public DisplacementPaintAxis PaintAxis { get; set; } = DisplacementPaintAxis.FaceNormal;
        public DisplacementSculptMode SculptMode { get; set; } = DisplacementSculptMode.RaiseLower;
        private readonly Random _rand = new Random();

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<RightClickMenuBuilder>("MapViewport:RightClick", b =>
            {
                b.Intercepted = true;
            });

            yield return Oy.Subscribe<Change>("MapDocument:Changed", DocumentChanged);
        }

        private Task DocumentChanged(Change change)
        {
            bool needsPublish = false;
            for (int i = 0; i < SelectedFaces.Count; i++)
            {
                var (solid, face) = SelectedFaces[i];
                var currentSolid = change.Document.Map.Root.FindByID(solid.ID) as Solid;

                if (currentSolid != null)
                {
                    var currentFace = currentSolid.Faces.FirstOrDefault(f => f.ID == face.ID);
                    if (currentFace != null && currentFace != face)
                    {
                        SelectedFaces[i] = (currentSolid, currentFace);
                        needsPublish = true;
                    }
                }
                else if (change.Removed.Contains(solid))
                {
                    SelectedFaces.RemoveAt(i);
                    i--;
                    needsPublish = true;
                }
            }

            if (needsPublish) Oy.Publish("DisplacementTool:FaceSelected", this);
            return Task.CompletedTask;
        }

        public override async Task ToolSelected()
        {
            var document = GetDocument();
            if (document != null)
            {
                await MapDocumentOperation.Bypass(document, new Deselect(document.Selection));
            }

            SelectedFaces.Clear();
            IsPainting = false;
            Oy.Publish("DisplacementTool:FaceSelected", this);

            await base.ToolSelected();
        }

        public override async Task ToolDeselected()
        {
            SelectedFaces.Clear();
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

            if (IsPainting)
            {
                if (!SelectedFaces.Any(x => x.Face == clicked.Face) && !Sledge.Shell.Input.KeyboardState.Ctrl)
                {
                    if (clicked.Face.Displacement != null)
                    {
                        SelectedFaces.Clear();
                        SelectedFaces.Add((clicked.Solid, clicked.Face));
                        Oy.Publish("DisplacementTool:FaceSelected", this);
                    }
                }

                var paintingTargets = SelectedFaces.Where(x => x.Face.Displacement != null).ToList();
                if (!paintingTargets.Any() && clicked.Face.Displacement != null)
                {
                    paintingTargets.Add((clicked.Solid, clicked.Face));
                }

                if (paintingTargets.Any())
                {
                    _activePaintingFaces = paintingTargets;
                    _originalFaceStates = paintingTargets.Select(x => (x.Solid, x.Face, Original: (Face)x.Face.Clone())).ToList();
                    _hitPoint = clicked.Intersection.Value;

                    float amount = e.Button == MouseButtons.Left ? PaintAmount : -PaintAmount;
                    foreach (var target in _activePaintingFaces)
                    {
                        PaintDisplacement(target.Face, clicked.Intersection.Value, amount);
                        target.Solid.DescendantsChanged();
                    }

                    var updatedSolids = _activePaintingFaces.Select(x => x.Solid).Distinct();
                    foreach (var solid in updatedSolids)
                    {
                        Oy.Publish("MapDocument:Changed", new Change(document).Update(solid));
                    }

                    e.Handled = true;
                    viewport.AquireInputLock(this);
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (Sledge.Shell.Input.KeyboardState.Ctrl)
                {
                    var existing = SelectedFaces.FirstOrDefault(x => x.Face == clicked.Face);
                    if (existing.Face != null)
                    {
                        SelectedFaces.Remove(existing);
                    }
                    else
                    {
                        SelectedFaces.Add((clicked.Solid, clicked.Face));
                    }
                }
                else
                {
                    SelectedFaces.Clear();
                    SelectedFaces.Add((clicked.Solid, clicked.Face));
                }
                Oy.Publish("DisplacementTool:FaceSelected", this);
                e.Handled = true;
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (!IsPainting || _activePaintingFaces == null || !_activePaintingFaces.Any()) return;
            if (!Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) && !Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.RButton)) return;

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            Vector3? intersection = null;
            foreach (var target in _activePaintingFaces)
            {
                var isect = target.Face.GetIntersectionPoint(ray);
                if (isect != null)
                {
                    intersection = isect;
                    break;
                }
            }

            if (intersection == null && _hitPoint.HasValue)
            {
                intersection = _hitPoint.Value;
            }
            else if (intersection != null)
            {
                _hitPoint = intersection.Value;
            }

            if (intersection != null)
            {
                float amount = Sledge.Shell.Input.KeyboardState.IsKeyDown(Keys.LButton) ? PaintAmount : -PaintAmount;
                foreach (var target in _activePaintingFaces)
                {
                    PaintDisplacement(target.Face, intersection.Value, amount);
                    target.Solid.DescendantsChanged();
                }

                var updatedSolids = _activePaintingFaces.Select(x => x.Solid).Distinct();
                foreach (var solid in updatedSolids)
                {
                    Oy.Publish("MapDocument:Changed", new Change(document).Update(solid));
                }
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_activePaintingFaces != null && _activePaintingFaces.Any())
            {
                var transaction = new Transaction();
                var newSelectedFaces = new List<(Solid Solid, Face Face)>();

                var faceMap = new Dictionary<Face, Face>();

                foreach (var (solid, face, original) in _originalFaceStates)
                {
                    var finishedFace = (Face)face.Clone();

                    face.Texture = original.Texture.Clone();
                    face.Displacement = original.Displacement?.Clone();
                    face.Vertices.Clear();
                    face.Vertices.AddRange(original.Vertices);
                    solid.DescendantsChanged();

                    transaction.Add(new RemoveMapObjectData(solid.ID, face));
                    transaction.Add(new AddMapObjectData(solid.ID, finishedFace));

                    newSelectedFaces.Add((solid, finishedFace));
                    faceMap[face] = finishedFace;
                }

                MapDocumentOperation.Perform(document, transaction);

                for (int i = 0; i < SelectedFaces.Count; i++)
                {
                    if (faceMap.TryGetValue(SelectedFaces[i].Face, out var newFace))
                    {
                        SelectedFaces[i] = (SelectedFaces[i].Solid, newFace);
                    }
                }

                _activePaintingFaces = null;
                _originalFaceStates = null;
            }
            _hitPoint = null;
            viewport.ReleaseInputLock(this);
        }

        public void ApplyNoise(float min, float max)
        {
            var doc = GetDocument();
            if (doc == null || SelectedFaces.Count == 0) return;

            var transaction = new Transaction();
            var newSelectedFaces = new List<(Solid Solid, Face Face)>();
            bool changed = false;

            foreach (var (solid, face) in SelectedFaces)
            {
                if (face.Displacement == null)
                {
                    newSelectedFaces.Add((solid, face));
                    continue;
                }

                if (!solid.Faces.Contains(face)) continue;

                var clone = (Face)face.Clone();
                int power = clone.Displacement.Power;
                int side = (1 << power) + 1;
                int totalVerts = side * side;

                for (int i = 0; i < totalVerts; i++)
                {
                    float noise = min + (float)_rand.NextDouble() * (max - min);
                    clone.Displacement.Distances[i] += noise;
                    if (clone.Displacement.Distances[i] < 0)
                    {
                        clone.Displacement.Distances[i] = -clone.Displacement.Distances[i];
                        clone.Displacement.Vectors[i] = -clone.Displacement.Vectors[i];
                    }
                }
                changed = true;

                transaction.Add(new RemoveMapObjectData(solid.ID, face));
                transaction.Add(new AddMapObjectData(solid.ID, clone));
                newSelectedFaces.Add((solid, clone));
            }

            if (changed && !transaction.IsEmpty)
            {
                MapDocumentOperation.Perform(doc, transaction);
                SelectedFaces = newSelectedFaces;
                Oy.Publish("DisplacementTool:FaceSelected", this);
            }
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

        public void SewSelectedDisplacements()
        {
            var doc = GetDocument();
            if (doc == null || SelectedFaces.Count < 2) return;

            var transaction = new Transaction();
            var newSelectedFaces = new List<(Solid Solid, Face Face)>();

            var clones = new Dictionary<Face, Face>();
            foreach (var (solid, face) in SelectedFaces)
            {
                if (face.Displacement == null) continue;
                var clone = (Face)face.Clone();
                if (face.Displacement != null) clone.Displacement = face.Displacement.Clone();
                clones[face] = clone;
            }

            var points = new List<DispPoint>();
            foreach (var kvp in clones)
            {
                var face = kvp.Value;
                var disp = face.Displacement;
                int side = (1 << disp.Power) + 1;
                for (int y = 0; y < side; y++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        if (x == 0 || x == side - 1 || y == 0 || y == side - 1)
                        {
                            float fr_x = (float)x / (side - 1);
                            float fr_y = (float)y / (side - 1);
                            var top = Vector3.Lerp(disp.Corners[0], disp.Corners[1], fr_x);
                            var bot = Vector3.Lerp(disp.Corners[3], disp.Corners[2], fr_x);
                            var basePos = Vector3.Lerp(top, bot, fr_y);

                            points.Add(new DispPoint
                            {
                                Face = face,
                                X = x,
                                Y = y,
                                BasePos = basePos,
                                Offset = disp.Vectors[y * side + x] * disp.Distances[y * side + x]
                            });
                        }
                    }
                }
            }

            var tolerance = 1.0f;
            var groups = new List<List<DispPoint>>();
            foreach (var pt in points)
            {
                var group = groups.FirstOrDefault(g => (g[0].BasePos - pt.BasePos).Length() < tolerance);
                if (group == null)
                {
                    group = new List<DispPoint>();
                    groups.Add(group);
                }
                group.Add(pt);
            }

            bool changed = false;
            foreach (var group in groups.Where(g => g.Count > 1))
            {
                if (group.Select(p => p.Face).Distinct().Count() > 1)
                {
                    var avgOffset = group.Aggregate(Vector3.Zero, (sum, p) => sum + p.Offset) / group.Count;
                    var dist = avgOffset.Length();
                    var vec = dist > 0.001f ? Vector3.Normalize(avgOffset) : Vector3.UnitZ;

                    foreach (var pt in group)
                    {
                        var disp = pt.Face.Displacement;
                        int idx = pt.Y * ((1 << disp.Power) + 1) + pt.X;

                        if ((disp.Vectors[idx] * disp.Distances[idx] - avgOffset).Length() > 0.01f)
                        {
                            disp.Distances[idx] = dist;
                            if (dist > 0.001f) disp.Vectors[idx] = vec;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                foreach (var (solid, face) in SelectedFaces)
                {
                    if (!clones.ContainsKey(face))
                    {
                        newSelectedFaces.Add((solid, face));
                        continue;
                    }

                    var clone = clones[face];
                    transaction.Add(new RemoveMapObjectData(solid.ID, face));
                    transaction.Add(new AddMapObjectData(solid.ID, clone));
                    newSelectedFaces.Add((solid, clone));
                }

                MapDocumentOperation.Perform(doc, transaction);
                SelectedFaces = newSelectedFaces;
                Oy.Publish("DisplacementTool:FaceSelected", this);
            }
        }
        public void InvertSelectedDisplacementAlphas()
        {
            var doc = GetDocument();
            if (doc == null || SelectedFaces.Count == 0) return;

            var transaction = new Transaction();
            var newSelectedFaces = new List<(Solid Solid, Face Face)>();
            bool changed = false;

            foreach (var (solid, face) in SelectedFaces)
            {
                if (face.Displacement == null)
                {
                    newSelectedFaces.Add((solid, face));
                    continue;
                }

                var clone = (Face)face.Clone();
                if (clone.Displacement.Alphas != null)
                {
                    for (int i = 0; i < clone.Displacement.Alphas.Length; i++)
                    {
                        clone.Displacement.Alphas[i] = Math.Clamp(255f - clone.Displacement.Alphas[i], 0f, 255f);
                    }
                    changed = true;
                }
                else
                {
                    int totalVerts = ((1 << clone.Displacement.Power) + 1) * ((1 << clone.Displacement.Power) + 1);
                    clone.Displacement.Alphas = new float[totalVerts];
                    Array.Fill(clone.Displacement.Alphas, 255f);
                    changed = true;
                }

                transaction.Add(new RemoveMapObjectData(solid.ID, face));
                transaction.Add(new AddMapObjectData(solid.ID, clone));
                newSelectedFaces.Add((solid, clone));
            }

            if (changed && !transaction.IsEmpty)
            {
                MapDocumentOperation.Perform(doc, transaction);
                SelectedFaces = newSelectedFaces;
                Oy.Publish("DisplacementTool:FaceSelected", this);
            }
        }

        class DispPoint
        {
            public Face Face;
            public int X, Y;
            public Vector3 BasePos;
            public Vector3 Offset;
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);
            if (SelectedFaces.Count == 0) return;

            var verts = new List<VertexStandard>();
            var indices = new List<uint>();
            var groups = new List<BufferGroup>();
            var selectionColour = Color.FromArgb(64, Color.Red).ToVector4();

            foreach (var (solid, face) in SelectedFaces)
            {
                if (face == null) continue;

                if (face.Displacement != null && face.Vertices.Count >= 4)
                {
                    int power = face.Displacement.Power;
                    int side = (1 << power) + 1;
                    var corners = face.Displacement.Corners.ToList();

                    uint d_offs = (uint)verts.Count;

                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            float fr_x = (float)x / (side - 1);
                            float fr_y = (float)y / (side - 1);
                            var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                            var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                            var pos = Vector3.Lerp(top, bot, fr_y) + face.Displacement.Vectors[y * side + x] * face.Displacement.Distances[y * side + x];

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
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, face.Origin, si_start, (uint)(indices.Count - si_start)));

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
                    verts.AddRange(face.Vertices.Select(x => new VertexStandard
                    {
                        Position = x,
                        Colour = Color.Red.ToVector4(),
                        Tint = selectionColour,
                        Flags = VertexFlags.FlatColour
                    }));

                    uint si_start = (uint)indices.Count;
                    for (uint i = 2; i < (uint)face.Vertices.Count; i++)
                    {
                        indices.Add(d_offs);
                        indices.Add(d_offs + i - 1);
                        indices.Add(d_offs + i);
                    }
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, face.Origin, si_start, (uint)(indices.Count - si_start)));

                    uint wi_start = (uint)indices.Count;
                    for (uint i = 0; i < (uint)face.Vertices.Count; i++)
                    {
                        indices.Add(d_offs + i);
                        indices.Add(d_offs + (i + 1) % (uint)face.Vertices.Count);
                    }
                    groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, wi_start, (uint)(indices.Count - wi_start)));
                }
            }

            builder.Append(verts, indices, groups);
        }
    }
}