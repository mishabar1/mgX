using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class PlayerData : BaseData<GameData>
    {

        public string GameId { get { return Game.Id; } }
        [JsonIgnore] public GameData Game { get; set; }

        public PlayerTypeEnum Type { get; set; }

        public UserData? User { get; set; }
             

        public PlayerData(GameData game):base()
        {            
            Game = game;
            Game.Players.Add(this);
        }
    }

    //public class PlayerTypeEnum
    //{
    //    public const int HUMAN = 1;
    //    public const int AI = 2;
    //    public const int OBSERVER = 3;
    //}

    public enum PlayerTypeEnum
    {
        EMPTY_SEAT = 0,
        HUMAN = 1,
        AI = 2,
        OBSERVER = 3
    }
}