﻿using MG.Server.Entities;
using MG.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace MG.Server.Database
{
    public class DataRepository
    {
        public IHubContext<NotificationHub> Hub;
        public List<UserData> Users;// = new List<UserData>();
        public List<GameData> Games;// = new List<GameData>();

        public DataRepository(IHubContext<NotificationHub> hub)
        {
            Users = new List<UserData>();
            Games = new List<GameData>();
            Hub = hub;

        }

        internal void HubGameUpdated(GameData game)
        {
            Hub.Clients.All.SendAsync("GameUpdated", game);
        }
        internal void HubGamesUpdated(GameData game)
        {
            Hub.Clients.All.SendAsync("GamesUpdated", game.Id);
        }
    }
}