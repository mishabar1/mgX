using MG.Server.Controllers;
using MG.Server.Entities;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class ChessGameFlow : BaseGameFlow
    {
        public ChessGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CHESS;
        }

        protected override async Task Create()
        {

        }

        protected override async Task Setup()
        {
            //Console.WriteLine("ChessGameFlow Setup " + this.GameData);
            throw new NotImplementedException();
        }
        protected override Task StartGame()
        {
            //Console.WriteLine("ChessGameFlow StartGame " + this.GameData);
            throw new NotImplementedException();
        }

        protected override Task EndGame()
        {
            //Console.WriteLine("ChessGameFlow EndGame " + this.GameData);
            throw new NotImplementedException();
        }

        protected override Task<bool> IsEndGame()
        {
            throw new NotImplementedException();
        }

        protected override List<PlayerData> GetGameWinners()
        {
            throw new NotImplementedException();
        }
    }
}
