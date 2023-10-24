namespace MG.Server.Entities
{
    public class AssetData : BaseData<AssetData>
    {
        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }
        public string? Text { get; set; }
        public string Type { get; set; }

        public AssetData(string frontURL, string backUrl = "", string assetType = AssetTypeEnum.OBJECT) :base()
        {
            FrontURL = frontURL;    
            BackURL = backUrl;
            Type = assetType;
        }
    }

    public class AssetTypeEnum
    {
        public const string TOKEN = "TOKEN"; // some "box" with very small height and 2 sides - front and back
        public const string SOUND = "SOUND"; // mp3 sound - can be played on demand
        public const string OBJECT = "OBJECT"; // stl, gbl or obj file to load a 3d model
        public const string TEXT3D = "TEXT3D"; // 3d text
        public const string TEXTCSS = "TEXTCSS"; // 3d text
        

    }
}
