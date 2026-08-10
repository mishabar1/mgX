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

            // Not in play (game over, or between setup/start) → idle, but KEEP polling so the
            // agent resumes if the game returns to PLAY (e.g. after an undo or a restart).
            if (this.gameData.GameStatus != GameStatusEnum.PLAY)
            {
                timer.Start();
                return;
            }

            // Not this AI's turn → wait for the next tick. Each game decides what
            // "this player's turn" means (Chess uses a white/black turn attribute).
            if (!gameData.GameFlow.IsAITurn(player))
            {
                timer.Start();
                return;
            }

            // (H1) this runs from an async-void timer handler; a thrown exception here would
            // otherwise crash the process. Contain and log it.
            try
            {
                await gameData.GameFlow.RunAITurn(player, rnd);
            }
            catch (Exception ex)
            {
                Console.WriteLine("AIAgent action failed: " + ex);
            }

            //continue the timer
            timer.Start();


        }

        // Permanently stop this agent (on restart replacement, seat change, or game delete).
        public void Stop()
        {
            try { timer.Stop(); timer.Dispose(); } catch { }
        }


    }
}
