using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Reflection;
using static MG.Server.GameFlows.TikTakToeGameFlow;

namespace MG.Server.GameFlows
{
    public abstract class BaseGameFlow
    {

        public GameData GameData { get; set; }
        public List<GameData> HistoryGameData { get; set; }


        public static GameData CreateGame(string gameType, string userId)
        {
            var game = new GameData();

            switch (gameType!)
            {
                case GameTypeEnum.TIK_TAK_TOE:
                    game.GameFlow = new TikTakToeGameFlow(game);
                    break;
                case GameTypeEnum.CHESS:
                    game.GameFlow = new ChessGameFlow(game);
                    break;
                case GameTypeEnum.DND:
                    game.GameFlow = new DnDGameFlow(game);
                    break;
                default:
                    break;
            }

            game.GameStatus = GameStatusEnum.CREATED;
            game.CreatorId = userId;

            _ = game.GameFlow.RunCreateFlow();

            return game;
        }
        public BaseGameFlow(GameData gameData)
        {
            GameData = gameData;
            GameData.GameFlow = this;

            HistoryGameData = new List<GameData>();
        }

        public async Task RunCreateFlow()
        {
            this.GameData.Players = new List<PlayerData>();
            await Create();

        }
        protected abstract Task Create();

        public async Task RunSetupFlow()
        {
            // reset all            
            this.GameData.Table = ItemData.Table();
            this.GameData.Winners = null;
            this.GameData.CurrentTurnId = null;
            this.GameData.GameStatus = GameStatusEnum.SETUP;

            foreach (var player in GameData.Players)
            {
                player.Hand = new ItemData("", null) { Name = "PLAYER TABLE" };
                player.Table = new ItemData("", null) { Name = "PLAYER TABLE" };
            }

            await Setup();

            // reset history
            HistoryGameData = new List<GameData>() { GameData.DeepCopy() };

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);
        }
        protected abstract Task Setup();

        public async Task RunStartFlow()
        {

            this.GameData.GameStatus = GameStatusEnum.PLAY;

            await StartGame();

            // create AI agents
            foreach (var player in this.GameData.Players)
            {
                if (player.Type == PlayerTypeEnum.AI)
                {
                    player.AIAgent = new AIAgent(this.GameData, player);
                }
            }

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);

        }
        protected abstract Task StartGame();
        protected abstract Task EndGame();


        public async Task RunEndGameFlow()
        {
            try
            {
                await EndGame();
            }
            catch (Exception)
            {
                Console.WriteLine("fail to run end game");
            }
        }

        protected abstract Task<bool> IsEndGame();
        protected abstract List<PlayerData> GetGameWinners();

        public async Task ExecuteAction(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow ExecuteAction ");

            data.Item = GameData.FindItem(data.itemId);
            data.Player = GameData.FindPlayer(data.playerId);
            if (data.Item != null && data.Player != null)
            {

                // SECURITY (C1): dispatch by client-supplied action name, but ONLY to methods
                // explicitly marked [GameAction]. Previously this invoked ANY method named by the
                // client (arbitrary-method-invocation / RCE vector).
                Type thisType = GetType();
                MethodInfo? theMethod = thisType.GetMethod(
                    data.actionId ?? string.Empty,
                    BindingFlags.Public | BindingFlags.Instance);

                if (theMethod == null || theMethod.GetCustomAttribute<GameActionAttribute>() == null)
                {
                    Console.WriteLine($"Rejected action '{data.actionId}' — not a registered [GameAction].");
                    return;
                }

                await (Task)theMethod.Invoke(this, new object[] { data })!;
            }

            // check if game ended - 
            var ended = await IsEndGame();
            if (ended)
            {
                this.GameData.GameStatus = GameStatusEnum.ENDED;

                this.GameData.Winners = GetGameWinners();
                Console.WriteLine("TikTakToeGameFlow GAME ENDED !!!!!! winners count: " + this.GameData.Winners.Count());

                await RunEndGameFlow();



            }

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);

        }

        internal AssetData addAsset(AssetData asset)
        {
            this.GameData.Assets.Add(asset.Name, asset);
            return asset;
        }

        internal ItemData addItem(AssetData asset)
        {
            var item = new ItemData(asset.Name, this.GameData.Table);
            return item;
        }
        //internal ItemData addItem(string assetKey)
        //{
        //    var item = new ItemData(assetKey, this.GameData.Table);
        //    return item;
        //}

        internal ItemData addTextItem(AssetData asset)
        {
            // TODO !!!!
            //this.GameData.Assets.TryAdd("TEXTBLOCK", new AssetData("", "", "TEXTBLOCK"));
            //var item = new ItemData("TEXTBLOCK", this.GameData.Table);
            var item = new ItemData(asset.Name, this.GameData.Table);
            //item.Text = text;
            return item;
        }

        internal ItemData playSound(AssetData asset, string playType = "ONCE") // "ONCE" OR "LOOP"
        {
            var item = new ItemData(asset.Name, this.GameData.Table);
            item.PlayType = playType;
            return item;
        }

        internal PlayerData getPlayerByAttribute(string key, string val)
        {
            foreach (var p in GameData.Players)
            {
                if (p.HaveAttribute(key, val))
                {
                    return p;
                }
            }
            return null;
        }

        internal ItemData addItemToPlayerTable(PlayerData player, AssetData asset)
        {
            var item = new ItemData(asset.Name, player.Table);
            return item;
        }
                
        internal ItemData addItemToPlayerHand(PlayerData player, AssetData asset)
        {
            var item = new ItemData(asset.Name, player.Hand);
            return item;
        }

        internal void removeItem(string itemId)
        {
            this.GameData.Table.RemoveItem(itemId);
        }

        internal void advanceNextTurn()
        {
            if (this.GameData.CurrentTurnId == null)
            {
                this.GameData.CurrentTurnId = this.GameData.Players.First().Id;
            }
            else
            {
                var idx = this.GameData.Players.FindIndex(x => x.Id == this.GameData.CurrentTurnId);
                if (idx == (this.GameData.Players.Count - 1))
                {
                    idx = 0;
                }
                else
                {
                    idx++;
                }
                this.GameData.CurrentTurnId = this.GameData.Players[idx].Id;
            }
        }

        internal List<ItemData> getItemsByAsset(AssetData asset)
        {
            return ItemData.GetItemsByAsset(GameData.Table, asset);
        }
        internal void removeItemsByAsset(AssetData asset)
        {
            ItemData.GetItemsByAsset(GameData.Table, asset).ForEach(x => { removeItem(x.Id); });
        }

        internal List<ItemData> getItemsByAttribute(string key)
        {
            return ItemData.GetItemsByAttribute(this.GameData.Table, key);
        }

        // ---------------------------------------------------------------------
        // Generic virtual-tabletop movement (used by Chess and D&D).
        // Interaction model: click a piece to select it, then click the move
        // surface (board/map) — the piece jumps to the clicked world point.
        // No rule enforcement; players self-enforce, like a tabletop simulator.
        // ---------------------------------------------------------------------

        /// <summary>Make an item selectable so it can be picked up and moved.</summary>
        protected void makeMovable(ItemData piece)
        {
            piece.AddAction(SelectPiece);
        }

        /// <summary>Make an item (board/map) a surface that moves the selected piece to the click point.</summary>
        protected void makeMoveSurface(ItemData surface)
        {
            surface.AddAction(MoveHere);
        }

        // Height a picked-up piece is raised to, as a visible "selected" cue.
        private const double LiftHeight = 1.2;

        [GameAction]
        public async Task SelectPiece(ExecuteActionData data)
        {
            // Drop any previously-selected piece back down.
            if (GameData.Attributes.TryGetValue("selectedItem", out var prevId)
                && !string.IsNullOrEmpty(prevId))
            {
                var prev = GameData.FindItem(prevId);
                if (prev != null) prev.Position.Y = 0;
            }

            // Remember + lift the newly selected piece so the user sees it's picked up.
            GameData.Attributes["selectedItem"] = data.itemId;
            if (data.Item != null) data.Item.Position.Y = LiftHeight;

            await Task.CompletedTask;
        }

        [GameAction]
        public async Task MoveHere(ExecuteActionData data)
        {
            if (GameData.Attributes.TryGetValue("selectedItem", out var selectedId)
                && !string.IsNullOrEmpty(selectedId))
            {
                var piece = GameData.FindItem(selectedId);
                if (piece != null && data.point != null)
                {
                    // Move to where the surface was clicked, and drop it back onto the board.
                    piece.Position.X = data.point.X;
                    piece.Position.Z = data.point.Z;
                    piece.Position.Y = 0;
                }
                GameData.Attributes.Remove("selectedItem");
            }
            await Task.CompletedTask;
        }



    }
}
