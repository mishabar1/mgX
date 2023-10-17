using MG.Server.Controllers;
using MG.Server.Entities;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class CatanGameFlow : BaseGameFlow
    {
        public CatanGameFlow(GameData gameData) : base(gameData)
        {
        }


        public override async Task Setup()
        {
            Console.WriteLine("CatanGameFlow Setup " + this.GameData);
        }
        public override Task StartGame()
        {
            throw new NotImplementedException();
        }

        public override Task EndGame()
        {
            throw new NotImplementedException();
        }
    }
}
