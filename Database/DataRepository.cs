using MG.Server.BL;
using MG.Server.Entities;
using MG.Server.GameFlows;
using MG.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MG.Server.Database
{
    public class DataRepository
    {
        public IHubContext<NotificationHub> Hub;
        public List<UserData> Users;
        public List<GameData> Games;

        public static DataRepository Singleton;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        // (C4/H3) Guards the in-memory collections and the persistence round-trips so that
        // concurrent SignalR callbacks and AI timer ticks can't corrupt state mid-serialize.
        private readonly object _sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 64
        };

        public DataRepository(IHubContext<NotificationHub> hub, IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
            Users = new List<UserData>();
            Games = new List<GameData>();
            Hub = hub;

            DataRepository.Singleton = this;

            using (var db = _dbFactory.CreateDbContext())
            {
                db.Database.EnsureCreated();
            }

            LoadInternal();
        }

        // ------------------------------------------------------------------------------------
        // BROADCAST ROUTING. These used to be Clients.All, which meant every connected client
        // received the FULL GameData of every game on every action — a player in a Tic-Tac-Toe
        // game parsed and mark-and-sweep-diffed a ~200 KB Small World payload for a game they
        // were not in. Cost scaled with (clients x active games), nearly all of it discarded by
        // the `if (data.id !== this.gameId) return` guard on the client.
        //
        // Now there are two audiences, and a connection joins one of them explicitly:
        //   * GAME group  ("game:<id>") — the connections currently VIEWING that game, i.e. the
        //     game-play and game-setup views. Seated players AND spectators; membership means
        //     "has this game open", not "owns a seat".
        //   * LOBBY group ("lobby")     — the connections sitting on the games list.
        // Joined via NotificationHub.WatchGame / WatchLobby; see SignalrService on the client.
        // ------------------------------------------------------------------------------------

        /// <summary>SignalR group carrying one game's full state.</summary>
        internal static string GameGroup(string gameId) => "game:" + gameId;

        /// <summary>SignalR group carrying "the games list changed" pings.</summary>
        internal const string LobbyGroup = "lobby";

        internal async Task HubGameUpdated(GameData game)
        {
            await Save();

            // FAST PATH — nothing to hide (12 of 14 games). One shared payload, zero copies:
            // exactly what this did before redaction existed.
            var flow = game.GameFlow;
            if (flow == null || !flow.HasHiddenInfo)
            {
                await Hub.Clients.Group(GameGroup(game.Id)).SendAsync("GameUpdated", game);
                return;
            }

            // REDACTED PATH — Resistance / One Night Werewolf. Roles and cards live in
            // Attributes, and each seat's panel spells its own secret out in words, so a single
            // shared payload means anyone reading the socket sees the whole game. Build one view
            // per DISTINCT WATCHING USER (not per connection — two tabs of the same account get
            // the same view, and the group handles the fan-out).
            //
            // If nobody is watching, this sends nothing at all.
            foreach (var userId in NotificationHub.WatcherUserIds(game.Id))
            {
                var view = game.DeepCopy();          // private copy; RedactFor mutates it
                flow.RedactFor(view, userId);
                await Hub.Clients.Group(NotificationHub.GameUserGroup(game.Id, userId))
                                 .SendAsync("GameUpdated", view);
            }
        }

        internal async Task HubGamesUpdated(GameData game)
        {
            await Save();
            await Hub.Clients.Group(LobbyGroup).SendAsync("GamesUpdated", game.Id);
        }

        /// <param name="save">
        /// Pass false when deleting several games at once: Save() serializes EVERY game, so saving
        /// once per deletion would re-serialize the whole store N times for one user action.
        /// The caller is then responsible for one Save() at the end.
        /// </param>
        internal async Task HubGameDeleted(string gameId, bool save = true)
        {
            if (save) await Save();
            // Both audiences: the lobby drops it from the list, and anyone with the game still
            // open has to be kicked back to the list. A connection in both groups (only possible
            // transiently, mid-navigation) may get this twice — both client handlers are
            // idempotent (navigate / refetch), so a duplicate is harmless.
            await Hub.Clients.Groups(new[] { LobbyGroup, GameGroup(gameId) })
                             .SendAsync("GameDeleted", gameId);
        }

        private void LoadInternal()
        {
            lock (_sync)
            {
                try
                {
                    using var db = _dbFactory.CreateDbContext();
                    var usersRow = db.Store.AsNoTracking().FirstOrDefault(x => x.Key == "users");
                    var gamesRow = db.Store.AsNoTracking().FirstOrDefault(x => x.Key == "games");

                    Users = usersRow == null
                        ? new List<UserData>()
                        : JsonSerializer.Deserialize<List<UserData>>(usersRow.Json, JsonOpts) ?? new List<UserData>();

                    Games = gamesRow == null
                        ? new List<GameData>()
                        : JsonSerializer.Deserialize<List<GameData>>(gamesRow.Json, JsonOpts) ?? new List<GameData>();

                    // Rebuild runtime-only state that is [JsonIgnore]'d: the GameFlow behaviour
                    // object and the AI agent timers for games that are mid-play.
                    foreach (var game in Games)
                    {
                        AttachGameFlow(game);

                        if (game.GameStatus == GameStatusEnum.PLAY)
                        {
                            foreach (var player in game.Players)
                            {
                                if (player.Type == PlayerTypeEnum.AI)
                                {
                                    player.AIAgent = new AIAgent(game, player);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DataRepository.Load failed, starting empty: " + ex);
                    Users = new List<UserData>();
                    Games = new List<GameData>();
                }
            }
        }

        private static void AttachGameFlow(GameData game)
        {
            switch (game.GameType)
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
            }
        }

        public Task Save()
        {
            lock (_sync)
            {
                try
                {
                    // Serialize a snapshot under the lock so a concurrent mutation
                    // can't throw "collection was modified" mid-write.
                    var usersJson = JsonSerializer.Serialize(Users, JsonOpts);
                    var gamesJson = JsonSerializer.Serialize(Games, JsonOpts);

                    using var db = _dbFactory.CreateDbContext();
                    UpsertRow(db, "users", usersJson);
                    UpsertRow(db, "games", gamesJson);
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DataRepository.Save failed: " + ex);
                }
            }

            return Task.CompletedTask;
        }

        private static void UpsertRow(AppDbContext db, string key, string json)
        {
            var row = db.Store.FirstOrDefault(x => x.Key == key);
            if (row == null)
            {
                db.Store.Add(new StoreRecord { Key = key, Json = json });
            }
            else
            {
                row.Json = json;
            }
        }
    }
}
