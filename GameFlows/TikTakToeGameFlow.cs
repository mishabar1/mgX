using MG.Server.Controllers;
using MG.Server.Entities;
using System.Reflection;
using System.Security.AccessControl;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class TikTakToeGameFlow : BaseGameFlow
    {
        public TikTakToeGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.TIK_TAK_TOE;
        }

        
        public override async Task Setup()
        {
            Console.WriteLine("TikTakToeGameFlow Setup " + this.GameData);
            // reset all
            this.GameData.Assets = new Dictionary<string, AssetData>();
            this.GameData.Items = new List<ItemData>();
            this.GameData.Players = new List<PlayerData>();
            this.GameData.Winners = null;
            this.GameData.CurrentTurnId = null;
            this.GameData.GameStatus = GameStatusEnum.SETUP;

            // create assets
            addAsset(Assets.BOARD, new AssetData("ticktacktoe/board.glb"));
            addAsset(Assets.HOVER, new AssetData("ticktacktoe/hover.gltf"));
            addAsset(Assets.X, new AssetData("ticktacktoe/x.glb"));
            addAsset(Assets.O, new AssetData("ticktacktoe/o.glb"));
            
            // set players
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.HUMAN };
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.AI };

            // set "setup" items
            new ItemData(this.GameData, Assets.BOARD).SetPosition(0, 0, 0);

            new ItemData(this.GameData, Assets.HOVER).SetPosition(-1, 0, 1).AddAttribute("hover").AddAttribute("idx","1");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(0, 0, 1).AddAttribute("hover").AddAttribute("idx","2");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(1, 0, 1).AddAttribute("hover").AddAttribute("idx","3");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(-1, 0, 0).AddAttribute("hover").AddAttribute("idx","4");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(0, 0, 0).AddAttribute("hover").AddAttribute("idx","5");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(1, 0, 0).AddAttribute("hover").AddAttribute("idx","6");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(-1, 0, -1).AddAttribute("hover").AddAttribute("idx","7");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(0, 0, -1).AddAttribute("hover").AddAttribute("idx","8");
            new ItemData(this.GameData, Assets.HOVER).SetPosition(1, 0, -1).AddAttribute("hover").AddAttribute("idx", "9");

            setActions();

        }
        private void addAsset(string assetKey, AssetData asset)
        {
            this.GameData.Assets.Add(assetKey, asset);
        }

        private void setActions()
        {
            List<ItemData> items = ItemData.GetItemsByAttribute(GameData.Items, "hover");

            items.ForEach(x => x.AddAction(MyItemClick111));
        }

       

        public async Task MyItemClick111(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick111 " + data);

            data.Item.SetPosition(0, 1, 0);
        }

        public async Task MyItemClick444(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick444 " + data);
            data.Item.SetPosition(0, 2, 0);
        }

        public async override Task StartGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow StartGame " + this.GameData);
            
        }

        public async override Task EndGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow EndGame " + this.GameData);
            
        }

        class Assets
        {
            public const string BOARD = "BOARD";
            public const string HOVER = "HOVER";
            public const string X = "X";
            public const string O = "O";
        }
    }


}
