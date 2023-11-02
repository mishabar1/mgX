using MG.Server.Controllers;
using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class ItemData : BaseData<ItemData>
    {
        public string Asset { get; set; }

        public V3 Position { get; set; }
        public V3 Rotation { get; set; }
        public V3 Scale { get; set; }

        public Dictionary<string, bool> Visible { get; set; }
        public Dictionary<string, string> ClickActions { get; set; } // player id - action name
        public Dictionary<string, string> HoverActions { get; set; } // player id - action name

        public string Text { get; set; }
        public string PlayType { get; set; }
        

        public List<ItemData> Items { get; set; }


        public string? ParentItemId { get { return ParentItem?.Id; } }
        [JsonIgnore] public ItemData? ParentItem { get; set; }


        public static ItemData Table()
        {
            return new ItemData("", null) { Name = "TABLE" };
        }

        public ItemData()
        {

        }

        public ItemData(string asset) : this(asset, null) { }
        public ItemData(string asset, ItemData parentItem) : base()
        {
            Asset = asset;

            ParentItem = parentItem;
            if (ParentItem != null)
            {
                ParentItem.Items.Add(this);
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

        public void RemoveItem(string itemId)
        {
            var i = Items.Find(x => x.Id == itemId);
            if (i != null)
            {
                Items.Remove(i);
            }
            Items.ForEach(item =>
            {
                item.RemoveItem(itemId);
            });
        }


        internal ItemData AddAction(Func<ExecuteActionData, Task> actionFunc)
        {
            ClickActions.Add("", actionFunc.Method.Name);
            return this;
        }
        internal ItemData AddAction(string playerId, Func<ExecuteActionData, Task> actionFunc)
        {
            ClickActions.Add(playerId, actionFunc.Method.Name);
            return this;
        }
        internal ItemData SetPosition(double x, double z)
        {
            return SetPosition(x, 0, z);
        }
        internal ItemData SetPosition(double x, double y, double z)
        {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            return this;
        }
        internal ItemData SetPosition(V3 pos)
        {
            Position = pos;
            return this;
        }

        internal ItemData SetScale(double a)
        {
            return SetScale(a, a, a);
        }
        internal ItemData SetScale(double x, double y, double z)
        {
            Scale.X = x;
            Scale.Y = y;
            Scale.Z = z;
            return this;
        }
        
        // rotate only on xz plane
        internal ItemData SetRotation(double deg)
        {
            return SetRotation(0, deg, 0);
        }
        internal ItemData SetRotation(double x, double y, double z)
        {
            Rotation.X = x;
            Rotation.Y = y;
            Rotation.Z = z;
            return this;
        }

        internal ItemData SetText(string text)
        {
            Text = text;
            return this;
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
        public static List<ItemData> GetItemsByAsset(ItemData item, AssetData asset)
        {
            var ret = new List<ItemData>();

            if (item.Asset== asset.Name)
            {
                ret.Add(item);
            }
            ret.AddRange(GetItemsByAsset(item.Items, asset));


            return ret;
        }
        public static List<ItemData> GetItemsByAsset(List<ItemData> items, AssetData asset)
        {
            var ret = new List<ItemData>();
            foreach (var item in items)
            {
                ret.AddRange(GetItemsByAsset(item, asset));
            }
            return ret;
        }



    }

}