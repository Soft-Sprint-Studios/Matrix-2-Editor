using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Transport;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives.MapObjects
{
    /// <summary>
    /// A collection of faces
    /// </summary>
    public class Solid : BaseMapObject
    {
        public IEnumerable<Face> Faces => Data.Get<Face>();
        public ObjectColor Color => Data.GetOne<ObjectColor>();

        public Solid(long id) : base(id)
        {
        }

        public Solid(SerialisedObject obj) : base(obj)
        {
        }

        [Export(typeof(IMapElementFormatter))]
        public class SolidFormatter : StandardMapElementFormatter<Solid> { }

        protected override Box GetBoundingBox()
        {
            var faces = Faces.ToList();
            var points = faces.SelectMany(x => x.Vertices).ToList();
            foreach (var f in faces)
            {
                if (f.Displacement != null && f.Vertices.Count >= 4)
                {
                    int power = f.Displacement.Power;
                    int side = (1 << power) + 1;
                    var corners = f.Displacement.Corners.ToList();
                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            float fr_x = (float)x / (side - 1);
                            float fr_y = (float)y / (side - 1);
                            var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                            var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                            var pos = Vector3.Lerp(top, bot, fr_y) + f.Displacement.Vectors[y * side + x] * f.Displacement.Distances[y * side + x];
                            points.Add(pos);
                        }
                    }
                }
            }
            return points.Any() ? new Box(points) : Box.Empty;
        }

        public override IEnumerable<Polygon> GetPolygons()
        {
            return Faces.Select(x => x.ToPolygon());
        }

        public Polyhedron ToPolyhedron()
        {
            return new Polyhedron(GetPolygons());
        }

        protected override string SerialisedName => "Solid";

        public override IEnumerable<IMapObject> Decompose(IEnumerable<Type> allowedTypes)
        {
            yield return this;
        }
    }
}