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

        internal async Task HubGameUpdated(GameData game)
        {
            await Save();
            await Hub.Clients.All.SendAsync("GameUpdated", game);
        }

        internal async Task HubGamesUpdated(GameData game)
        {
            await Save();
            await Hub.Clients.All.SendAsync("GamesUpdated", game.Id);
        }

        internal async Task HubGameDeleted(string gameId)
        {
            await Save();
            await Hub.Clients.All.SendAsync("GameDeleted", gameId);
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
