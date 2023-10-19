using MG.Server.GameFlows;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class GameData: BaseData<GameData>
    {
        public string GameType { get; set; }

        public Dictionary<string,AssetData> Assets { get; set; }

        public List<ItemData> Items { get; set; }
        public List<PlayerData> Players { get; set; }

        public string GameStatus { get; set; }
        public string CreatorId { get; set; }
        public string CurrentTurnId { get; set; }
        public List<PlayerData> Winners { get; set; }

        [JsonIgnore] public BaseGameFlow GameFlow { get; set; }

        public GameData():base()
        {
            Assets = new Dictionary<string,AssetData>();
            Items = new List<ItemData>();
            Players = new List<PlayerData>();
        }


        public ItemData FindItem(string itemId)
        {
            ItemData found = null;

            Items.ForEach(item => {
                if (item.Id == itemId)
                {
                    found = item;
                    return;
                }
                var f = item.FindItem(itemId);
                if (f != null)
                {
                    found = item;
                    return;
                }

            });

            return found;
        }
        public PlayerData FindPlayer(string playerId)
        {
            //PlayerData found = null;

            return Players.Find(p => p.Id == playerId);

            //return found;
        }
        

    }


    public class GameTypeEnum
    {
         public const string TIK_TAK_TOE = "TIK_TAK_TOE";
        public const string CATAN = "CATAN";
        public const string DND = "DND";

    }
    public class GameStatusEnum
    {
        public const string CREATED = "CREATED";
        public const string SETUP = "SETUP";
        public const string PLAY = "PLAY";
        public const string ENDED = "ENDED";

    }
}