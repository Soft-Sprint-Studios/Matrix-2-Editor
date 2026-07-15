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
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.Common.Shell.Components;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;

namespace Sledge.BspEditor.Tools.Sprinkle
{
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("U")]
    [AutoTranslate]
    public class SprinkleTool : BaseTool
    {
        public override Image GetIcon() => Properties.Resources.Tool_Sprinkle;
        public override string GetName() => "Sprinkle Tool";

        public SprinkleDefinition ActiveDefinition { get; set; }
        public float Radius { get; set; } = 256f;
        public float Density { get; set; } = 0.25f;
        public bool RandomYaw { get; set; } = true;

        private Vector3? _hitPoint;
        private Vector3? _hitNormal;
        private bool _isPainting;
        private readonly Random _rand = new Random();

        public SprinkleTool()
        {
            Usage = ToolUsage.Both;
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isPainting = true;
                viewport.AquireInputLock(this);
                PerformSprinkle(document);
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isPainting = false;
                viewport.ReleaseInputLock(this);
            }
        }

        protected override void MouseWheel(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (KeyboardState.Ctrl)
            {
                float delta = e.Delta > 0 ? 32f : -32f;
                Radius = Math.Max(16f, Radius + delta);

                Oy.Publish("SprinkleTool:RadiusChanged", Radius);
                
                e.Handled = true;
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            var hit = document.Map.Root.GetIntersectionsForVisibleObjects(ray).FirstOrDefault();
            if (hit != null)
            {
                _hitPoint = hit.Intersection;
                _hitNormal = Vector3.UnitZ;
                
                if (_isPainting) PerformSprinkle(document);
            }
            else
            {
                _hitPoint = null;
            }
        }

        private void PerformSprinkle(MapDocument document)
        {
            if (ActiveDefinition == null || !_hitPoint.HasValue)
                return;

            int attempts = (int)(Radius / 8f); 
            var trans = new Transaction();

            for (int i = 0; i < attempts; i++)
            {
                if (_rand.NextDouble() > Density) 
                    continue;

                float angle = (float)(_rand.NextDouble() * Math.PI * 2);
                float dist = (float)(_rand.NextDouble() * Radius);
                Vector3 offset = new Vector3((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist, 0);

                Vector3 traceStart = _hitPoint.Value + offset + (Vector3.UnitZ);
                Vector3 traceEnd = _hitPoint.Value + offset - (Vector3.UnitZ);
                var traceRay = new Line(traceStart, traceEnd);

                var floorHit = document.Map.Root.GetIntersectionsForVisibleObjects(traceRay).FirstOrDefault();

                if (floorHit != null)
                {
                    CreateEntity(document, trans, floorHit.Intersection);
                }
            }

            if (!trans.IsEmpty)
            {
                MapDocumentOperation.Perform(document, trans);
            }
        }

        private void CreateEntity(MapDocument doc, Transaction trans, Vector3 pos)
        {
            var ent = new Primitives.MapObjects.Entity(doc.Map.NumberGenerator.Next("MapObject"))
            {
                Data = { new Origin(pos), new ObjectColor(Color.White) }
            };

            var data = new EntityData();

            void ApplyProperty(string key, string value)
            {
                if (key.ToLower() == "classname") 
                    data.Name = value;
                else 
                    data.Properties[key] = value;
            }

            foreach (var p in ActiveDefinition.BaseProperties)
                ApplyProperty(p.Key, p.Value);

            foreach (var p in ActiveDefinition.GetRandomProperties(_rand))
                ApplyProperty(p.Key, p.Value);

            ent.Data.Add(data);
            
            if (RandomYaw)
            {
                int yaw = _rand.Next(0, 360);
                data.Properties["angles"] = $"0 {yaw} 0";
            }

            trans.Add(new Attach(doc.Map.Root.ID, ent));
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            if (_hitPoint.HasValue)
            {
                var verts = new List<VertexStandard>();
                var indices = new List<uint>();
                var center = _hitPoint.Value;
                var tint = new Vector4(0, 1, 1, 1);

                uint startIdx = (uint)verts.Count;
                int segments = 64;
                for (int i = 0; i < segments; i++)
                {
                    float angle = (i / (float)segments) * MathF.PI * 2;
                    var pt = center + new Vector3(MathF.Cos(angle) * Radius, MathF.Sin(angle) * Radius, 2);
                    verts.Add(new VertexStandard { Position = pt, Colour = Vector4.One, Tint = tint });
                    indices.Add(startIdx + (uint)i);
                    indices.Add(startIdx + (uint)((i + 1) % segments));
                }

                builder.Append(verts, indices, new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, 0, (uint)indices.Count) });
            }
        }
    }
}