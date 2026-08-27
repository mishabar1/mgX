using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
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


        // The catalog of creatable games — the SINGLE SOURCE OF TRUTH the client's "Create a game"
        // list is built from (type + label + PrimeNG icon). Adding a game = add a line here (plus the
        // CreateGame/AttachGameFlow cases). The client needs no change.
        // type + label + PrimeNG icon (fallback) + a cover image (relative to the games asset base).
        public record GameTypeInfo(string type, string label, string icon, string image);
        public static List<GameTypeInfo> GameCatalog() => new()
        {
            new(GameTypeEnum.TIK_TAK_TOE, "Tic-Tac-Toe",         "pi pi-th-large",   "covers/tictactoe.svg"),
            new(GameTypeEnum.CHESS,       "Chess",               "pi pi-flag",       "covers/chess.svg"),
            new(GameTypeEnum.GOMOKU,      "Gomoku",              "pi pi-circle-fill","covers/gomoku.svg"),
            new(GameTypeEnum.REVERSI,     "Reversi",             "pi pi-circle",     "covers/reversi.svg"),
            new(GameTypeEnum.CHECKERS,    "Checkers",            "pi pi-star",       "covers/checkers.svg"),
            new(GameTypeEnum.DND,         "D&D",                 "pi pi-compass",    "covers/dnd.svg"),
            new(GameTypeEnum.DURAK,       "Durak",               "pi pi-clone",      "covers/durak.svg"),
            new(GameTypeEnum.RESISTANCE,  "The Resistance",      "pi pi-users",      "covers/resistance.svg"),
            new(GameTypeEnum.SPLENDOR,    "Splendor",            "pi pi-wallet",     "covers/splendor.svg"),
            new(GameTypeEnum.CARCASSONNE, "Carcassonne",         "pi pi-map",        "covers/carcassonne.svg"),
            new(GameTypeEnum.CATAN,       "Catan",               "pi pi-sitemap",    "covers/catan.svg"),
            new(GameTypeEnum.ONE_NIGHT_WEREWOLF, "One Night Werewolf", "pi pi-moon", "covers/werewolf.svg"),
            new(GameTypeEnum.SMALL_WORLD, "Small World",     "pi pi-globe",      "covers/smallworld.svg"),
            new(GameTypeEnum.DEMO,        "Demo (dev reference)","pi pi-code",       "covers/demo.svg"),
        };

        private static string PrettyName(string type) => type switch
        {
            GameTypeEnum.TIK_TAK_TOE => "Tic-Tac-Toe",
            GameTypeEnum.CHESS => "Chess",
            GameTypeEnum.DND => "D&D",
            GameTypeEnum.GOMOKU => "Gomoku",
            GameTypeEnum.REVERSI => "Reversi",
            GameTypeEnum.CHECKERS => "Checkers",
            GameTypeEnum.DURAK => "Durak",
            GameTypeEnum.RESISTANCE => "The Resistance",
            GameTypeEnum.DEMO => "Demo (dev reference)",
            GameTypeEnum.SPLENDOR => "Splendor",
            GameTypeEnum.CARCASSONNE => "Carcassonne",
            GameTypeEnum.CATAN => "Catan",
            GameTypeEnum.ONE_NIGHT_WEREWOLF => "One Night Werewolf",
            GameTypeEnum.SMALL_WORLD => "Small World",
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
                case GameTypeEnum.RESISTANCE:
                    game.GameFlow = new ResistanceGameFlow(game);
                    break;
                case GameTypeEnum.DEMO:
                    game.GameFlow = new DemoGameFlow(game);
                    break;
                case GameTypeEnum.SPLENDOR:
                    game.GameFlow = new SplendorGameFlow(game);
                    break;
                case GameTypeEnum.CARCASSONNE:
                    game.GameFlow = new CarcassonneGameFlow(game);
                    break;
                case GameTypeEnum.CATAN:
                    game.GameFlow = new CatanGameFlow(game);
                    break;
                case GameTypeEnum.ONE_NIGHT_WEREWOLF:
                    game.GameFlow = new OneNightWerewolfGameFlow(game);
                    break;
                case GameTypeEnum.SMALL_WORLD:
                    game.GameFlow = new SmallWorldGameFlow(game);
                    break;
                default:
                    // Was an empty default: an unknown gameType left GameFlow null and the
                    // RunCreateFlow() call below NRE'd. Fail with something readable instead —
                    // gameType comes straight off the wire.
                    throw new ArgumentException($"Unknown game type '{gameType}'.", nameof(gameType));
            }

            game.GameStatus = GameStatusEnum.CREATED;
            game.CreatorId = userId;

            // Friendly name: game type + an auto number (e.g. "Chess 2"), instead of the
            // random "Colour Animal" default.
            int seq = (DataRepository.Singleton?.Games?.Count(g => g.GameType == game.GameType) ?? 0) + 1;
            game.Name = PrettyName(game.GameType) + " " + seq;

            // Run the create flow to completion BEFORE returning/persisting the game. Every game's
            // Create() completes synchronously (returns Task.CompletedTask), so this does not block;
            // but it ensures seats/attributes are populated and — crucially — that any exception in
            // Create() propagates to the caller instead of being silently swallowed by a discarded task.
            game.GameFlow.RunCreateFlow().GetAwaiter().GetResult();

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
            var preservedKeys = new[] { "allowVoice", "voiceSpectators", "showHeads", "cardBack", "noAvatars", "usesCardBack" };
            var preserved = preservedKeys
                .Where(k => this.GameData.Attributes.ContainsKey(k))
                .ToDictionary(k => k, k => this.GameData.Attributes[k]);
            this.GameData.Attributes.Clear();
            foreach (var kv in preserved) this.GameData.Attributes[kv.Key] = kv.Value;

            foreach (var player in GameData.Players)
            {
                player.Hand = new ItemData("", null) { Name = "PLAYER TABLE" };
                player.Table = new ItemData("", null) { Name = "PLAYER TABLE" };
                // Drop the finished game's control panel too. It used to survive a restart, so
                // between Setup and Start the client kept rendering the PREVIOUS game's panel —
                // live-looking, but its buttons now hit the "game is not in PLAY" gate.
                player.Screen = null;
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

            // Standard per-seat HAND/TABLE anchor placement (the client hard-codes none of this).
            // A game may override any of these in StartGame() below.
            GameData.Attributes["tableAnchor"] = "0,-1.5,1.5";
            GameData.Attributes["tableRot"]    = "0,0,0";
            GameData.Attributes["handAnchor"]  = "0,0,1.5";
            GameData.Attributes["handRot"]     = "-90,0,0";   // cards lie flat, face up

            await StartGame();
            RefreshScreens();   // build the server-driven per-seat panels for the fresh game

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

        // Server-driven UI hook: build each seat's PlayerData.Screen (the 2D panel the dumb client
        // renders). Called on start and after every action. Games with a panel override this;
        // games that render their panel elsewhere (Resistance/Demo build it in Render/StartGame)
        // or have no panel leave it as a no-op.
        protected virtual void RefreshScreens() { }

        // ---------------------------------------------------------------------
        // PER-SEAT REDACTION
        //
        // GameData is ONE object sent to everyone watching the game, so whatever a game keeps in
        // Attributes is on the wire even when the UI shows it to a single seat. That was fine
        // while "hidden" meant "not drawn"; it is not fine for Resistance / One Night Werewolf,
        // where opening devtools hands you the whole game.
        //
        // Cost control: HasHiddenInfo is FALSE by default, and DataRepository then sends one
        // shared payload to the whole game group and makes no copies at all — so the games with
        // nothing to hide pay nothing for this. A game that opts in gets one DeepCopy per
        // DISTINCT WATCHING USER, and RedactFor mutates that private copy.
        // ---------------------------------------------------------------------

        /// <summary>
        /// True if this game keeps information some seats may not see. Opting in switches
        /// DataRepository.HubGameUpdated to the per-viewer copy + RedactFor path.
        /// </summary>
        public virtual bool HasHiddenInfo => false;

        /// <summary>
        /// Strip everything <paramref name="userId"/> is not allowed to see.
        /// <paramref name="view"/> is a PRIVATE DeepCopy for this one viewer — mutate it freely.
        /// NEVER read or write this.GameData here: the live game must not end up redacted.
        /// Only called when <see cref="HasHiddenInfo"/> is true.
        /// </summary>
        public virtual void RedactFor(GameData view, string? userId) { }

        /// <summary>Every seat held by this user (one person can hold several seats in hotseat).</summary>
        protected static HashSet<string> SeatIdsOf(GameData view, string? userId)
        {
            var seats = new HashSet<string>();
            if (string.IsNullOrEmpty(userId) || view.Players == null) return seats;
            foreach (var p in view.Players)
                if (p.User?.Id == userId) seats.Add(p.Id);
            return seats;
        }

        /// <summary>
        /// Remove attributes named "&lt;prefix&gt;&lt;seatId&gt;" for every seat this user does NOT hold.
        /// For per-seat secrets the owner IS allowed to see — e.g. a Resistance role.
        /// </summary>
        protected static void RedactOtherSeatKeys(GameData view, IEnumerable<string> prefixes, HashSet<string> mySeats)
        {
            if (view.Attributes == null) return;
            foreach (var prefix in prefixes)
            {
                var doomed = view.Attributes.Keys
                    .Where(k => k.StartsWith(prefix) && !mySeats.Contains(k.Substring(prefix.Length)))
                    .ToList();
                foreach (var k in doomed) view.Attributes.Remove(k);
            }
        }

        /// <summary>
        /// Remove EVERY attribute starting with one of these prefixes, the viewer's own included.
        /// For secrets nobody may see yet — e.g. a One Night Werewolf card: you don't know your own.
        /// </summary>
        protected static void RedactAllKeys(GameData view, IEnumerable<string> prefixes)
        {
            if (view.Attributes == null) return;
            foreach (var prefix in prefixes)
            {
                var doomed = view.Attributes.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in doomed) view.Attributes.Remove(k);
            }
        }

        /// <summary>
        /// Blank every other seat's Screen. The client renders only its OWN seat's panel
        /// (game-play.component.ts finds players.find(p =&gt; p.user?.id === me &amp;&amp; p.screen)), and in
        /// a hidden-role game another seat's panel spells its secret out in words ("You are a SPY").
        /// Also the single biggest payload saving in these games: N panels become 1.
        /// </summary>
        protected static void RedactOtherScreens(GameData view, HashSet<string> mySeats)
        {
            if (view.Players == null) return;
            foreach (var p in view.Players)
                if (!mySeats.Contains(p.Id)) p.Screen = null;
        }

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

        /// <summary>
        /// Handle one client-originated action.
        /// </summary>
        /// <param name="data">The wire payload. Everything on it is client-controlled and untrusted.</param>
        /// <param name="callerUserId">
        /// The AUTHENTICATED user id, taken from the JWT by NotificationHub — never from the payload.
        /// Required: an action that cannot be attributed to a signed-in user is refused.
        /// </param>
        public async Task ExecuteAction(ExecuteActionData data, string? callerUserId)
        {
            await _turnLock.WaitAsync();
            try
            {
                if (!AuthorizeCaller(data, callerUserId)) return;

                await DispatchAction(data);
                await AfterAction();
            }
            finally { _turnLock.Release(); }
        }

        /// <summary>
        /// Gate every action on three things the payload cannot be trusted to assert:
        /// the game is actually in play, the named seat exists, and the AUTHENTICATED caller
        /// is the user sitting in it.
        ///
        /// Before this, DispatchAction resolved the actor purely from the client-supplied
        /// `playerId`, so any connected client could act as any seat in any game — and every
        /// per-game "is it your turn" check was comparing a CLAIM, not an identity.
        /// </summary>
        private bool AuthorizeCaller(ExecuteActionData data, string? callerUserId)
        {
            if (data == null) return false;

            // Actions only exist during play. Previously they were accepted in CREATED/SETUP
            // (before StartGame had built the scene) and after ENDED (play continued past game
            // over in any game whose actions lack their own "over" guard).
            if (GameData.GameStatus != GameStatusEnum.PLAY)
            {
                Console.WriteLine($"Rejected action '{data.actionId}' — game is {GameData.GameStatus}, not {GameStatusEnum.PLAY}.");
                return false;
            }

            if (string.IsNullOrEmpty(callerUserId))
            {
                Console.WriteLine($"Rejected action '{data.actionId}' — unauthenticated caller.");
                return false;
            }

            var seat = GameData.FindPlayer(data.playerId);
            if (seat == null)
            {
                Console.WriteLine($"Rejected action '{data.actionId}' — no seat '{data.playerId}' in this game.");
                return false;
            }

            // The seat must be a human seat occupied by THIS user. This is what makes every
            // downstream turn check meaningful; a spectator, an empty seat or somebody else's
            // seat can no longer be driven from the wire.
            if (seat.Type != PlayerTypeEnum.HUMAN || seat.User?.Id != callerUserId)
            {
                Console.WriteLine($"Rejected action '{data.actionId}' — user '{callerUserId}' does not occupy seat '{data.playerId}'.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// True when whoever is acting is entitled to move for <paramref name="seatToMove"/>.
        ///
        /// Two ways to qualify:
        ///  * the actor IS that seat — which is how an AI re-enters its own [GameAction]
        ///    (TikTakToeGameFlow.PlayAI routes its move through HoverClick so the placement /
        ///    turn / win logic all runs). This path never comes off the wire: AuthorizeCaller
        ///    has already refused any payload naming a seat the caller doesn't occupy;
        ///  * the actor is a seat held by the SAME USER as the seat to move — hotseat, one
        ///    person holding several colours.
        ///
        /// A seat with no user (AI or empty) can therefore never be driven by somebody else,
        /// which is the hole the old <c>current.User != null &amp;&amp; ...</c> form left open:
        /// it skipped the check entirely whenever the seat to move was an AI, so in any
        /// human-vs-AI game the human could play the AI's moves.
        /// </summary>
        protected static bool ControlsSeat(ExecuteActionData data, PlayerData? seatToMove)
        {
            if (seatToMove == null || data?.Player == null) return false;
            if (data.Player.Id == seatToMove.Id) return true;
            return seatToMove.User?.Id != null && data.Player.User?.Id == seatToMove.User.Id;
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

                // The action must actually be BOUND to the clicked item for this seat. The client
                // only ever dispatches `clickActions[mySeatId] || clickActions['']` (mg.game.ts:1102),
                // so this rejects nothing a real client sends — it just stops a crafted payload from
                // invoking an action on an item that never offered it (e.g. calling MoveHere with an
                // arbitrary item to teleport a piece). Panel buttons carry no item and are unaffected.
                if (data.Item != null && !ItemOffersAction(data.Item, data.playerId, data.actionId))
                {
                    Console.WriteLine($"Rejected action '{data.actionId}' — item '{data.itemId}' does not offer it to seat '{data.playerId}'.");
                    return;
                }

                // An action that throws used to propagate out of here and skip AfterAction()
                // entirely, so mutations already applied were NEITHER broadcast NOR saved and the
                // clients silently diverged from server memory. Contain it: log, then let
                // AfterAction run so everyone re-syncs to whatever the real state now is.
                // (Bad args are easy to reach — Carcassonne:191 int.Parse's a raw arg, and
                // BaseData.GetNumberAttribute Convert.ToDouble's whatever string it finds.)
                try
                {
                    await (Task)theMethod.Invoke(this, new object[] { data })!;
                }
                catch (Exception ex)
                {
                    var real = (ex as TargetInvocationException)?.InnerException ?? ex;
                    Console.WriteLine($"Action '{data.actionId}' failed on game {GameData.Id} ({GameData.GameType}): {real}");
                    return;
                }

                // Remember who made the last HUMAN move — only that player may undo it (and only
                // their own move). Captured AFTER the move; the pre-move snapshot keeps the prior
                // value, so undoing reverts this too.
                if (data.Player.Type == PlayerTypeEnum.HUMAN)
                    GameData.Attributes["lastHumanActor"] = data.Player.Id;
            }
        }

        // Does this item expose this action to this seat? Games bind actions either to everyone
        // (the "" key) or to one seat id — see ItemData.AddAction.
        private static bool ItemOffersAction(ItemData item, string? playerId, string? actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return false;
            if (item.ClickActions == null) return false;

            if (item.ClickActions.TryGetValue("", out var any) && any == actionId) return true;
            if (!string.IsNullOrEmpty(playerId)
                && item.ClickActions.TryGetValue(playerId!, out var mine) && mine == actionId) return true;

            return false;
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

            RefreshScreens();   // rebuild the server-driven per-seat panels after every action

            HistoryGameData.Add(GameData.DeepCopy());

            await DataRepository.Singleton.HubGameUpdated(GameData);

            // The games LIST only shows a game's name, status and seats — none of which a
            // mid-game move changes. This used to fire on every single action, and every client
            // sitting on the lobby answered it with a full getGamesList() HTTP round-trip: one
            // click in one game turned into N list refetches across the server. Ping the lobby
            // only on the transition that is actually visible there.
            if (ended)
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
            if (asset == null) throw new InvalidOperationException("addAsset: asset is NULL");
            if (asset.Name == null) throw new InvalidOperationException("addAsset: asset.Name is NULL for type=" + asset.Type);
            if (this.GameData?.Assets == null) throw new InvalidOperationException("addAsset: GameData.Assets is NULL");
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
        // Selection + free movement.
        //
        // The MECHANICS live here so any game can reuse them (Chess calls SelectPieceCore from
        // its own rule-checked ChessSelect). The dispatchable [GameAction] WRAPPERS deliberately
        // do NOT: they live on FreeMoveGameFlow, which only the sandbox games inherit.
        //
        // Why: [GameAction] is Inherited = true, so a `[GameAction] SelectPiece` / `MoveHere`
        // declared here was dispatchable in ALL 14 games — and MoveHere writes piece.Position
        // straight from the client-supplied point with no rules attached. Any client could
        // select any item in Chess/Splendor/Catan and teleport it anywhere. Moving the wrappers
        // to an opt-in subclass takes that away from the 12 games that never wanted it.
        // ---------------------------------------------------------------------

        // Hooks so a specific game can show/hide move targets (Chess shows yellow markers).
        protected virtual void OnPieceSelected(ItemData? piece) { }
        protected virtual void OnMarkersClear() { }

        /// <summary>
        /// Called after a free move has been applied, with where the piece came from.
        /// Replaces the `if (GameData.GameType == "DND")` special case that used to sit inside
        /// MoveHere — game-specific behaviour belongs in the game, not the base class.
        /// </summary>
        protected virtual void OnFreeMoved(ItemData piece, double fromX, double fromZ) { }

        /// <summary>The select/toggle mechanic, with no [GameAction] on it. See the note above.</summary>
        protected async Task SelectPieceCore(ExecuteActionData data)
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

        /// <summary>The free-placement mechanic, with no [GameAction] on it. See the note above.</summary>
        protected async Task MoveHereCore(ExecuteActionData data)
        {
            if (GameData.Attributes.TryGetValue("selectedItem", out var selectedId)
                && !string.IsNullOrEmpty(selectedId))
            {
                var piece = GameData.FindItem(selectedId);
                if (piece != null)
                {
                    var oldX = piece.Position.X;
                    var oldZ = piece.Position.Z;
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

                    // Was: `if (GameData.GameType == "DND") { ...face the direction of travel... }`
                    // — a game name hard-coded into the base class. Now a hook the game overrides.
                    OnFreeMoved(piece, oldX, oldZ);
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
