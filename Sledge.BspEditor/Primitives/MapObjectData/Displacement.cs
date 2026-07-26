using System;
using System.Linq;
using System.Numerics;

namespace Sledge.BspEditor.Primitives.MapObjectData
{
    public class Displacement
    {
        public int Power { get; set; }
        public Vector3[] Corners { get; set; }
        public float[] Distances { get; set; }
        public Vector3[] Vectors { get; set; }
        public float[] Alphas { get; set; }

        public Displacement(int power, Vector3[] corners)
        {
            Power = power;
            Corners = new Vector3[4];
            for (int i = 0; i < 4 && i < corners.Length; i++) Corners[i] = corners[i];
            var count = ((1 << power) + 1) * ((1 << power) + 1);
            Distances = new float[count];
            Vectors = new Vector3[count];
            Alphas = new float[count];
            for (int i = 0; i < count; i++) Vectors[i] = Vector3.UnitZ;
        }

        public Displacement Clone()
        {
            var clone = new Displacement(Power, Corners);
            Array.Copy(Distances, clone.Distances, Distances.Length);
            Array.Copy(Vectors, clone.Vectors, Vectors.Length);
            Array.Copy(Alphas, clone.Alphas, Alphas.Length);
            return clone;
        }
    }
}