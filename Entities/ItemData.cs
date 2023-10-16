using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class ItemData : BaseEntity<GameData>
    {
        public AssetData Asset { get; set; }

        public string GameId { get { return Game.Id; } }
        [JsonIgnore] public GameData Game { get; set; }



        public V3 Position { get; set; }
        public V3 Rotation { get; set; }
        public V3 Scale { get; set; }

        public Dictionary<string, bool> Visible { get; set; }
        public Dictionary<string, string> ClickActions { get; set; } // player id - action name
        public Dictionary<string, string> HoverActions { get; set; } // player id - action name


        public List<ItemData> Items { get; set; }



        public string? ParentItemId { get { return ParentItem?.Id; } }
        [JsonIgnore] public ItemData? ParentItem { get; set; }


        public ItemData(GameData game, AssetData asset, ItemData? parentItem = null):base()
        {            
            Name = Utils.RandomName();

            Asset = asset;

            Game = game;
            
            ParentItem = parentItem;
            if (ParentItem != null)
            {
                ParentItem.Items.Add(this);
            }
            else
            {
                Game.Items.Add(this);
            }

            Items = new List<ItemData>();

            Position = new V3();
            Rotation = new V3();
            Scale = new V3(1);

        }
    }

}