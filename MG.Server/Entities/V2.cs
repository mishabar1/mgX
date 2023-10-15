namespace MG.Server.Entities
{
    public class V2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public V2() : this(0, 0) { }

        public V2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}