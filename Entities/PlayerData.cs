using MG.Server.BL;
using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class PlayerData : BaseData<GameData>
    {
        public string Type { get; set; }

        public UserData? User { get; set; }

        public V3 Position { get; set; }
        public V3 Rotation { get; set; }

        public ItemData Table { get; set; }
        public ItemData Hand { get; set; }

        [JsonIgnore] public AIAgent AIAgent { get; set; }

        public PlayerData(GameData game):base()
        {
            Type = PlayerTypeEnum.EMPTY_SEAT;
            Position = new V3();
            Rotation = new V3();
            Table = new ItemData("", null) { Name = "PLAYER_TABLE" };
            Hand = new ItemData("", null) { Name = "PLAYER_HAND" };

            game.Players.Add(this);
        }
    }

    public class PlayerTypeEnum
    {
        public const string EMPTY_SEAT = "EMPTY_SEAT";
        public const string HUMAN = "HUMAN";
        public const string AI = "AI";
        
    }

}