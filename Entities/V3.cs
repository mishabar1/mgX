
namespace MG.Server.Entities
{

    public class V3 : V2
    {
        public float Z { get; set; }

        public V3() : this(0, 0, 0) { }

        public V3(float a) : this(a, a, a) { }

        public V3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }


    }
}