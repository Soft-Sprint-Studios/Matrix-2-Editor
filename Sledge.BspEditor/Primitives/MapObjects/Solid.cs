using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
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
                    float minD = f.Displacement.Distances.Min();
                    float maxD = f.Displacement.Distances.Max();
                    if (minD < 0) points.Add(f.Origin + f.Plane.Normal * minD);
                    if (maxD > 0) points.Add(f.Origin + f.Plane.Normal * maxD);
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