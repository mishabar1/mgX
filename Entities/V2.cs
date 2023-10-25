using MG.Server.Services;
using System.Text;

namespace MG.Server.Entities
{
    public class V2
    {
        public double X { get; set; }
        public double Y { get; set; }

        public V2() : this(0, 0) { }

        public V2(double x, double y)
        {
            X = x;
            Y = y;
        }


        public override string ToString()
        {
            return Utils.ListProperties(this);
        }
    }
}