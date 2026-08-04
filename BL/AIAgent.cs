using MG.Server.Controllers;
using MG.Server.Entities;
using System.Diagnostics;
using System.Timers;

namespace MG.Server.BL
{
    public class AIAgent
    {
        private System.Timers.Timer timer;
        private GameData gameData;
        private PlayerData player;

        //PeriodicTimer t;
        Random rnd = new Random();
        //public static DataRepository _dataRepository;

        public AIAgent(GameData _gameData, PlayerData _player)
        {
            this.gameData = _gameData;
            this.player = _player;
            
            // (H2) was 1ms — an effective busy-loop per AI player. 800ms is far lighter
            // on CPU and reads as a natural "thinking" pause.
            timer = new System.Timers.Timer(800);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            Debug.WriteLine(DateTime.Now.Ticks + "Create Agent " + player.Name);

            
        }
                        
        private async void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            //Stop The Timer
            timer.Stop();

            //Debug.WriteLine(DateTime.Now.Ticks + " AIAgent onTimerTick " + player.Name);
            //await Task.Delay(3000);
            //Debug.WriteLine(DateTime.Now.Ticks + " 3000 " + player.Name);

            
            //Debug.WriteLine("STOP timer" + player.Name);

            if (this.gameData.GameStatus != GameStatusEnum.PLAY)
            {
                //Debug.WriteLine(DateTime.Now.Ticks + " GameStatus = " + this.gameData.GameStatus);                
                return;
            }

            if (gameData.CurrentTurnId != player.Id)
            {                
                //Debug.WriteLine(DateTime.Now.Ticks + " " + player.Name + " - NOT MY TURN");
                timer.Start();
                return;
            }
            

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


                ////*********************
                ////test ML - START
                ////Load model and predict output
                //var result = MLModel.Predict(new MLModel.ModelInput()
                //{
                //    Col0 = @"Crust is not good.",
                //});
                //Console.WriteLine(result.Col1);
                //result = MLModel.Predict(new MLModel.ModelInput()
                //{
                //    Col0 = @"Very good product, recomend to buy",
                //});
                //Console.WriteLine(result.Col1);
                ////test ML - FINISH
                ////*********************

                
                ExecuteActionData action = new ExecuteActionData()
                {
                    actionId = item.ClickActions.GetValueOrDefault("", item.ClickActions.GetValueOrDefault(player.Id)),
                    gameId = this.gameData.Id,
                    playerId = this.player.Id,
                    itemId = item.Id
                };
                //Debug.WriteLine(DateTime.Now.Ticks + " execute action " + action.actionId + "TIMER:"+player.Name);
                // (H1) this runs from an async-void timer handler; a thrown exception here would
                // otherwise crash the process. Contain and log it.
                try
                {
                    await gameData.GameFlow.ExecuteAction(action);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("AIAgent action failed: " + ex);
                }

            }

            //continue the timer
            timer.Start();
            //Debug.WriteLine(DateTime.Now.Ticks + " continue timer " + player.Name);


        }


    }
}
