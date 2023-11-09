using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    public class DnDGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData MAP_1_0 = new TokenAssetData("dnd/map_1_0.png");
            internal static AssetData MAP_1_1 = new TokenAssetData("dnd/map_1_1.png");
            internal static AssetData SKELETON = new ObjectAssetData("dnd/skeleton.stl");
            internal static AssetData angel = new ObjectAssetData("dnd/angel.stl");

            internal static AssetData flytrap = new ObjectAssetData("dnd/flytrap.glb");
            internal static AssetData rover = new ObjectAssetData("dnd/rover.glb");

            internal static AssetData Card1 = new TokenAssetData("dnd/card1.png", "dnd/cardBack.png");
            internal static AssetData Card2 = new TokenAssetData("dnd/card2.png", "dnd/cardBack.png");
            internal static AssetData Card3 = new TokenAssetData("dnd/card3.png", "dnd/cardBack.png");
            internal static AssetData Card4 = new TokenAssetData("dnd/card4.png", "dnd/cardBack.png");
            internal static AssetData Card5 = new TokenAssetData("dnd/card5.png", "dnd/cardBack.png");
        }

        public DnDGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DND;
        }

        protected override async Task Create()
        {
            Console.WriteLine("DnDGameFlow Create ");

            addAsset(Assets.MAP_1_0);
            addAsset(Assets.MAP_1_1);
            addAsset(Assets.SKELETON);
            addAsset(Assets.angel);

            addAsset(Assets.flytrap);
            addAsset(Assets.rover);


            addAsset(Assets.Card1);
            addAsset(Assets.Card2);
            addAsset(Assets.Card3);
            addAsset(Assets.Card4);
            addAsset(Assets.Card5);

            GameData.Observer.Position.Set(0, 12, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "DungeonMaster")
                .SetCameraPosition(-6, 2, 3)
                .SetAvatarPosition(-6, 2, 3);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "Paladin")
                .SetCameraPosition(0, 2, -6)
                .SetAvatarPosition(0, 2, -6);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "Wizard")
                .SetCameraPosition(8, 2, 0)
                .SetAvatarPosition(8, 2, 0);



        }

        protected override async Task Setup()
        {
            Console.WriteLine("DnDGameFlow Setup ");

            //set the map
            addItem(Assets.MAP_1_0).SetPosition(0, 0, 0).SetScale(10);
            addItem(Assets.SKELETON).SetPosition(0, 0, 0);
            addItem(Assets.angel).SetPosition(1, 0, 1);

            addItem(Assets.rover).SetPosition(3, 0, -3);
            addItem(Assets.rover).SetPosition(4, 0, -3);
            addItem(Assets.flytrap).SetPosition(3, 0, 3);
            addItem(Assets.flytrap).SetPosition(4, 0, 3);


            // give players cards
            var p = getPlayerByAttribute("type", "DungeonMaster");
            addItemToPlayerTable(p, Assets.Card1);
            addItemToPlayerTable(p, Assets.Card2);
            addItemToPlayerHand(p, Assets.Card3);
            addItemToPlayerHand(p, Assets.Card4);

            p = getPlayerByAttribute("type", "Paladin");
            addItemToPlayerTable(p, Assets.Card5);

            p = getPlayerByAttribute("type", "Wizard");
            addItemToPlayerTable(p, Assets.Card4);

        }

        protected override async Task StartGame()
        {
            Console.WriteLine("DnDGameFlow StartGame " + this.GameData);

            //addItem("MAP_1_0")
            //    .SetPosition(0, 0, 0)
            //    .SetScale(1128/40, 1, 876/40).AddAction(MapClick); ;

        }

        public async Task MapClick(ExecuteActionData data)
        {
            Console.WriteLine(data.point);

            //addItem("X").SetPosition(data.point);

        }

        protected override async Task EndGame()
        {
            Console.WriteLine("DnDGameFlow EndGame " + this.GameData);

        }
        protected override async Task<bool> IsEndGame()
        {
            return false;
        }

        protected override List<PlayerData> GetGameWinners()
        {
            return new List<PlayerData>();
        }

    }
}
