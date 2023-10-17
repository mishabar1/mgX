using MG.Server.Controllers;
using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class ItemData : BaseData<GameData>
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


        public ItemData(GameData game, AssetData asset, ItemData? parentItem = null) : base()
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

            Visible = new Dictionary<string, bool>();
            ClickActions = new Dictionary<string, string>();
            HoverActions = new Dictionary<string, string>();

        }

        public ItemData? FindItem(string itemId)
        {
            ItemData found = null;

            Items.ForEach(item => {
                if (item.Id == itemId)
                {
                    found = item;
                    return;
                }
                var f = item.FindItem(itemId);
                if(f != null) {
                    found = item;
                    return;
                }
                
            });

            return found;
        }

        internal void AddAction(Func<ExecuteActionData, Task> actionFunc)
        {
            ClickActions.Add("", actionFunc.Method.Name);
        }
        internal void SetPosition(double x, double y, double z)
        {
            Position.X = x; 
            Position.Y = y;  
            Position.Z = z;
        }
        
    }

}