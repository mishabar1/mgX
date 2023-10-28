namespace MG.Server.Entities
{
    public class LocationData: BaseData<LocationData>
    {
        public V3 Position { get; set; }
        public V3 Rotation { get; set; }
        public V3 Scale { get; set; }

        public LocationData()
        {
            Position = new V3();
            Rotation = new V3();
            Scale = new V3(1);
        }        

        

    }
}
