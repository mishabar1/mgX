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
        public int AnimationIdx { get; set; } = -1; // -1 = no animation (default). >=0 plays that clip.
        public string PlayType { get; set; }
        

        public List<ItemData> Items { get; set; }


        public string? ParentItemId { get; set; }

        // ================================ THE HOLDER =====================================
        // An item with no Asset is already a bare group that carries children (see the else
        // branch of MgGame.createItem), and children are already positioned by the SERVER
        // relative to their parent. So a "holder" needs exactly one thing that did not exist:
        // WHERE it attaches.
        //
        // That is what Anchor is. It replaces the two hard-coded holders the client ships today —
        // PLAYER HAND and PLAYER TABLE, each a Group bolted to the avatar and placed from
        // handAnchor/tableAnchor attributes — with one generic mechanism, any number of them,
        // nested however a game likes.
        //
        // The client only PARENTS and TRANSFORMS. It never measures, arranges, wraps or scales
        // anything to fit: every child's Position/Rotation/Scale is the server's word, exactly as
        // it already is for board items. That is the whole point — nothing can reflow, so nothing
        // can flicker or resize when the camera moves or a card is clicked.
        // =================================================================================

        /// <summary>
        /// Where this item's group hangs in the scene:
        ///   "world" (or null) - under its parent item / the table, as every board item does today.
        ///   "avatar"          - on <see cref="Owner"/>'s seated figure. EVERYONE sees it, which is
        ///                       how a hand of cards has presence at the table.
        ///   "camera"          - on the viewer's own camera: a HUD that rides the view. Rendered
        ///                       ONLY for Owner — nobody else has that camera.
        ///   "hand"            - on the owner's VR controller; falls back to "camera" outside VR.
        /// Position/Rotation are relative to whatever it attaches to.
        /// </summary>
        public string? Anchor { get; set; }

        /// <summary>
        /// The seat this item belongs to. Required for the per-seat anchors ("avatar", "camera",
        /// "hand") — it names whose avatar to hang on, and whose eyes may see it.
        /// </summary>
        public string? Owner { get; set; }

        public ItemData SetAnchor(string anchor, string? ownerSeatId = null)
        {
            Anchor = anchor;
            if (ownerSeatId != null) Owner = ownerSeatId;
            return this;
        }

        public ItemData SetOwner(string ownerSeatId) { Owner = ownerSeatId; return this; }

        // ---- a UI PANEL as an item -------------------------------------------------------
        // The mirror image of UiNode.Item3d (a panel holding an item): an ITEM that IS a uikit
        // panel. A uikit panel is ordinary three.js geometry, so it can hang in a holder like
        // anything else — which means dense UI (labels, buttons, a log) and free-form 3D items can
        // live side by side in the same tray.
        //
        // The holder decides WHERE it sits and UiWidth decides HOW BIG it is; uikit only arranges
        // the contents inside that fixed plate. That split is the point: the flicker and the
        // resizing came from the client choosing a panel's place and size from the camera every
        // frame, never from the arranging.

        /// <summary>For Type == PANEL: the panel's contents, exactly as PlayerData.Screen uses.</summary>
        public List<UiNode>? Ui { get; set; }

        /// <summary>Physical width of the panel in WORLD units. Its height follows its content.</summary>
        public double? UiWidth { get; set; }

        public ItemData SetUi(double worldWidth, params UiNode[] nodes)
        {
            UiWidth = worldWidth;
            Ui = new List<UiNode>(nodes);
            return this;
        }


        public static ItemData Table()
        {
            return new ItemData("", null) { Name = "GAME TABLE" };
        }

        public ItemData()
        {

        }

        public ItemData(string asset) : this(asset, null) { }
        public ItemData(string asset, ItemData parentItem) : base()
        {
            Asset = asset;
            
            if (parentItem != null)            
            {
                ParentItemId = parentItem.Id;
                parentItem.Items.Add(this);
            }

            Items = new List<ItemData>();

            Position = new V3();
            Rotation = new V3();
            Scale = new V3(1);

            Visible = new Dictionary<string, bool>();
            ClickActions = new Dictionary<string, string>();
            HoverActions = new Dictionary<string, string>();
        }

        // Depth-first search for a descendant by id.
        //
        // FIXED (two bugs, both latent until a game nests items — D&D already parents label
        // ItemData to its tokens, DnDGameFlow.cs:349/370/385):
        //   1) a nested hit returned the PARENT of the match instead of the match itself, so
        //      ExecuteAction handed the game flow the wrong ItemData;
        //   2) `return` inside List.ForEach only ends THAT iteration, so the search never
        //      short-circuited and a later sibling could overwrite an already-found match.
        // A plain foreach with real returns fixes both.
        public ItemData? FindItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            foreach (var item in Items)
            {
                if (item.Id == itemId) return item;

                var found = item.FindItem(itemId);
                if (found != null) return found;
            }

            return null;
        }

        public void RemoveItem(string itemId)
        {
            var i = Items.Find(x => x.Id == itemId);
            if (i != null)
            {
                Items.Remove(i);
            }
            foreach (var item in Items)
            //Items.ForEach(item =>
            {
                item.RemoveItem(itemId);
            }
            //);
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
        internal ItemData SetAnimation(int idx)
        {
            AnimationIdx=idx;
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