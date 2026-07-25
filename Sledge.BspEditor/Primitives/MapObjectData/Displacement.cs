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

        public Displacement(int power, Vector3[] corners)
        {
            Power = power;
            Corners = new Vector3[4];
            for (int i = 0; i < 4 && i < corners.Length; i++) Corners[i] = corners[i];
            Distances = new float[((1 << power) + 1) * ((1 << power) + 1)];
        }

        public Displacement Clone()
        {
            var clone = new Displacement(Power, Corners);
            Array.Copy(Distances, clone.Distances, Distances.Length);
            return clone;
        }
    }
}