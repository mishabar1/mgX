using MG.Server.GameFlows;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class GameData: BaseData<GameData>
    {
        public GameTypeEnum GameType { get; set; }

        public List<ItemData> Items { get; set; }
        public List<PlayerData> Players { get; set; }

        public GameStatusEnum GameStatus { get; set; }
        public string CreatorId { get; set; }
        public string CurrentTurnId { get; set; }
        public List<PlayerData> Winners { get; set; }

        [JsonIgnore] public BaseGameFlow GameFlow { get; set; }

        public GameData():base()
        {            
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

    public enum GameTypeEnum
    {
        TIK_TAK_TOE=1,
        CATAN = 2,
        DND = 3
    }
    public enum GameStatusEnum
    {
        CREATED = 1,
        SETUP = 2,
        PLAY = 3,
        ENDED = 4,

    }
}