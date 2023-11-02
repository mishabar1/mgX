using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    [JsonDerivedType(typeof(TokenAssetData), typeDiscriminator: AssetTypeEnum.TOKEN)]
    [JsonDerivedType(typeof(ObjectAssetData), typeDiscriminator: AssetTypeEnum.OBJECT)]
    [JsonDerivedType(typeof(SoundAssetData), typeDiscriminator: AssetTypeEnum.SOUND)]
    [JsonDerivedType(typeof(Text3dAssetData), typeDiscriminator: AssetTypeEnum.TEXT3D)]
    [JsonDerivedType(typeof(TextBlockAssetData), typeDiscriminator: AssetTypeEnum.TEXTBLOCK)]
    public class AssetData : BaseData<AssetData>
    {
        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }
        public string? Text { get; set; }
        public string Type { get; set; }

        public AssetData()
        {
        }
        public AssetData(string name, string frontURL, string backUrl, string assetType) : base()
        {
            Name = name;
            FrontURL = frontURL;
            BackURL = backUrl;
            Type = assetType;
        }
    }

    public class TokenAssetData : AssetData
    {
        public TokenAssetData() { }
        public TokenAssetData(string name, string frontURL, string backUrl = "") : base(name, frontURL, backUrl, AssetTypeEnum.TOKEN)
        {
        }
    }
    public class ObjectAssetData : AssetData
    {
        public ObjectAssetData() { }
        public ObjectAssetData(string name, string url) : base(name, url, "", AssetTypeEnum.OBJECT)
        {
        }
    }
    public class SoundAssetData : AssetData
    {
        public SoundAssetData() { }
        public SoundAssetData(string name, string url) : base(name, url, "", AssetTypeEnum.SOUND)
        {
        }
    }
    public class Text3dAssetData : AssetData
    {
        public Text3dAssetData() { }
        public Text3dAssetData(string name, string text) : base(name, "", "", AssetTypeEnum.TEXT3D)
        {
            this.Text = text;
        }
    }
    public class TextBlockAssetData : AssetData
    {
        public TextBlockAssetData() { }
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


}
