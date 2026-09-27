using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Primitives
{
    public static class SolidExtensions
    {
        public static bool Split(this Solid solid, UniqueNumberGenerator generator, Plane plane, out Solid back, out Solid front)
        {
            back = front = null;
            var pln = plane.ToPrecisionPlane();
            var poly = solid.ToPolyhedron().ToPrecisionPolyhedron();

            if (!poly.Split(pln, out var backPoly, out var frontPoly))
            {
                if (backPoly != null) back = solid;
                else if (frontPoly != null) front = solid;
                return false;
            }

            front = MakeSolid(generator, solid, frontPoly);
            back = MakeSolid(generator, solid, backPoly);
            return true;
        }

        private static Solid MakeSolid(UniqueNumberGenerator generator, Solid original, DataStructures.Geometric.Precision.Polyhedron poly)
        {
            var originalFacePlanes = original.Faces.Select(x => new
            {
                Face = x,
                Plane = x.Plane.ToPrecisionPlane()
            }).ToList();

            var solid = new Solid(generator.Next("MapObject")) { IsSelected = original.IsSelected };
            foreach (var p in poly.Polygons)
            {
                // Try and find the face with the same plane, so we can duplicate the texture values
                var originalFace = originalFacePlanes
                    .Where(x => p.ClassifyAgainstPlane(x.Plane) == PlaneClassification.OnPlane)
                    .Select(x => x.Face)
                    .FirstOrDefault();

                var face = new Face(generator.Next("Face"));
                face.Vertices.AddRange(p.Vertices.Select(x => x.ToStandardVector3()));
                face.Plane = p.Plane.ToStandardPlane();

                if (originalFace != null)
                {
                    // The plane exists, so we can just apply the texture axes directly
                    face.Texture = originalFace.Texture.Clone();
                    if (originalFace.Displacement != null && face.Vertices.Count == 4)
                    {
                        face.Displacement = CreateSubDisplacement(originalFace, face.Vertices, out var alignedCorners);
                        if (alignedCorners != null)
                        {
                            face.Vertices.Clear();
                            face.Vertices.AddRange(alignedCorners);
                            face.Plane = p.Plane.ToStandardPlane();
                        }
                    }
                }
                else
                {
                    // No matching plane exists, so it's the clipping plane.
                    // Apply the first texture we find and align it with the face.
                    var firstFace = originalFacePlanes[0].Face;
                    face.Texture = firstFace.Texture.Clone();
                    face.Texture.AlignToNormal(face.Plane.Normal);
                }

                solid.Data.Add(face);
            }

            // Add any extra data (visgroups, colour, etc)
            foreach (var data in original.Data.Where(x => !(x is Face)))
            {
                solid.Data.Add((IMapObjectData)data.Clone());
            }
            solid.DescendantsChanged();
            return solid;
        }

        public static bool IsValid(this Solid solid)
        {
            return solid.ToPolyhedron().IsValid();
        }

        private static Displacement CreateSubDisplacement(Face origFace, IList<Vector3> newVerts, out Vector3[] alignedCorners)
        {
            alignedCorners = null;
            var origDisp = origFace.Displacement;
            if (origDisp == null) return null;

            var origCorners = (origDisp.Corners != null && origDisp.Corners.Length == 4 && origDisp.Corners.Any(c => c != Vector3.Zero))
                ? origDisp.Corners
                : (origFace.Vertices.Count == 4 ? origFace.Vertices.ToArray() : null);

            if (origCorners == null) return null;

            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++) uvs[i] = GetUVOnQuad(origCorners, newVerts[i]);

            int startIdx = 0;
            float minLenSq = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float lenSq = uvs[i].X * uvs[i].X + uvs[i].Y * uvs[i].Y;
                if (lenSq < minLenSq)
                {
                    minLenSq = lenSq;
                    startIdx = i;
                }
            }

            alignedCorners = new[]
            {
                newVerts[startIdx],
                newVerts[(startIdx + 1) % 4],
                newVerts[(startIdx + 2) % 4],
                newVerts[(startIdx + 3) % 4]
            };

            var newDisp = new Displacement(origDisp.Power, alignedCorners);
            newDisp.Texture2Name = origDisp.Texture2Name ?? "";

            int newSide = (1 << newDisp.Power) + 1;
            int origSide = (1 << origDisp.Power) + 1;

            for (int ny = 0; ny < newSide; ny++)
            {
                for (int nx = 0; nx < newSide; nx++)
                {
                    float fx = (float)nx / (newSide - 1);
                    float fy = (float)ny / (newSide - 1);

                    var top = Vector3.Lerp(alignedCorners[0], alignedCorners[1], fx);
                    var bot = Vector3.Lerp(alignedCorners[3], alignedCorners[2], fx);
                    var basePt = Vector3.Lerp(top, bot, fy);

                    var uv = GetUVOnQuad(origCorners, basePt);

                    float gx = Math.Clamp(uv.X * (origSide - 1), 0f, origSide - 1);
                    float gy = Math.Clamp(uv.Y * (origSide - 1), 0f, origSide - 1);

                    int x0 = (int)MathF.Floor(gx);
                    int x1 = Math.Min(x0 + 1, origSide - 1);
                    int y0 = (int)MathF.Floor(gy);
                    int y1 = Math.Min(y0 + 1, origSide - 1);

                    float rx = gx - x0;
                    float ry = gy - y0;

                    Vector3 o00 = GetDispOffset(origDisp, origSide, x0, y0);
                    Vector3 o10 = GetDispOffset(origDisp, origSide, x1, y0);
                    Vector3 o01 = GetDispOffset(origDisp, origSide, x0, y1);
                    Vector3 o11 = GetDispOffset(origDisp, origSide, x1, y1);
                    Vector3 interpOffset = Vector3.Lerp(Vector3.Lerp(o00, o10, rx), Vector3.Lerp(o01, o11, rx), ry);

                    float a00 = GetDispAlpha(origDisp, origSide, x0, y0);
                    float a10 = GetDispAlpha(origDisp, origSide, x1, y0);
                    float a01 = GetDispAlpha(origDisp, origSide, x0, y1);
                    float a11 = GetDispAlpha(origDisp, origSide, x1, y1);
                    float interpAlpha = (1 - ry) * ((1 - rx) * a00 + rx * a10) + ry * ((1 - rx) * a01 + rx * a11);

                    int nidx = ny * newSide + nx;
                    float dist = interpOffset.Length();
                    newDisp.Distances[nidx] = dist;
                    newDisp.Vectors[nidx] = dist > 0.0001f ? Vector3.Normalize(interpOffset) : Vector3.UnitZ;
                    newDisp.Alphas[nidx] = Math.Clamp(interpAlpha, 0f, 255f);
                }
            }

            return newDisp;
        }

        private static Vector2 GetUVOnQuad(Vector3[] c, Vector3 p)
        {
            var normal = Vector3.Normalize(Vector3.Cross(c[1] - c[0], c[2] - c[0]));
            if (normal.LengthSquared() < 0.001f)
                normal = Vector3.Normalize(Vector3.Cross(c[2] - c[0], c[3] - c[0]));

            var absN = Vector3.Abs(normal);
            Vector2 Project(Vector3 v)
            {
                if (absN.Z >= absN.X && absN.Z >= absN.Y) return new Vector2(v.X, v.Y);
                if (absN.Y >= absN.X) return new Vector2(v.X, v.Z);
                return new Vector2(v.Y, v.Z);
            }

            Vector2 p0 = Project(c[0]);
            Vector2 p1 = Project(c[1]);
            Vector2 p2 = Project(c[2]);
            Vector2 p3 = Project(c[3]);
            Vector2 pt = Project(p);

            Vector2 a = p0;
            Vector2 b = p1 - p0;
            Vector2 d = p3 - p0;
            Vector2 e = p0 - p1 + p2 - p3;
            Vector2 q = pt - a;

            float Cross(Vector2 v1, Vector2 v2) => v1.X * v2.Y - v1.Y * v2.X;

            float A = Cross(b, e);
            float B = Cross(b, d) - Cross(q, e);
            float C = Cross(d, q);

            float u;
            if (Math.Abs(A) < 1e-6f)
            {
                u = Math.Abs(B) > 1e-6f ? -C / B : 0f;
            }
            else
            {
                float disc = B * B - 4 * A * C;
                if (disc < 0) disc = 0;
                float sqrtDisc = MathF.Sqrt(disc);
                float u1 = (-B + sqrtDisc) / (2 * A);
                float u2 = (-B - sqrtDisc) / (2 * A);
                u = (u1 >= -0.05f && u1 <= 1.05f) ? u1 : u2;
            }

            float denomX = d.X + e.X * u;
            float denomY = d.Y + e.Y * u;
            float v;
            if (Math.Abs(denomX) > Math.Abs(denomY) && Math.Abs(denomX) > 1e-6f)
            {
                v = (q.X - b.X * u) / denomX;
            }
            else if (Math.Abs(denomY) > 1e-6f)
            {
                v = (q.Y - b.Y * u) / denomY;
            }
            else
            {
                v = 0f;
            }

            return new Vector2(Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f));
        }

        private static Vector3 GetDispOffset(Displacement disp, int side, int x, int y)
        {
            int idx = y * side + x;
            if (disp.Vectors == null || disp.Distances == null || idx >= disp.Vectors.Length || idx >= disp.Distances.Length)
                return Vector3.Zero;
            return disp.Vectors[idx] * disp.Distances[idx];
        }

        private static float GetDispAlpha(Displacement disp, int side, int x, int y)
        {
            int idx = y * side + x;
            if (disp.Alphas == null || idx >= disp.Alphas.Length)
                return 0f;
            return disp.Alphas[idx];
        }
    }
}