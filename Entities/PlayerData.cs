using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class PlayerData : BaseData<GameData>
    {
        public string Type { get; set; }

        public UserData? User { get; set; }
             

        public PlayerData(GameData game):base()
        {
            Type = PlayerTypeEnum.EMPTY_SEAT;
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