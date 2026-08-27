using MG.Server.Entities;

namespace MG.Server.BL
{
    /// <summary>
    /// One row of the games list. Deliberately NOT a GameData: the list screen reads
    /// name / type / status / creator, each occupied seat's display name and seat type, and the
    /// result line once a game has ended. It never touches the item tree, the panels or the
    /// game attributes — and those are exactly where the boards and the hidden roles live.
    ///
    /// Property names match the GameData fields the client already reads, so the Angular side
    /// needs no change: missing fields simply come back undefined and are never referenced.
    /// (C# PascalCase serializes to camelCase — ASP.NET Core's Web defaults.)
    /// </summary>
    public class GameSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string GameType { get; set; }
        public string GameStatus { get; set; }
        public string CreatorId { get; set; }

        /// <summary>Only "result" — never the game's full attribute bag (roles, cards, board state).</summary>
        public Dictionary<string, string> Attributes { get; set; } = new();

        public List<SeatSummary> Players { get; set; } = new();

        public static GameSummary Of(GameData g)
        {
            var s = new GameSummary
            {
                Id = g.Id,
                Name = g.Name,
                GameType = g.GameType,
                GameStatus = g.GameStatus,
                CreatorId = g.CreatorId,
            };

            // The list shows a result line only for a finished game.
            if (g.GameStatus == GameStatusEnum.ENDED
                && g.Attributes != null
                && g.Attributes.TryGetValue("result", out var result))
            {
                s.Attributes["result"] = result;
            }

            foreach (var p in g.Players ?? new List<PlayerData>())
            {
                var seat = new SeatSummary
                {
                    Id = p.Id,
                    Type = p.Type,
                    User = p.User == null ? null : new UserSummary { Id = p.User.Id, Name = p.User.Name },
                };

                // Only the seat's colour/role label ("white", "black", ...) — the one key the
                // list renders. A seat's other attributes stay on the server.
                if (p.Attributes != null && p.Attributes.TryGetValue("type", out var seatType))
                    seat.Attributes["type"] = seatType;

                s.Players.Add(seat);
            }

            return s;
        }
    }

    public class SeatSummary
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public UserSummary? User { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    public class UserSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
