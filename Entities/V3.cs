
namespace MG.Server.Entities
{

    public class V3 : V2
    {
        public double Z { get; set; }

        public V3() : this(0, 0, 0) { }

        public V3(double a) : this(a, a, a) { }

        public V3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }


    }
}