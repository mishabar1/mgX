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
            Console.WriteLine("DnDGameFlow Setup ");

            addAsset("MAP_1_0", new AssetData("dnd/map_1_0.png", "", AssetTypeEnum.TOKEN));
            addAsset("MAP_1_1", new AssetData("dnd/map_1_1.png", "", AssetTypeEnum.TOKEN));
            addAsset("MAP_1_1_v2", new AssetData("dnd/map_1_1_v2.png", "", AssetTypeEnum.TOKEN));
            addAsset("MAP_1_2", new AssetData("dnd/map_1_2.png", "", AssetTypeEnum.TOKEN));
            addAsset("MAP_1_3", new AssetData("dnd/map_1_3.png", "", AssetTypeEnum.TOKEN));

            addAsset("X", new AssetData("ticktacktoe/x.glb"));
            addAsset("O", new AssetData("ticktacktoe/o.glb"));

            GameData.Observer.Position.Set(0, 12, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
            .AddAttribute("type", "DungeonMaster")
            .SetCameraPosition(0, 20, 10);
        }
        public override async Task StartGame()
        {
            Console.WriteLine("DnDGameFlow StartGame " + this.GameData);

            addItem("MAP_1_0")
                .SetPosition(0, 0, 0)
                .SetScale(1128/40, 1, 876/40).AddAction(MapClick); ;

        }

        public async Task MapClick(ExecuteActionData data)
        {
            Console.WriteLine(data.point);

            addItem("X").SetPosition(data.point);

        }

        public override async Task EndGame()
        {
            Console.WriteLine("DnDGameFlow EndGame " + this.GameData);
            
        }
        public override async Task<bool> IsEndGame()
        {
            return false;
        }

        public override List<PlayerData> GetGameWinners()
        {
            return new List<PlayerData>();
        }

    }
}
