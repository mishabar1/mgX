using MG.Server.Controllers;
using MG.Server.Entities;
using System.Reflection;
using System.Security.AccessControl;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class TikTakToeGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            //internal static AssetData BOARD = new TokenAssetData("BOARD", "ticktacktoe/board.png");
            internal static AssetData BOARD = new ObjectAssetData("BOARD", "ticktacktoe/board.glb");
            internal static AssetData HOVER = new ObjectAssetData("HOVER", "ticktacktoe/hover.gltf");
            internal static AssetData X = new ObjectAssetData("X", "ticktacktoe/x.glb");
            internal static AssetData O = new ObjectAssetData("O", "ticktacktoe/o.glb");

            internal static AssetData TEST_TEXT3D = new Text3dAssetData("TEST_TEXT3D", "this is test text");
            internal static AssetData TEST_TEXTBLOCK = new TextBlockAssetData("TEST_TEXTBLOCK", "xxx");
            internal static AssetData TEST_SOUND = new SoundAssetData("TEST_SOUND", "ticktacktoe/beep.mp3");
        }

        public TikTakToeGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.TIK_TAK_TOE;



        }
        protected override async Task Create()
        {
            Console.WriteLine("TikTakToeGameFlow Create ");

            addAsset(Assets.BOARD);
            addAsset(Assets.HOVER);
            addAsset(Assets.X);
            addAsset(Assets.O);

            //some tests
            addAsset(Assets.TEST_TEXT3D);
            addAsset(Assets.TEST_TEXTBLOCK);
            addAsset(Assets.TEST_SOUND);

            GameData.Observer.Position.Set(0, 4, 0);

            // set players
            // X
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
            .AddAttribute("type", "x")
            .SetCameraPosition(0, 2, 3)
            .SetAvatarPosition(0, 2, 3)
            ;

            //O
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
            .AddAttribute("type", "o")
            .SetCameraPosition(0, 2, -3)
            .SetAvatarPosition(0, 2, -3)
            ;
        }

        protected override async Task Setup()
        {
            Console.WriteLine("TikTakToeGameFlow Setup ");
        }

        protected async override Task StartGame()
        {
            Console.WriteLine("TikTakToeGameFlow StartGame ");

            addTextItem(Assets.TEST_TEXTBLOCK).SetPosition(0, 1, 0).AddAttribute("text1").SetText("START");

            //demo
            //addItem(Assets.BOARD).SetPosition(0, 0, 0).SetScale(3, 1, 3);
            
            addItem(Assets.BOARD).SetPosition(0, 0, 0);

            // start sound
            //playSound(Assets.SOUND1, "LOOP"); // or "LOOP" // 

            addItem(Assets.HOVER).SetPosition(-1, 0, 1).AddAttribute("hover").AddAttribute("idx", "0").AddAttribute("x", -1).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(0, 0, 1).AddAttribute("hover").AddAttribute("idx", "1").AddAttribute("x", 0).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(1, 0, 1).AddAttribute("hover").AddAttribute("idx", "2").AddAttribute("x", 1).AddAttribute("z", 1);
            addItem(Assets.HOVER).SetPosition(-1, 0, 0).AddAttribute("hover").AddAttribute("idx", "3").AddAttribute("x", -1).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(0, 0, 0).AddAttribute("hover").AddAttribute("idx", "4").AddAttribute("x", 0).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(1, 0, 0).AddAttribute("hover").AddAttribute("idx", "5").AddAttribute("x", 1).AddAttribute("z", 0);
            addItem(Assets.HOVER).SetPosition(-1, 0, -1).AddAttribute("hover").AddAttribute("idx", "6").AddAttribute("x", -1).AddAttribute("z", -1);
            addItem(Assets.HOVER).SetPosition(0, 0, -1).AddAttribute("hover").AddAttribute("idx", "7").AddAttribute("x", 0).AddAttribute("z", -1);
            addItem(Assets.HOVER).SetPosition(1, 0, -1).AddAttribute("hover").AddAttribute("idx", "8").AddAttribute("x", 1).AddAttribute("z", -1);


            // demo
            //addItem(Assets.TEST_TEXT3D).SetPosition(0, 1, 0).SetText("aaaa").AddAction(RotateMe);

            advanceNextTurn();
            setActionsByCurrentTurn();
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

            ItemData text1 = ItemData.GetItemsByAttribute(this.GameData.Table, "text1").First();
            PlayerData player = GameData.Players.Where(x => x.Id == GameData.CurrentTurnId).First();
            text1.Text = "Turn " + player.Name + " " + player.GetStringAttribute("type").ToUpper();
        }


        public async Task HoverClick(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow HoverClick ");

            ItemData a;
            if (data.Player.GetStringAttribute("type") == "x")
            {
                a = addItem(Assets.X);                
            }
            else
            {
                a = addItem(Assets.O);               
            }
            a.AddAttribute("item"); // x or o
            a.AddAttribute(data.Player.GetStringAttribute("type")); // x or o
            a.AddAttribute("type", data.Player.GetStringAttribute("type"));
            a.AddAttribute("idx", data.Item.GetStringAttribute("idx"));
            a.SetPosition(data.Item.GetNumberAttribute("x"), 0, data.Item.GetNumberAttribute("z"));

            // delete hover item
            removeItem(data.itemId);


            //remove the sound
            ItemData.GetItemsByAsset(GameData.Table, Assets.TEST_SOUND).ForEach(x => { removeItem(x.Id); });

            // start sound
            playSound(Assets.TEST_SOUND, "ONCE"); // or "LOOP" // 


            //advance turn

            // set
            advanceNextTurn();
            setActionsByCurrentTurn();
        }

        public async Task RotateMe(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow RotateMe ");

            data.Item.Rotation.Y += 10;
            data.Item.Scale.X += 0.5;
            if (data.Item.Scale.X > 3)
            {
                data.Item.Scale.X = 0.5;
            }
        }



        protected async override Task EndGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow EndGame ");

            //remove the sounds
            getItemsByAsset(Assets.TEST_SOUND).ForEach(x => { removeItem(x.Id); });
            //remove hovers
            removeItemsByAsset(Assets.HOVER);

            ItemData text1 = getItemsByAttribute("text1").First();            
            text1.Text = "Game ended: ";

            if (GameData.Winners?.Count > 0)
            {
                PlayerData player = GameData.Winners[0];
                text1.Text += player.GetStringAttribute("type").ToUpper() + " WIN !"; 

            }
            else
            {
                text1.Text += "TIE !";
            }


        }

        protected override async Task<bool> IsEndGame()
        {

            var board = getGameAsBoard();

            //Console.WriteLine(board);
            

            if (isAWon(board,"x") || isAWon(board,"o") || (board.Where(x => x != "").Count() == 9))
            {
                return true;
            }                      
;

            return false;
        }
        protected override List<PlayerData> GetGameWinners()
        {
            var board = getGameAsBoard();
            if (isAWon(board, "x"))
            {
                return GameData.Players.Where(x => x.HaveAttribute("type", "x")).ToList();
            }
            if (isAWon(board, "o"))
            {
                return GameData.Players.Where(x => x.HaveAttribute("type", "o")).ToList();
            }

            return new List<PlayerData>();
        }

        private List<string> getGameAsBoard()
        {
            // get board as list
            var board = new List<string>() { "", "", "", "", "", "", "", "", "" };
            //board[0] = "x";
            var allItems = GameData.GetAllGameItems();
            var x_items = allItems.Where(x => x.HaveAttribute("x") && x.HaveAttribute("item"));
            foreach (var item in x_items)
            {
                try
                {
                    board[item.GetIntAttribute("idx")] = "x";
                }
                catch (Exception)
                {
                    Console.WriteLine("ERRRRROOOROROROROORRORO  !!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                    Console.WriteLine(board.ToString() + item);
                    throw;
                }
                
            }
            var o_items = allItems.Where(x => x.HaveAttribute("o") && x.HaveAttribute("item"));
            foreach (var item in o_items)
            {
                board[item.GetIntAttribute("idx")] = "o";
            }
            return board;
        }
        private bool isAWon(List<string> board, string a)
        {
            if ((board[0] == board[1] && board[1] == board[2] && board[2] == a) ||
                (board[3] == board[4] && board[4] == board[5] && board[5] == a) ||
                (board[6] == board[7] && board[7] == board[8] && board[8] == a) ||
                (board[0] == board[3] && board[3] == board[6] && board[6] == a) ||
                (board[1] == board[4] && board[4] == board[7] && board[7] == a) ||
                (board[2] == board[5] && board[5] == board[8] && board[8] == a) ||
                (board[0] == board[4] && board[4] == board[8] && board[8] == a) ||
                (board[2] == board[4] && board[4] == board[6] && board[6] == a))
            {
                return true;
            }
            return false;
        }


    }


}
