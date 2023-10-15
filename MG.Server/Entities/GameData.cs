namespace MG.Server.Entities
{
    public class GameData
    {
        public string Id { get; set; }
        public GameTypeEnum GameType { get; set; }
        public string? Name { get; set; }

        public string? UserData { get; set; }

        public List<Item> Items { get; set; }
        public List<Player> Players { get; set; }

        public GameData()
        {
            Id = Guid.NewGuid().ToString();

            Items = new List<Item>();
            Players = new List<Player>();
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
}