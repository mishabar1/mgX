using System.ComponentModel;
using System.Reflection;

namespace MG.Server.Entities
{
    public class AssetData : BaseData<AssetData>
    {
        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }
        public string? Text { get; set; }
        public string Type { get; set; }

        public AssetData(string name, string frontURL, string backUrl, string assetType) :base()
        {
            this.Name = name;
            FrontURL = frontURL;    
            BackURL = backUrl;
            Type = assetType;
        }
    }

    public class TokenAssetData : AssetData
    {
        public TokenAssetData(string name, string frontURL, string backUrl = "") : base(name, frontURL, backUrl, AssetTypeEnum.TOKEN)
        {
        }
    }
    public class ObjectAssetData : AssetData
    {
        public ObjectAssetData(string name, string url) : base(name, url, "", AssetTypeEnum.OBJECT)
        {
        }
    }
    public class SoundAssetData : AssetData
    {
        public SoundAssetData(string name, string url) : base(name, url, "", AssetTypeEnum.SOUND)
        {
        }
    }
    public class Text3dAssetData : AssetData
    {
        public Text3dAssetData(string name, string text) : base(name, "", "", AssetTypeEnum.TEXT3D)
        {
            this.Text = text;
        }
    }
    public class TextBlockAssetData : AssetData
    {
        public TextBlockAssetData(string name, string text) : base(name, "", "", AssetTypeEnum.TEXTBLOCK)
        {
            this.Text = text;
        }
    }

    public class AssetTypeEnum
    {
        public const string TOKEN = "TOKEN"; // some "box" with very small height and 2 sides - front and back
        public const string OBJECT = "OBJECT"; // stl, gbl or obj file to load a 3d model
        public const string SOUND = "SOUND"; // mp3 sound - can be played on demand        
        public const string TEXT3D = "TEXT3D"; // 3d text
        public const string TEXTBLOCK = "TEXTBLOCK"; // 3d text      

    }

    //[DefaultProperty("Name")]
    //public class GameTypeEnum 
    //{
    //    public static GameTypeEnum TIK_TAK_TOE => new GameTypeEnum( "TIK_TAK_TOE");
    //    public static GameTypeEnum CHESS => new GameTypeEnum( "CHESS");
    //    public static GameTypeEnum DND => new GameTypeEnum( "DND");


    //    public GameTypeEnum( string name)
    //    {
    //    }
    //}

    
  
}
