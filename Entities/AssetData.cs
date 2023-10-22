namespace MG.Server.Entities
{
    public class AssetData : BaseData<AssetData>
    {
        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }

        public string Type { get; set; }

        public AssetData(string frontURL, string backUrl = "") :base()
        {
            FrontURL = frontURL;
            BackURL = backUrl;
        }
    }

    public class AssetTypeEnum
    {
        public const string TOKKEN = "TOKKEN";
        public const string SOUND = "SOUND";
        public const string OBJECT = "OBJECT";

    }
}
