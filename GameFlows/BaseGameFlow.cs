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
                case GameTypeEnum.GOMOKU:
                    game.GameFlow = new GomokuGameFlow(game);
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
            // Clear per-game state (turn, en-passant, game-over flag, result, …) so a
            // replay/restart starts clean — otherwise a stale "over" ended the new game
            // on the first move. Each game's StartGame repopulates what it needs.
            this.GameData.Attributes.Clear();

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
            await DispatchAction(data);
            await AfterAction();
        }

        // Dispatch a single action by name to a [GameAction]-marked method (no broadcast).
        private async Task DispatchAction(ExecuteActionData data)
        {
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
        }

        // Shared tail: end-game check, snapshot history, and broadcast the new state.
        private async Task AfterAction()
        {
            var ended = await IsEndGame();
            if (ended)
            {
                this.GameData.GameStatus = GameStatusEnum.ENDED;
                this.GameData.Winners = GetGameWinners();
                Console.WriteLine("GAME ENDED !!!!!! winners count: " + this.GameData.Winners.Count());
                await RunEndGameFlow();
            }

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);
            await DataRepository.Singleton.HubGamesUpdated(GameData);
        }

        // ---------------------------------------------------------------------
        // AI hooks. An AIAgent ticks on a timer and, when it's this player's turn,
        // calls RunAITurn. Games with special turn/movement models (e.g. Chess)
        // override IsAITurn and PlayAI.
        // ---------------------------------------------------------------------

        /// <summary>Is it this AI player's turn to act?</summary>
        public virtual bool IsAITurn(PlayerData player) => GameData.CurrentTurnId == player.Id;

        /// <summary>Play the AI's move (if any) and broadcast the result.</summary>
        public async Task RunAITurn(PlayerData player, Random rnd)
        {
            if (await PlayAI(player, rnd))
                await AfterAction();
        }

        /// <summary>Make one AI move. Returns true if a move was made.
        /// Default: pick a random clickable item and run its action.</summary>
        public virtual async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            var items = GameData.GetAllGameItems()
                .Where(i => i.ClickActions.ContainsKey("") || i.ClickActions.ContainsKey(player.Id))
                .ToList();
            if (items.Count == 0) return false;

            var item = items[rnd.Next(0, items.Count)];
            var action = new ExecuteActionData
            {
                actionId = item.ClickActions.GetValueOrDefault("", item.ClickActions.GetValueOrDefault(player.Id)),
                gameId = GameData.Id,
                playerId = player.Id,
                itemId = item.Id
            };
            await DispatchAction(action);
            return true;
        }

        internal AssetData addAsset(AssetData asset)
        {
            // Idempotent: deterministic keys mean re-adding the same asset is a no-op
            // rather than a duplicate-key crash.
            this.GameData.Assets[asset.Name] = asset;
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

        // Human-readable name for a seat: the joined user's name, "AI", or "open".
        internal static string PlayerDisplayName(PlayerData p)
        {
            if (p == null) return "?";
            if (!string.IsNullOrEmpty(p.User?.Name)) return p.User!.Name!;
            return p.Type == PlayerTypeEnum.AI ? "AI" : "open";
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

        // Hooks so a specific game can show/hide move targets (Chess shows yellow markers).
        protected virtual void OnPieceSelected(ItemData? piece) { }
        protected virtual void OnMarkersClear() { }

        [GameAction]
        public async Task SelectPiece(ExecuteActionData data)
        {
            // Drop any previously-selected piece and clear its markers.
            ClearSelection();

            // Remember the selection and mark the item so the client highlights it IN PLACE
            // (no lift, no move — the piece stays put and just glows).
            GameData.Attributes["selectedItem"] = data.itemId;
            if (data.Item != null) data.Item.Attributes["selected"] = "1";

            // Let the game show where the piece can go (Chess: yellow square markers).
            OnPieceSelected(data.Item);

            await Task.CompletedTask;
        }

        [GameAction]
        public async Task MoveHere(ExecuteActionData data)
        {
            if (GameData.Attributes.TryGetValue("selectedItem", out var selectedId)
                && !string.IsNullOrEmpty(selectedId))
            {
                var piece = GameData.FindItem(selectedId);
                if (piece != null)
                {
                    if (data.Item != null && data.Item.HaveAttribute("moveMarker"))
                    {
                        // Clicked a move marker → snap the piece to that exact square.
                        piece.Position.X = data.Item.Position.X;
                        piece.Position.Z = data.Item.Position.Z;
                    }
                    else if (data.point != null)
                    {
                        // Clicked the board surface → free placement at the click point.
                        piece.Position.X = data.point.X;
                        piece.Position.Z = data.point.Z;
                    }
                    piece.Position.Y = 0; // drop it back down
                }
            }
            ClearSelection();
            await Task.CompletedTask;
        }

        protected void ClearSelection()
        {
            if (GameData.Attributes.TryGetValue("selectedItem", out var prevId)
                && !string.IsNullOrEmpty(prevId))
            {
                var prev = GameData.FindItem(prevId);
                if (prev != null) prev.Attributes.Remove("selected"); // un-highlight
            }
            GameData.Attributes.Remove("selectedItem");
            OnMarkersClear();
        }



    }
}
