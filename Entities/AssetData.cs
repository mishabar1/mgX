namespace MG.Server.Entities
{
    public class AssetData : BaseEntity<AssetData>
    {

        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }

        public AssetData():base()
        {            
        }
    }
}
