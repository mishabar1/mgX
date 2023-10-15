using MG.Server.BL;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class Player
    {
        public string Id { get; set; }
        public string? Name { get; set; }

        public string GameId { get { return Game.Id; } }
        [JsonIgnore] public GameData Game { get; set; }

        public PlayerTypeEnum Type { get; set; }

        public User? User { get; set; }


        public string? UserData { get; set; }




        public Player(GameData game)
        {
            Id = Guid.NewGuid().ToString();
            Name = Utils.RandomName();
            Game = game;
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