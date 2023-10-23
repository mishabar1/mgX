using MG.Server.BL;
using MG.Server.Entities;
using MG.Server.Services;
using Microsoft.AspNetCore.SignalR;

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
        }

        internal async Task HubGameUpdated(GameData game)
        {
            await Hub.Clients.All.SendAsync("GameUpdated", game);            
        }
        internal async Task HubGamesUpdated(GameData game)
        {
            await Hub.Clients.All.SendAsync("GamesUpdated", game.Id);
        }
        internal async Task HubGameDeleted(string gameId)
        {
            await Hub.Clients.All.SendAsync("GameDeleted", gameId);
        }
    }
}