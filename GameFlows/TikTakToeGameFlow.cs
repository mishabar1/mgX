using MG.Server.Controllers;
using MG.Server.Entities;
using System.Reflection;
using System.Security.AccessControl;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MG.Server.GameFlows
{
    public class TikTakToeGameFlow : BaseGameFlow
    {
        public TikTakToeGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.TIK_TAK_TOE;
        }

        
        public override async Task Setup()
        {
            Console.WriteLine("TikTakToeGameFlow Setup " + this.GameData);
            
            // create aseets
            var assetA = new AssetData();
            var assetB = new AssetData();

            // set players
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.HUMAN };
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.AI };
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.AI };
                        
            // set "setup" items
            var i1 = new ItemData(this.GameData, assetA)
            {
                Position = new V3(0.5, 0.5, 0.5)
            };
            var i2 = new ItemData(this.GameData, assetB, i1)
            {
                Position = new V3(0, 0.05, 0.05)
            };
            var i3 = new ItemData(this.GameData, assetB, i1)
            {
                Position = new V3(0, -0.05, -0.05)
            };
            var i4 = new ItemData(this.GameData, assetA)
            {
                Position = new V3(0, 0.3, 0.1)
            };


            

            i1.AddAction(MyItemClick111);
            i4.AddAction(MyItemClick444);
        }

        public async Task MyItemClick111(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick111 " + data);

            data.Item.SetPosition(1, 1, 1);
        }

        public async Task MyItemClick444(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick444 " + data);
        }

        public async override Task StartGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow StartGame " + this.GameData);
            
        }

        public async override Task EndGame()
        {
            // TODO !!!
            Console.WriteLine("TikTakToeGameFlow EndGame " + this.GameData);
            
        }
    }
}
