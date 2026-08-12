using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Linq;
using System.Reflection;
using static MG.Server.GameFlows.TikTakToeGameFlow;

namespace MG.Server.GameFlows
{
    public abstract class BaseGameFlow
    {

        public GameData GameData { get; set; }
        public List<GameData> HistoryGameData { get; set; }

        // How many seats must be occupied (HUMAN or AI) before the game may start. By default
        // EVERY seat the game creates is mandatory; a game with optional seats can override this.
        public virtual int MinPlayers => GameData.Players?.Count ?? 0;

        // Seats currently taken by a human or an AI (EMPTY_SEAT doesn't count).
        public int OccupiedSeats => GameData.Players?.Count(p => p.Type != PlayerTypeEnum.EMPTY_SEAT) ?? 0;

        public bool CanStart => OccupiedSeats >= MinPlayers;


        private static string PrettyName(string type) => type switch
        {
            GameTypeEnum.TIK_TAK_TOE => "Tic-Tac-Toe",
            GameTypeEnum.CHESS => "Chess",
            GameTypeEnum.DND => "D&D",
            GameTypeEnum.GOMOKU => "Gomoku",
            GameTypeEnum.REVERSI => "Reversi",
            GameTypeEnum.CHECKERS => "Checkers",
            GameTypeEnum.DURAK => "Durak",
            _ => type
        };

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
                case GameTypeEnum.REVERSI:
                    game.GameFlow = new ReversiGameFlow(game);
                    break;
                case GameTypeEnum.CHECKERS:
                    game.GameFlow = new CheckersGameFlow(game);
                    break;
                case GameTypeEnum.DURAK:
                    game.GameFlow = new DurakGameFlow(game);
                    break;
                default:
                    break;
            }

            game.GameStatus = GameStatusEnum.CREATED;
            game.CreatorId = userId;

            // Friendly name: game type + an auto number (e.g. "Chess 2"), instead of the
            // random "Colour Animal" default.
            int seq = (DataRepository.Singleton?.Games?.Count(g => g.GameType == game.GameType) ?? 0) + 1;
            game.Name = PrettyName(game.GameType) + " " + seq;

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
            // Publish the required-seat count so the client can gate Start.
            this.GameData.MinPlayers = this.MinPlayers;
        }
        protected abstract Task Create();

        public async Task RunSetupFlow()
        {
            // reset all
            this.GameData.Table = ItemData.Table();
            this.GameData.Winners = null;
            this.GameData.CurrentTurnId = null;
            this.GameData.GameStatus = GameStatusEnum.SETUP;
            // Clear per-game RUNTIME state (turn, en-passant, game-over flag, result, …) so a
            // replay/restart starts clean — otherwise a stale "over" ended the new game
            // on the first move. Each game's StartGame repopulates what it needs.
            // BUT preserve persistent game SETTINGS that also live in Attributes (e.g. the
            // voice-chat config) — otherwise Setup/Restart would silently reset them.
            var preservedKeys = new[] { "allowVoice", "voiceSpectators", "showHeads", "cardBack" };
            var preserved = preservedKeys
                .Where(k => this.GameData.Attributes.ContainsKey(k))
                .ToDictionary(k => k, k => this.GameData.Attributes[k]);
            this.GameData.Attributes.Clear();
            foreach (var kv in preserved) this.GameData.Attributes[kv.Key] = kv.Value;

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
            // Safety net (the client also gates the Start button): never start until every
            // mandatory seat is occupied by a human or an AI.
            this.GameData.MinPlayers = this.MinPlayers;
            if (!CanStart) return;

            this.GameData.GameStatus = GameStatusEnum.PLAY;

            await StartGame();

            // (Re)create AI agents — exactly one per current AI seat. Stop any prior agent first
            // so a restart, or a seat that changed to/from AI, can't leave a duplicate ticking.
            foreach (var player in this.GameData.Players)
            {
                player.AIAgent?.Stop();
                player.AIAgent = null;
                if (player.Type == PlayerTypeEnum.AI)
                    player.AIAgent = new AIAgent(this.GameData, player);
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

        // Serializes all state mutations for THIS game (human actions, AI turns, undo) so a
        // background AI-timer tick can't interleave with a SignalR action and corrupt state.
        private readonly System.Threading.SemaphoreSlim _turnLock = new(1, 1);

        // Snapshots taken just before each real move, for undo/takeback. The full history is
        // kept so you can undo repeatedly, all the way back to the start of the game.
        private readonly List<GameData> _undo = new();
        protected void SaveUndoPoint()
        {
            _undo.Add(GameData.DeepCopy());
        }

        public async Task ExecuteAction(ExecuteActionData data)
        {
            await _turnLock.WaitAsync();
            try
            {
                await DispatchAction(data);
                await AfterAction();
            }
            finally { _turnLock.Release(); }
        }

        // Whose turn is it right now? Default uses CurrentTurnId; games that track turn via
        // an attribute (chess/checkers/…) override this. Used by undo to rewind to a human.
        protected virtual PlayerData? CurrentTurnPlayer()
            => GameData.Players?.FirstOrDefault(p => p.Id == GameData.CurrentTurnId);

        // Revert to the last HUMAN turn: pop snapshots (restoring board/attributes/turn — the
        // seat list is kept so AI agents stay valid) past any AI moves, so undoing in a vs-AI
        // game takes back both the AI's reply and your move, and the AI won't just replay.
        public async Task UndoLastMove()
        {
            await _turnLock.WaitAsync();
            try
            {
                if (_undo.Count == 0) return;
                do
                {
                    var snap = _undo[_undo.Count - 1];
                    _undo.RemoveAt(_undo.Count - 1);

                    GameData.Table = snap.Table;
                    GameData.Attributes = snap.Attributes;
                    GameData.Winners = snap.Winners;
                    GameData.CurrentTurnId = snap.CurrentTurnId;
                    GameData.GameStatus = snap.GameStatus;
                }
                while (_undo.Count > 0 && CurrentTurnPlayer()?.Type == PlayerTypeEnum.AI);

                // Let games that render from their own state (e.g. Durak, whose hands live in
                // player.Hand, not GameData.Table) rebuild the scene from the restored attributes.
                AfterUndo();

                await DataRepository.Singleton.HubGameUpdated(GameData);
                await DataRepository.Singleton.HubGamesUpdated(GameData);
            }
            finally { _turnLock.Release(); }
        }

        // Hook: rebuild the scene after an undo restore. Board games keep everything in
        // GameData.Table (restored above) so they don't need it; Durak overrides to Render().
        protected virtual void AfterUndo() { }

        // Dispatch a single action by name to a [GameAction]-marked method (no broadcast).
        private async Task DispatchAction(ExecuteActionData data)
        {
            data.Item = GameData.FindItem(data.itemId);
            data.Player = GameData.FindPlayer(data.playerId);
            // Item may be null for UI-driven actions (the DM console posts args, not a clicked
            // item); actions that need an item null-check it themselves. Player is still required.
            if (data.Player != null)
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

                // Remember who made the last HUMAN move — only that player may undo it (and only
                // their own move). Captured AFTER the move; the pre-move snapshot keeps the prior
                // value, so undoing reverts this too.
                if (data.Player.Type == PlayerTypeEnum.HUMAN)
                    GameData.Attributes["lastHumanActor"] = data.Player.Id;
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
            await _turnLock.WaitAsync();
            try
            {
                if (await PlayAI(player, rnd))
                    await AfterAction();
            }
            finally { _turnLock.Release(); }
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
            if (p.Type == PlayerTypeEnum.AI) return !string.IsNullOrEmpty(p.Name) ? p.Name! : "AI";
            return "open";
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
            // Toggle: clicking the piece that's already selected unselects it.
            if (GameData.Attributes.TryGetValue("selectedItem", out var curSel)
                && !string.IsNullOrEmpty(curSel) && curSel == data.itemId)
            {
                ClearSelection();
                await Task.CompletedTask;
                return;
            }

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
            // Keep the piece SELECTED so every subsequent board click moves it again. The user
            // unselects by clicking the piece itself (SelectPiece toggles it off).
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
