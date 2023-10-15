using Microsoft.EntityFrameworkCore;

namespace MG.Server.Entities
{
    [Keyless]
    public class Vector3 : Vector2
    {
        public float Z { get; set; }

        public Vector3() : this(0, 0, 0) { }

        public Vector3(float a) : this(a, a, a) { }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }


    }
}