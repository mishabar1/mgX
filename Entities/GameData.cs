using MG.Server.GameFlows;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MG.Server.Entities
{
    public class GameData : BaseData<GameData>
    {
        public string GameType { get; set; }
        public string GameStatus { get; set; }
        public Dictionary<string, AssetData> Assets { get; set; }
        public ItemData Table { get; set; }
        public List<PlayerData> Players { get; set; }
        public string CreatorId { get; set; }
        public string CurrentTurnId { get; set; }
        public List<PlayerData> Winners { get; set; }

        // Minimum occupied seats (HUMAN or AI) required before the game can start. Set from the
        // game flow's MinPlayers at creation; the client uses it to gate the Start button.
        public int MinPlayers { get; set; }

        public LocationData Observer { get; set; }

        [JsonIgnore] public BaseGameFlow GameFlow { get; set; }

        public GameData() : base()
        {
            Assets = new Dictionary<string, AssetData>();
            Table = ItemData.Table();
            Players = new List<PlayerData>();
            Observer = new LocationData();
        }

        // Options for producing an independent snapshot. GameFlow/AIAgent are [JsonIgnore]
        // so they aren't copied (a history snapshot only needs state, not behaviour objects).
        private static readonly JsonSerializerOptions _copyOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 64
        };

        public GameData DeepCopy()
        {
            // Was `return this;` — every history entry aliased the live object, so the
            // recorded history all reflected the latest state. Serialize/deserialize to
            // get a real, independent snapshot.
            var serialized = JsonSerializer.Serialize(this, _copyOptions);
            return JsonSerializer.Deserialize<GameData>(serialized, _copyOptions)!;
        }


        public ItemData? FindItem(string itemId)
        {
            // Search the main table first, then each player's hand/table zones — so cards held
            // in a hand (e.g. Durak) are clickable, not just items on the shared table.
            var found = Table.FindItem(itemId);
            if (found != null) return found;
            foreach (var p in Players)
            {
                if (p.Hand != null) { found = p.Hand.FindItem(itemId); if (found != null) return found; }
                if (p.Table != null) { found = p.Table.FindItem(itemId); if (found != null) return found; }
            }
            return null;
        }
        public void RemoveItem(string itemId)
        {            


                Table.RemoveItem(itemId);


        }
        public PlayerData? FindPlayer(string playerId)
        {            

                return Players.Find(p => p.Id == playerId);

        }

        public List<ItemData> GetAllGameItems()
        {

                var list = new List<ItemData>();

                list.AddRange(GetAllItems(Table));
                foreach (var player in Players)
                {
                    list.AddRange(GetAllItems(player.Table));
                    list.AddRange(GetAllItems(player.Hand));
                }

                return list;
            

            
        }

        public List<ItemData> GetAllItems(ItemData item)
        {
            var list = new List<ItemData>();
            list.Add(item);
            foreach (var i in item.Items)
            {
                list.AddRange(GetAllItems(i));
            }
            return list;
        }



    }


    public class GameTypeEnum
    {
        public const string TIK_TAK_TOE = "TIK_TAK_TOE";
        public const string CHESS = "CHESS";
        public const string DND = "DND";
        public const string GOMOKU = "GOMOKU";
        public const string REVERSI = "REVERSI";
        public const string CHECKERS = "CHECKERS";
        public const string DURAK = "DURAK";
        public const string RESISTANCE = "RESISTANCE";
        public const string DEMO = "DEMO";
        public const string SPLENDOR = "SPLENDOR";
        public const string CARCASSONNE = "CARCASSONNE";
        public const string CATAN = "CATAN";
        public const string ONE_NIGHT_WEREWOLF = "ONE_NIGHT_WEREWOLF";
    }

    public class GameStatusEnum
    {
        public const string CREATED = "CREATED";
        public const string SETUP = "SETUP";
        public const string PLAY = "PLAY";
        public const string ENDED = "ENDED";

    }
}