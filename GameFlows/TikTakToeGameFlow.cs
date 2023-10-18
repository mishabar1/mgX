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

            // set "setup" items
            new ItemData(this.GameData, assetA).SetPosition(-1, 0, 1).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(0, 0, 1).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(1, 0, 1).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(-1, 0, 0).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(0, 0, 0).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(1, 0, 0).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(-1, 0, -1).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(0, 0, -1).AddAttribute("click");
            new ItemData(this.GameData, assetA).SetPosition(1, 0, -1).AddAttribute("click");

            setActions();

            //var i4 = new ItemData(this.GameData, assetA)
            //{
            //    Position = new V3(0, 0.3, 0.1)
            //};

            //for (int i = 0; i < 1000; i++)
            //{
            //    new ItemData(this.GameData, assetA, i2);
            //}



            //i1.AddAction(MyItemClick111);
            //i4.AddAction(MyItemClick444);
        }

        private void setActions()
        {
            List<ItemData> items = ItemData.GetItemsByAttribute(GameData.Items, "click");

            items.ForEach(x => x.AddAction(MyItemClick111));
        }

       

        public async Task MyItemClick111(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick111 " + data);

            data.Item.SetPosition(0, 1, 0);
        }

        public async Task MyItemClick444(ExecuteActionData data)
        {
            Console.WriteLine("TikTakToeGameFlow MyItemClick444 " + data);
            data.Item.SetPosition(0, 2, 0);
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
