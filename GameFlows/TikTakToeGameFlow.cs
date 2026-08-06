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
            internal static AssetData BOARD = new ObjectAssetData("ticktacktoe/board.glb") { Scale = new V3(3) };
            internal static AssetData HOVER = new ObjectAssetData("ticktacktoe/hover.gltf") { Scale = new V3(0.8) };
            internal static AssetData X = new ObjectAssetData( "ticktacktoe/x.glb");
            internal static AssetData O = new ObjectAssetData( "ticktacktoe/o.glb");

            // 3D text for the "whose turn / who wins" labels on the board edges.
            internal static AssetData TURN_TEXT = new Text3dAssetData("turn");

            internal static AssetData TEST_TEXT3D = new Text3dAssetData( "this is test text");
            internal static AssetData TEST_TEXTBLOCK = new TextBlockAssetData( "xxx");
            internal static AssetData TEST_SOUND = new SoundAssetData( "ticktacktoe/beep.mp3");
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
            addAsset(Assets.TURN_TEXT);

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

            PlayerData current = GameData.Players.First(p => p.Id == GameData.CurrentTurnId);

            // Every seat controlled by the SAME user as the current-turn seat. This lets a
            // single user holding BOTH seats (hotseat) act for whichever side is to move —
            // driven entirely by the server; the client stays dumb.
            var controllingSeatIds = GameData.Players
                .Where(p => current.User != null && p.User?.Id == current.User.Id)
                .Select(p => p.Id)
                .ToList();
            if (controllingSeatIds.Count == 0) controllingSeatIds.Add(current.Id); // AI / empty seat (no user)

            foreach (var x in hovers)
            {
                x.ClickActions = new Dictionary<string, string>();
                x.Visible = new Dictionary<string, bool>();
                foreach (var sid in controllingSeatIds)
                {
                    x.AddAction(sid, HoverClick);
                    x.Visible[sid] = true;
                }
            }

            string type = current.GetStringAttribute("type"); // "x" or "o"
            SetBoardText(type.ToUpper() + " TO MOVE", type == "x" ? "0x22C55E" : "0x2563EB"); // X green, O blue
        }

        // Place the same label flat on all 4 board edges (readable from any side).
        // Laid flat with a -90° X tilt; in-plane facing is a ROLL about Z.
        private void SetBoardText(string label, string tint)
        {
            foreach (var t in getItemsByAttribute("turnText")) removeItem(t.Id);

            (double x, double z, double roll)[] sides =
            {
                (0, -1.8, 180),  // O's side (-z)
                (0,  1.8, 0),    // X's side (+z)
                (-1.8, 0, -90),  // west
                ( 1.8, 0,  90),  // east
            };
            foreach (var s in sides)
            {
                addTextItem(Assets.TURN_TEXT)
                    .SetText(label)
                    .SetPosition(s.x, 0.1, s.z)
                    .SetScale(0.4)
                    .SetRotation(-90, 0, s.roll)
                    .AddAttribute("turnText", "1")
                    .AddAttribute("tint", tint);
            }
        }


        [GameAction]
        public async Task HoverClick(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow HoverClick ");

            var current = GameData.Players.FirstOrDefault(p => p.Id == GameData.CurrentTurnId);
            if (current == null || data.Player == null) return;

            // Server-authoritative turn check: the click must come from the user who
            // controls the current-turn seat (AI seats have no user, so allow those).
            if (current.User != null && data.Player.User?.Id != current.User.Id) return;

            string type = current.GetStringAttribute("type"); // whose turn it is: "x" or "o"

            var a = addItem(type == "x" ? Assets.X : Assets.O);
            a.AddAttribute("item");
            a.AddAttribute(type); // x or o
            a.AddAttribute("type", type);
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

        [GameAction]
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

            if (GameData.Winners?.Count > 0)
            {
                string t = GameData.Winners[0].GetStringAttribute("type"); // "x" or "o"
                string who = PlayerDisplayName(GameData.Winners[0]);
                SetBoardText(t.ToUpper() + " WINS!", t == "x" ? "0x22C55E" : "0x2563EB"); // X green, O blue
                GameData.Attributes["result"] = t.ToUpper() + " (" + who + ") wins!";
            }
            else
            {
                SetBoardText("TIE!", "0x888888");
                GameData.Attributes["result"] = "It's a tie.";
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
