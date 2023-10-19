using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class PlayerData : BaseData<GameData>
    {

        public string GameId { get { return Game.Id; } }
        [JsonIgnore] public GameData Game { get; set; }

        public string Type { get; set; }

        public UserData? User { get; set; }
             

        public PlayerData(GameData game):base()
        {            
            Game = game;
            Game.Players.Add(this);
        }
    }

    public class PlayerTypeEnum
    {
        public const string EMPTY_SEAT = "EMPTY_SEAT";
        public const string HUMAN = "HUMAN";
        public const string AI = "AI";
        public const string OBSERVER = "OBSERVER";
        
    }

}