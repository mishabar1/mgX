using MG.Server.Controllers;
using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class ItemData : BaseData<GameData>
    {
        public string Asset { get; set; }

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


        public ItemData(GameData game, string asset, ItemData? parentItem = null) : base()
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

            Items.ForEach(item =>
            {
                if (item.Id == itemId)
                {
                    found = item;
                    return;
                }
                var f = item.FindItem(itemId);
                if (f != null)
                {
                    found = item;
                    return;
                }

            });

            return found;
        }

        internal ItemData AddAction(Func<ExecuteActionData, Task> actionFunc)
        {
            ClickActions.Add("", actionFunc.Method.Name);
            return this;
        }
        internal ItemData SetPosition(double x, double y, double z)
        {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            return this;
        }
        internal ItemData AddAttribute(string key)
        {
            return AddAttribute(key, "TRUE");
        }
        internal ItemData AddAttribute(string key, string val)
        {
            Attributes.Add(key, val);
            return this;
        }

        internal bool HaveAttribute(string key)
        {
            return Attributes.ContainsKey(key);
        }

        public static List<ItemData> GetItemsByAttribute(ItemData item, string key)
        {
            var ret = new List<ItemData>();

            if (item.HaveAttribute(key))
            {
                ret.Add(item);
            }
            ret.AddRange(GetItemsByAttribute(item.Items, key));
            

            return ret;
        }
        public static List<ItemData> GetItemsByAttribute(List<ItemData> items, string key)
        {
            var ret = new List<ItemData>();
            foreach (var item in items)
            {
                ret.AddRange(GetItemsByAttribute(item, key));
            }
            return ret;
        }
    }

}