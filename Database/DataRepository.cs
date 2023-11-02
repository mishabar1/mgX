using MG.Server.BL;
using MG.Server.Entities;
using MG.Server.GameFlows;
using MG.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MG.Server.Database
{
    public class DataRepository
    {
        public IHubContext<NotificationHub> Hub;
        public List<UserData> Users;// = new List<UserData>();
        public List<GameData> Games;// = new List<GameData>();

        public static DataRepository Singleton;

        public DataRepository(IHubContext<NotificationHub> hub)
        {
            Users = new List<UserData>();
            Games = new List<GameData>();
            Hub = hub;
            //await Hub.Clients.Group(user_id).SendAsync("test", "some data");

            //AIAgent._dataRepository = this;
            DataRepository.Singleton = this;

            Load();
        }

        internal async Task HubGameUpdated(GameData game)
        {
            //save db
            Save();

            await Hub.Clients.All.SendAsync("GameUpdated", game);            
        }
        internal async Task HubGamesUpdated(GameData game)
        {
            //save db
            Save();

            await Hub.Clients.All.SendAsync("GamesUpdated", game.Id);
        }
        internal async Task HubGameDeleted(string gameId)
        {
            //save db
            Save();

            await Hub.Clients.All.SendAsync("GameDeleted", gameId);
        }

        public async Task Load()
        {
            try
            {
                using StreamReader usersReader = new(@".\Database\users.json");
                var json = usersReader.ReadToEnd();
                Users = JsonSerializer.Deserialize<List<UserData>>(json);
                Console.WriteLine(Users);

                using StreamReader gamesReader = new(@".\Database\games.json");
                json = gamesReader.ReadToEnd();
                Games = JsonSerializer.Deserialize<List<GameData>>(json);
                Console.WriteLine(Games);

                Games.ForEach(game =>
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
                        default:
                            break;
                    }


                    if (game.GameStatus == GameStatusEnum.PLAY)
                    {
                        // create AI agents
                        foreach (var player in game.Players)
                        {
                            if (player.Type == PlayerTypeEnum.AI)
                            {
                                player.AIAgent = new AIAgent(game, player);
                            }
                        }
                    }

                });

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                //throw;
            }



        }

        public async Task Save()
        {
            string json = JsonSerializer.Serialize(Users);
            File.WriteAllText(@".\Database\users.json", json);

            json = JsonSerializer.Serialize(Games);
            File.WriteAllText(@".\Database\games.json", json);

        }
    }
}