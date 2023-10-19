namespace MG.Server.Entities
{
    public class AssetData : BaseData<AssetData>
    {

        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }

        public AssetData(string frontURL, string backUrl = "") :base()
        {
            FrontURL = frontURL;
            BackURL = backUrl;
        }
    }
}
