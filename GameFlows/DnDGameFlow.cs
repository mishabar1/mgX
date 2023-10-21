using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    public class DnDGameFlow : BaseGameFlow
    {
        public DnDGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DND;
        }

        public override async Task Setup()
        {
            Console.WriteLine("DnDGameFlow Setup " + this.GameData);
        }
        public override Task StartGame()
        {
            Console.WriteLine("DnDGameFlow StartGame " + this.GameData);
            throw new NotImplementedException();
        }

        public override Task EndGame()
        {
            Console.WriteLine("DnDGameFlow EndGame " + this.GameData);
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
