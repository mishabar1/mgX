using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using MG.Server.Services;
using System.Collections.Generic;

namespace MG.Server.BL
{
    public class AIAgent
    {
        private Timer timer;
        private GameData gameData;
        private PlayerData player;

        //PeriodicTimer t;
        Random rnd = new Random();
        //public static DataRepository _dataRepository;

        public AIAgent(GameData _gameData, PlayerData _player)
        {
            this.gameData = _gameData;
            this.player = _player;
            timer = new Timer(onTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            //t = new PeriodicTimer(TimeSpan.FromSeconds(1));
            //t.WaitForNextTickAsync
        }

        private async void onTimerTick(object? state)
        {
            Console.WriteLine("AIAgent onTimerTick " + player.Name);                       

            if (this.gameData.GameStatus != GameStatusEnum.PLAY)
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                timer.Dispose();
                return;
            }

            if (gameData.CurrentTurnId != player.Id)
            {                
                return;
            }

            //Stop The Timer
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            var allGameItems = gameData.GetAllGameItems();

            // filter allowed actions
            allGameItems = allGameItems.Where(item =>
            {
                var allow = false;
                if (item.ClickActions.ContainsKey("") || item.ClickActions.ContainsKey(player.Id))
                {
                    allow = true;
                }

                return allow;
            }).ToList();

            if (allGameItems.Count > 0)
            {
                // play random
                var idx = rnd.Next(0, allGameItems.Count);
                var item = allGameItems[idx];
                

                ExecuteActionData action = new ExecuteActionData()
                {
                    actionId = item.ClickActions.GetValueOrDefault("", item.ClickActions.GetValueOrDefault(player.Id)),
                    gameId = this.gameData.Id,
                    playerId = this.player.Id,
                    itemId = item.Id
                };
                await gameData.GameFlow.ExecuteAction(action);
                await DataRepository.Singleton.HubGameUpdated(gameData);
                                
            }

            //continue the timer
            timer.Change(TimeSpan.FromSeconds(rnd.Next(1,5)), TimeSpan.FromSeconds(1));


        }


    }
}
