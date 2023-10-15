namespace MG.Server.Entities
{
    public class GameData: BaseEntity<GameData>
    {
        public GameTypeEnum GameType { get; set; }

        public List<ItemData> Items { get; set; }
        public List<PlayerData> Players { get; set; }

        public GameStatusEnum GameStatus { get; set; }
        public string CreatorId { get; set; }
        public string CurrentTurnId { get; set; }
        public List<PlayerData> Winners { get; set; }

        public GameData():base()
        {
            
            Items = new List<ItemData>();
            Players = new List<PlayerData>();
        }


    }

    //public class GameTypeEnum
    //{
    //    public const int TIK_TAK_TOE = 1;
    //    public const int CHESS = 2;
    //    public const int REVERCY = 3;
    //    public const int DND = 4;

    //}

    public enum GameTypeEnum
    {
        TIK_TAK_TOE = 1,
        CHESS = 2,
        REVERCY = 3,
        DND = 4
    }
    public enum GameStatusEnum
    {
        CREATED = 1,
        SETUP = 2,
        PLAY = 3,
        ENDED = 4,

    }
}