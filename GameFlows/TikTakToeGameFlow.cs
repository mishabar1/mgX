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
            List<ItemData> hovers = ItemData.GetItemsByAttribute(this.GameData.Table, "hover");

            hovers.ForEach(x =>
            {
                x.ClickActions = new Dictionary<string, string>();
                x.Visible = new Dictionary<string, bool>();

                x.AddAction(this.GameData.CurrentTurnId, HoverClick);
                x.Visible.Add(this.GameData.CurrentTurnId, true);
            });
        }



        public async Task HoverClick(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow HoverClick " + data);

            ItemData a;
            if (data.Player.GetStringAttribute("type") == "x")
            {
                a = addItem(Assets.X);                
            }
            else
            {
                a = addItem(Assets.O);               
            }
            a.AddAttribute("type", data.Player.GetStringAttribute("type"));
            a.SetPosition(data.Item.GetNumberAttribute("x"), 0, data.Item.GetNumberAttribute("z"));

            // delete hover item
            removeItem(data.itemId);

            //advance turn

            // set
            advanceNextTurn();
            setActionsByCurrentTurn();
        }


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

        public override async Task<bool> IsEndGame()
        {

            return false;
        }

        public override List<PlayerData> GetGameWinners()
        {
            return GameData.Players;
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
