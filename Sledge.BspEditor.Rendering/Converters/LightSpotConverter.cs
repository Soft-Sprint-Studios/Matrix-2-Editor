using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
    [Export(typeof(IMapObjectSceneConverter))]
    public class LightSpotConverter : IMapObjectSceneConverter
    {
        public MapObjectSceneConverterPriority Priority => MapObjectSceneConverterPriority.DefaultLow;

        public bool ShouldStopProcessing(MapDocument document, IMapObject obj)
        {
            return false;
        }

        public bool Supports(IMapObject obj)
        {
            return obj is Entity e && e.EntityData.Name.ToLower() == "light_spot";
        }

        public Task Convert(BufferBuilder builder, MapDocument document, IMapObject obj, ResourceCollector resourceCollector)
        {
            var entity = (Entity)obj;
            var origin = entity.Origin;

            var angles = entity.EntityData.GetVector3("angles") ?? Vector3.Zero;
            var angle = entity.EntityData.Get<float>("angle", 0f);
            var pitch = entity.EntityData.Get<float>("pitch", 0f);

            Vector3 normal = Vector3.Zero;

            if (angle == -1)
            {
                normal = Vector3.UnitZ;
            }
            else if (angle == -2)
            {
                normal = -Vector3.UnitZ;
            }
            else
            {
                if (angle == 0) angle = angles.Y;

                normal.X = MathF.Cos(angle * MathF.PI / 180f);
                normal.Y = MathF.Sin(angle * MathF.PI / 180f);
                normal.Z = 0;
            }

            if (pitch == 0) pitch = angles.X;

            normal.Z = MathF.Sin(pitch * MathF.PI / 180f);
            var cosPitch = MathF.Cos(pitch * MathF.PI / 180f);
            normal.X *= cosPitch;
            normal.Y *= cosPitch;

            normal = Vector3.Normalize(normal);

            var verts = new List<VertexStandard>();
            var indices = new List<uint>();

            var lineEnd = origin + normal * 40f;
            verts.Add(new VertexStandard { Position = origin, Colour = Vector4.One, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = lineEnd, Colour = Vector4.One, Tint = Vector4.One });
            indices.Add(0);
            indices.Add(1);

            if (obj.IsSelected)
            {
                var cone1 = entity.EntityData.Get<float>("_cone", 30f);
                var cone2 = entity.EntityData.Get<float>("_cone2", 45f);

                var right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));
                if (right.LengthSquared() < 0.01f) right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
                var up = Vector3.Normalize(Vector3.Cross(normal, right));

                var lightProp = entity.EntityData.Get<string>("_light", "255 255 255 200");
                var spl = lightProp.Split(' ');
                var color = new Vector4(1f, 1f, 1f, 1f);
                if (spl.Length >= 3)
                {
                    if (float.TryParse(spl[0], out var r) && float.TryParse(spl[1], out var g) && float.TryParse(spl[2], out var b))
                    {
                        color = new Vector4(r, g, b, 255f) / 255.0f;
                    }
                }

                GenerateConeGeometry(origin, normal, right, up, cone1, color, verts, indices);
                GenerateConeGeometry(origin, normal, right, up, cone2, color * new Vector4(0.7f, 0.7f, 0.7f, 0.5f), verts, indices);
            }

            builder.Append(verts, indices.Select(x => (uint)x), new[]
            {
                new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, (uint)indices.Count)
            });

            return Task.CompletedTask;
        }

        private void GenerateConeGeometry(Vector3 origin, Vector3 normal, Vector3 right, Vector3 up, float coneAngle, Vector4 color, List<VertexStandard> verts, List<uint> indices)
        {
            const float distance = 150f;
            float rad = coneAngle * MathF.PI / 180f;
            float radius = distance * MathF.Tan(rad);

            var endCenter = origin + normal * distance;
            const int segments = 12;

            uint baseOffset = (uint)verts.Count;

            verts.Add(new VertexStandard { Position = origin, Colour = color, Tint = Vector4.One });

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * MathF.PI * 2f;
                var pt = endCenter + right * radius * MathF.Cos(angle) + up * radius * MathF.Sin(angle);

                verts.Add(new VertexStandard { Position = pt, Colour = color, Tint = Vector4.One });

                indices.Add(baseOffset);
                indices.Add(baseOffset + 1 + (uint)i);

                indices.Add(baseOffset + 1 + (uint)i);
                indices.Add(baseOffset + 1 + (uint)((i + 1) % segments));
            }
        }
    }
}