using MG.Server.Controllers;
using MG.Server.Entities;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class CatanGameFlow : BaseGameFlow
    {
        public CatanGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CATAN;
        }


        public override async Task Setup()
        {
            Console.WriteLine("CatanGameFlow Setup " + this.GameData);
            throw new NotImplementedException();
        }
        public override Task StartGame()
        {
            Console.WriteLine("CatanGameFlow StartGame " + this.GameData);
            throw new NotImplementedException();
        }

        public override Task EndGame()
        {
            Console.WriteLine("CatanGameFlow EndGame " + this.GameData);
            throw new NotImplementedException();
        }

        public override Task<bool> IsEndGame()
        {
            throw new NotImplementedException();
        }

        public override List<PlayerData> GetGameWinners()
        {
            throw new NotImplementedException();
        }
    }
}
