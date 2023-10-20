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
            this.GameData.Table = ItemData.Table();
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
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }.AddAttribute("type", "x");
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }.AddAttribute("type", "o");

        }


        private void setActionsByCurrentTurn()
        {
            List<ItemData> items = ItemData.GetItemsByAttribute(this.GameData.Table, "hover");

            items.ForEach(x =>
            {
                x.ClickActions = new Dictionary<string, string>();
                x.Visible = new Dictionary<string, bool>();

                x.AddAction(this.GameData.CurrentTurnId, HoverClick);
                x.Visible!.Add(this.GameData.CurrentTurnId, true);
            });
        }



        public async Task HoverClick(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow HoverClick " + data);

            //data.Item.SetPosition(0, 1, 0);

            // create x or o item in the hover place
            var a = addItem(Assets.X);
            a.AddAttribute("type", "x");
            a.SetPosition(data.Item.GetNumberAddAttribute("x"), 0, data.Item.GetNumberAddAttribute("z"));

            // delete hover item
            removeItem(data.itemId);

            //advance turn

            // set
            advanceNextTurn();
            setActionsByCurrentTurn();
        }

        //private (double x, double y, double z) getPosByIndex(ItemData item)
        //{
        //    double x = 0;
        //    double y = 0;
        //    double z = 0;

        //    return (0, 0, z);
        //}


        public async override Task StartGame()
        {
            Console.WriteLine("TikTakToeGameFlow StartGame " + this.GameData);


            addItem(Assets.BOARD).SetPosition(0, 0, 0);

            addItem(Assets.HOVER).SetPosition(-1, 0, 1).AddAttribute("hover").AddAttribute("idx", "1").AddAttribute("x", -1).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(0, 0, 1).AddAttribute("hover").AddAttribute("idx", "2").AddAttribute("x", 0).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(1, 0, 1).AddAttribute("hover").AddAttribute("idx", "3").AddAttribute("x", 1).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(-1, 0, 0).AddAttribute("hover").AddAttribute("idx", "4").AddAttribute("x", -1).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(0, 0, 0).AddAttribute("hover").AddAttribute("idx", "5").AddAttribute("x", 0).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(1, 0, 0).AddAttribute("hover").AddAttribute("idx", "6").AddAttribute("x", 1).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(-1, 0, -1).AddAttribute("hover").AddAttribute("idx", "7").AddAttribute("x", -1).AddAttribute("z", -1);
            addItem(Assets.HOVER).SetPosition(0, 0, -1).AddAttribute("hover").AddAttribute("idx", "8").AddAttribute("x", 0).AddAttribute("z", -1);
            addItem(Assets.HOVER).SetPosition(1, 0, -1).AddAttribute("hover").AddAttribute("idx", "9").AddAttribute("x", 1).AddAttribute("z", -1);

            advanceNextTurn();
            setActionsByCurrentTurn();
        }


        public async override Task EndGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow EndGame " + this.GameData);

        }

        public override bool IsEndGame()
        {
            throw new NotImplementedException();
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
