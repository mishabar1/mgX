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
    [JsonDerivedType(typeof(CylinderAssetData), typeDiscriminator: AssetTypeEnum.CYLINDER)]
    [JsonDerivedType(typeof(ArrowAssetData), typeDiscriminator: AssetTypeEnum.ARROW)]
    [JsonDerivedType(typeof(DieAssetData), typeDiscriminator: AssetTypeEnum.DIE)]
    [JsonDerivedType(typeof(ButtonAssetData), typeDiscriminator: AssetTypeEnum.BUTTON)]
    [JsonDerivedType(typeof(PanelAssetData), typeDiscriminator: AssetTypeEnum.PANEL)]
    public class AssetData : BaseData<AssetData>
    {
        public string? FrontURL { get; set; }
        public string? BackURL { get; set; }
        public string? Text { get; set; }
        public string Type { get; set; }
        public V3 Scale { get; set; }

        public AssetData()
        {
        }
        public AssetData(string assetType) : base()
        {
            Type = assetType;
            Scale = new V3(1);
        }
    }

    // NOTE: asset keys (Name) are DETERMINISTIC (derived from the asset's content),
    // not the random BaseData name. Assets are static/process-global, so a random
    // name is regenerated on every restart — which left persisted games referencing
    // asset keys that no longer existed (blank scene / "undefined frontURL" crash).
    public class TokenAssetData : AssetData
    {
        public TokenAssetData() { }
        public TokenAssetData( string frontURL, string backUrl = "") : base( AssetTypeEnum.TOKEN)
        {
            FrontURL = frontURL;
            BackURL = backUrl;
            Name = "token:" + frontURL + "|" + backUrl;
        }
    }
    public class ObjectAssetData : AssetData
    {
        public ObjectAssetData() { }
        public ObjectAssetData( string url) : base(  AssetTypeEnum.OBJECT)
        {
            FrontURL = url;
            Name = "object:" + url;
        }
    }
    public class SoundAssetData : AssetData
    {
        public SoundAssetData() { }
        public SoundAssetData( string url) : base( AssetTypeEnum.SOUND)
        {
            FrontURL = url;
            Name = "sound:" + url;
        }
    }
    public class Text3dAssetData : AssetData
    {
        public Text3dAssetData() { }
        public Text3dAssetData( string text) : base( AssetTypeEnum.TEXT3D)
        {
            this.Text = text;
            Name = "text3d:" + text;
        }
    }
    public class TextBlockAssetData : AssetData
    {
        public TextBlockAssetData() { }
        public TextBlockAssetData( string text) : base(  AssetTypeEnum.TEXTBLOCK)
        {
            this.Text = text;
            Name = "textblock:" + text;
        }
    }

    // A procedural round disc (Three.js CylinderGeometry) — no model file needed.
    // Used for Reversi/Othello discs; the per-item "tint" attribute colours it (black/white).
    public class CylinderAssetData : AssetData
    {
        public CylinderAssetData() { }
        public CylinderAssetData(string key = "disc") : base(AssetTypeEnum.CYLINDER)
        {
            Name = "cylinder:" + key;
        }
    }

    // A flat "last move" arrow (shaft + cone head) built procedurally in the client.
    // The item's "len" attribute is the length; rotation.y aims it; "tint" colours it.
    public class ArrowAssetData : AssetData
    {
        public ArrowAssetData() { }
        public ArrowAssetData(string key = "arrow") : base(AssetTypeEnum.ARROW)
        {
            Name = "arrow:" + key;
        }
    }

    // A procedural die (Three.js cube) showing the rolled number, built in the client. Its
    // "result"/"sides" attributes drive the face; "result"=0 shows "?" (awaiting the roll).
    public class DieAssetData : AssetData
    {
        public DieAssetData() { }
        public DieAssetData(string key = "die") : base(AssetTypeEnum.DIE)
        {
            Name = "die:" + key;
        }
    }

    /// <summary>
    /// A real 3D BUTTON: a solid plate with its label printed on the front face. Built procedurally
    /// in the client, so no art file is needed.
    ///
    /// Unlike the uikit panel — which is also 3D geometry, but arranges and sizes ITSELF — a button
    /// is an ordinary item: the SERVER gives it a position, a rotation and a scale, and it goes
    /// exactly there. That is what lets a holder full of buttons hang off the camera, a player's
    /// figure, the world or a VR hand without anything reflowing.
    ///
    /// Per item: Text = the label, "bg" / "fg" attributes = plate and ink colours (CSS colours,
    /// e.g. "#22C55E"). One asset serves every button, because everything that differs lives on
    /// the item.
    /// </summary>
    public class ButtonAssetData : AssetData
    {
        public ButtonAssetData() { }
        public ButtonAssetData(string key = "button") : base(AssetTypeEnum.BUTTON)
        {
            Name = "button:" + key;
        }
    }

    /// <summary>
    /// A uikit UI panel rendered as a scene object, so it can live inside a holder. The item's
    /// <see cref="ItemData.Ui"/> is its content and <see cref="ItemData.UiWidth"/> its physical
    /// width; one asset serves every panel.
    /// </summary>
    public class PanelAssetData : AssetData
    {
        public PanelAssetData() { }
        public PanelAssetData(string key = "panel") : base(AssetTypeEnum.PANEL)
        {
            Name = "panel:" + key;
        }
    }

    public class AssetTypeEnum
    {
        public const string TOKEN = "TOKEN"; // some "box" with very small height and 2 sides - front and back
        public const string OBJECT = "OBJECT"; // stl, gbl or obj file to load a 3d model
        public const string SOUND = "SOUND"; // mp3 sound - can be played on demand
        public const string TEXT3D = "TEXT3D"; // 3d text
        public const string TEXTBLOCK = "TEXTBLOCK"; // 3d text
        public const string CYLINDER = "CYLINDER"; // procedural round disc (radius/height), tinted per-item
        public const string ARROW = "ARROW";       // procedural flat "last move" arrow (shaft + head)
        public const string DIE = "DIE";           // procedural 3D die cube showing the rolled number
        public const string BUTTON = "BUTTON";     // procedural 3D button plate; item.Text = the label
        public const string PANEL  = "PANEL";      // a uikit UI panel as a scene object; item.Ui = contents

    }


}
