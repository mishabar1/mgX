using MG.Server.Controllers;
using MG.Server.Entities;
using System.Reflection;

namespace MG.Server.GameFlows
{
    public abstract class BaseGameFlow
    {

        public GameData GameData { get; set; }


        public static GameData CreateGame(GameTypeEnum GameType)
        {
            var game = new GameData();
            switch (GameType)
            {
                case GameTypeEnum.TIK_TAK_TOE:
                    game.GameFlow = new TikTakToeGameFlow(game);
                    break;
                case GameTypeEnum.CATAN:
                    game.GameFlow = new CatanGameFlow(game);
                    break;
                case GameTypeEnum.DND:
                    game.GameFlow = new DnDGameFlow(game);
                    break;
                default:
                    break;
            }
           
            return game;
        }
        public BaseGameFlow(GameData gameData)
        {
            GameData = gameData;
            GameData.GameFlow = this;
        }
        public abstract Task Setup();
        public abstract Task StartGame();
        public abstract Task EndGame();

        //public abstract Task ExecuteAction(ExecuteActionData data);

        public async Task ExecuteAction(ExecuteActionData data)
        {
            data.Item = GameData.FindItem(data.ItemId);
            data.Player = GameData.FindPlayer(data.PlayerId);
            if(data.Item != null && data.Player != null)
            {
                Console.WriteLine("TikTakToeGameFlow ExecuteAction " + data);
                Type thisType = GetType();
                MethodInfo theMethod = thisType.GetMethod(data.ActionId);
                theMethod.Invoke(this, new object[] { data });
            }

            
        }
    }
}
