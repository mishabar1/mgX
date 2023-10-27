using MG.Server.BL;
using MG.Server.Services;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class PlayerData : BaseData<PlayerData>
    {
        public string Type { get; set; }

        public UserData? User { get; set; }

        public ItemData Table { get; set; }
        public ItemData Hand { get; set; }

        public LocationData Avatar { get; set; }
        public LocationData Camera { get; set; }

        [JsonIgnore] public AIAgent AIAgent { get; set; }

        public PlayerData(GameData game):base()
        {
            Type = PlayerTypeEnum.EMPTY_SEAT;
            Avatar = new LocationData();
            Camera = new LocationData();
            Table = new ItemData("", null) { Name = "PLAYER_TABLE" };
            Hand = new ItemData("", null) { Name = "PLAYER_HAND" };
            if (game != null)
            {
                game.Players.Add(this);
            }
        }

        internal PlayerData SetCameraPosition(int x, int y, int z)
        {
            this.Camera.Position.X = x;
            this.Camera.Position.Y = y;
            this.Camera.Position.Z = z;

            return this;
        }
    }

    

        public class PlayerTypeEnum
    {
        public const string OBSERVER = "OBSERVER";
        public const string EMPTY_SEAT = "EMPTY_SEAT";
        public const string HUMAN = "HUMAN";
        public const string AI = "AI";
        
    }

}