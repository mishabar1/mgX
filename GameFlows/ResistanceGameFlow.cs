using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // The Resistance — a hidden-role social-deduction game (base game, 5..10 players).
    //
    // Players are secretly RESISTANCE or SPY. Over up to 5 rounds, the current Leader proposes a
    // mission Team; everyone votes to approve/reject it (5 consecutive rejects in one round = spies
    // win). If approved, the team members secretly play Success/Fail cards (resistance MUST play
    // Success; spies may sabotage). 3 successful missions = resistance win; 3 failed = spies win.
    //
    // All state lives in GameData.Attributes; the visible scene is rebuilt each action (Durak-style).
    // The real UI is a per-player HTML overlay console on the client; the 3D scene is a minimal
    // title + mission/vote track for spectators.
    //
    // SECRECY (pragmatic, per product decision): every player's role is in Attributes, which is
    // broadcast to all clients. The client console only DISPLAYS a player their own role (and, if a
    // spy, the other spies). A determined cheater could read the wire — true secrecy would need a
    // per-player redacted broadcast (noted for a future pass), which would slot in at
    // DataRepository.HubGameUpdated.
    public class ResistanceGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("resistance");
        }

        private const int MAXSEATS = 10;
        public override int MinPlayers => 5;

        // Character-card art (themed): allies = resistance (up to 6), axis = spies (up to 4).
        private static readonly string[] ALLY =
            { "ally-1-en.jpg", "ally-2-en.jpg", "ally-3-en.jpg", "ally-4-en.jpg", "ally-5.jpg", "ally-6-en.jpg" };
        private static readonly string[] AXIS =
            { "axis-1-en.jpg", "axis-2-en.jpg", "axis-3-en.jpg", "axis-4.jpg" };

        public ResistanceGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.RESISTANCE;
        }

        // Read a UI action parameter (posted by the HTML console), falling back to a clicked item's
        // attribute. Mirrors the D&D console pattern.
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        // ============================ rules tables ============================
        // Spies by total player count (5..10): 2,2,3,3,3,4.
        private static int SpyCount(int n) => n switch { 5 => 2, 6 => 2, 7 => 3, 8 => 3, 9 => 3, 10 => 4, _ => Math.Max(1, n / 3) };

        // Team size per mission (1..5), indexed by player count. Rows from the rulebook table.
        private static readonly Dictionary<int, int[]> TEAM = new()
        {
            { 5,  new[] { 2, 3, 2, 3, 3 } },
            { 6,  new[] { 2, 3, 4, 3, 4 } },
            { 7,  new[] { 2, 3, 3, 4, 4 } },
            { 8,  new[] { 3, 4, 4, 5, 5 } },
            { 9,  new[] { 3, 4, 4, 5, 5 } },
            { 10, new[] { 3, 4, 4, 5, 5 } },
        };
        private static int TeamSize(int n, int mission)
        {
            var row = TEAM.TryGetValue(n, out var r) ? r : TEAM[Math.Clamp(n, 5, 10)];
            return row[Math.Clamp(mission, 1, 5) - 1];
        }
        // Mission 4 in games of 7+ needs TWO sabotages to fail.
        private static int FailsNeeded(int n, int mission) => (mission == 4 && n >= 7) ? 2 : 1;

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            GameData.Observer.Position.Set(0, 16, 16);

            // Ten empty seats around a ring (only occupied ones actually play; min 5).
            const int Ra = 9, Rc = 12;
            for (int i = 0; i < MAXSEATS; i++)
            {
                double deg = i * (360.0 / MAXSEATS);
                double t = deg * Math.PI / 180.0;
                int ax = (int)Math.Round(Ra * Math.Sin(t));
                int az = (int)Math.Round(-Ra * Math.Cos(t));
                int cx = (int)Math.Round(Rc * Math.Sin(t));
                int cz = (int)Math.Round(-Rc * Math.Cos(t));
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .SetCameraPosition(cx, 9, cz)
                    .SetAvatarPosition(ax, 2, az);
            }
            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var seats = Occupied();
            int n = seats.Count;

            // Secret roles: SpyCount(n) spies, the rest resistance; shuffled onto the seats.
            int spies = SpyCount(n);
            var roles = Enumerable.Repeat("spy", spies).Concat(Enumerable.Repeat("resistance", n - spies)).ToList();
            for (int i = roles.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (roles[i], roles[j]) = (roles[j], roles[i]); }

            int ai = 0, ri = 0;
            for (int i = 0; i < n; i++)
            {
                var id = seats[i];
                GameData.Attributes["role:" + id] = roles[i];
                GameData.Attributes["card:" + id] = roles[i] == "spy" ? AXIS[ai++ % AXIS.Length] : ALLY[ri++ % ALLY.Length];
            }

            GameData.Attributes["order"] = string.Join(",", seats);
            GameData.Attributes["mnum"] = "1";
            GameData.Attributes["voteTrack"] = "0";
            GameData.Attributes["results"] = "";
            GameData.Attributes["log"] = "";
            GameData.Attributes["leader"] = seats[rnd.Next(n)];
            GameData.Attributes["phase"] = "reveal";
            ClearRoundState();
            foreach (var id in seats) { GameData.Attributes.Remove("ack:" + id); GameData.Attributes.Remove("susp:" + id); }
            GameData.Attributes.Remove("over");
            GameData.Attributes.Remove("result");
            GameData.Attributes.Remove("winnerIds");

            Render();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));
        protected override void AfterUndo() => Render();

        protected override List<PlayerData> GetGameWinners()
        {
            var ids = GameData.Attributes.GetValueOrDefault("winnerIds", "");
            if (string.IsNullOrEmpty(ids)) return new List<PlayerData>();
            var set = ids.Split(',').ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        // ============================ player actions ============================
        [GameAction] public async Task Ready(ExecuteActionData d)       { DoAck(d.Player!.Id); await Task.CompletedTask; }
        [GameAction] public async Task ProposeTeam(ExecuteActionData d)  { DoPropose(d.Player!.Id, Arg(d, "team")); await Task.CompletedTask; }
        [GameAction] public async Task Vote(ExecuteActionData d)         { DoVote(d.Player!.Id, Arg(d, "vote") == "approve"); await Task.CompletedTask; }
        [GameAction] public async Task Mission(ExecuteActionData d)      { DoMission(d.Player!.Id, Arg(d, "card") == "fail"); await Task.CompletedTask; }

        // ============================ AI ============================
        public override bool IsAITurn(PlayerData player)
        {
            if (GameData.Attributes.ContainsKey("over")) return false;
            var id = player.Id;
            switch (Phase)
            {
                case "reveal":  return GameData.Attributes.GetValueOrDefault("ack:" + id) != "1";
                case "team":    return id == Leader && Team.Count == 0;
                case "vote":    return IsOccupied(id) && !GameData.Attributes.ContainsKey("vote:" + id);
                case "mission": return Team.Contains(id) && !GameData.Attributes.ContainsKey("mcard:" + id);
                default:        return false;
            }
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return false; }
            var id = player.Id;
            switch (Phase)
            {
                case "reveal":
                    DoAck(id);
                    break;
                case "team":
                    if (id != Leader || Team.Count > 0) { await Task.CompletedTask; return false; }
                    DoPropose(id, string.Join(",", AiPickTeam(id, rnd)));
                    break;
                case "vote":
                    if (!IsOccupied(id) || GameData.Attributes.ContainsKey("vote:" + id)) { await Task.CompletedTask; return false; }
                    DoVote(id, AiVoteApprove(id, rnd));
                    break;
                case "mission":
                    if (!Team.Contains(id) || GameData.Attributes.ContainsKey("mcard:" + id)) { await Task.CompletedTask; return false; }
                    DoMission(id, AiSabotage(id, rnd));
                    break;
                default:
                    await Task.CompletedTask; return false;
            }
            await Task.CompletedTask;
            return true;
        }

        // Leader picks a team of the required size, always including self. A SPY leader fills at
        // random (guaranteeing a spy on the mission). A RESISTANCE leader prefers the least-suspected
        // players (those not seen on a failed mission).
        private List<string> AiPickTeam(string leaderId, Random rnd)
        {
            int size = TeamSize(Occupied().Count, MissionNum);
            var chosen = new List<string> { leaderId };
            IEnumerable<string> pool = Occupied().Where(x => x != leaderId);
            pool = IsSpy(leaderId)
                ? pool.OrderBy(_ => rnd.Next())
                : pool.OrderBy(Susp).ThenBy(_ => rnd.Next());   // trust the clean players
            foreach (var x in pool) { if (chosen.Count >= size) break; chosen.Add(x); }
            return chosen;
        }

        private bool AiVoteApprove(string id, Random rnd)
        {
            bool isSpy = IsSpy(id);
            var team = Team;
            int vt = VoteTrack;
            if (isSpy)
            {
                bool spyOnTeam = team.Any(IsSpy);
                // Approve teams that carry a spy; otherwise reject (and definitely reject to trigger
                // the 5-reject spy win when we're one away).
                if (spyOnTeam) return true;
                if (vt >= 4) return false;
                return rnd.NextDouble() < 0.2;
            }
            // Resistance: never hand spies the 5-reject win; otherwise reject any team carrying a
            // player who was on a failed mission, trust your own proposal, else usually approve.
            if (vt >= 4) return true;
            if (team.Any(x => x != id && Susp(x) > 0)) return false;
            if (id == Leader) return true;
            return rnd.NextDouble() < 0.75;
        }

        private bool AiSabotage(string id, Random rnd)
        {
            if (!IsSpy(id)) return false;                 // resistance can never sabotage
            double p = MissionNum == 1 ? 0.5 : 0.8;       // vary behaviour a little across rounds
            return rnd.NextDouble() < p;
        }

        // ============================ state mutation / rules ============================
        private void DoAck(string id)
        {
            if (Phase != "reveal" || !IsOccupied(id)) return;
            GameData.Attributes["ack:" + id] = "1";
            if (Occupied().All(x => GameData.Attributes.GetValueOrDefault("ack:" + x) == "1"))
            {
                GameData.Attributes["phase"] = "team";
            }
            Render();
        }

        private void DoPropose(string leaderId, string teamCsv)
        {
            if (Phase != "team" || leaderId != Leader) return;
            var team = (teamCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Where(IsOccupied).Distinct().ToList();
            if (team.Count != TeamSize(Occupied().Count, MissionNum)) return;   // wrong size — ignore
            SaveUndoPoint();
            GameData.Attributes["team"] = string.Join(",", team);
            foreach (var id in Occupied()) GameData.Attributes.Remove("vote:" + id);
            GameData.Attributes["phase"] = "vote";
            Render();
        }

        private void DoVote(string id, bool approve)
        {
            if (Phase != "vote" || !IsOccupied(id)) return;
            if (GameData.Attributes.ContainsKey("vote:" + id)) return;      // one vote each
            GameData.Attributes["vote:" + id] = approve ? "approve" : "reject";
            ResolveVoteIfComplete();
            Render();
        }

        private void ResolveVoteIfComplete()
        {
            var occ = Occupied();
            if (!occ.All(x => GameData.Attributes.ContainsKey("vote:" + x))) return;   // still voting

            int approves = occ.Count(x => GameData.Attributes["vote:" + x] == "approve");
            int rejects = occ.Count - approves;
            bool approved = approves * 2 > occ.Count;                       // strict majority; tie = reject

            LogVote(approved, approves, rejects);

            if (approved)
            {
                GameData.Attributes["voteTrack"] = "0";
                GameData.Attributes["phase"] = "mission";
                foreach (var x in occ) GameData.Attributes.Remove("mcard:" + x);
            }
            else
            {
                int vt = VoteTrack + 1;
                GameData.Attributes["voteTrack"] = vt.ToString();
                if (vt >= 5)
                {
                    EndWith("spy", "The Spies win — 5 rejected teams!");
                    return;
                }
                GameData.Attributes["leader"] = NextLeader();
                GameData.Attributes["team"] = "";
                foreach (var x in occ) GameData.Attributes.Remove("vote:" + x);
                GameData.Attributes["phase"] = "team";
            }
        }

        private void DoMission(string id, bool fail)
        {
            if (Phase != "mission" || !Team.Contains(id)) return;
            if (GameData.Attributes.ContainsKey("mcard:" + id)) return;
            if (fail && !IsSpy(id)) fail = false;                          // resistance always supports
            GameData.Attributes["mcard:" + id] = fail ? "fail" : "success";
            ResolveMissionIfComplete();
            Render();
        }

        private void ResolveMissionIfComplete()
        {
            var team = Team;
            if (!team.All(x => GameData.Attributes.ContainsKey("mcard:" + x))) return;   // still playing

            int n = Occupied().Count, m = MissionNum;
            int fails = team.Count(x => GameData.Attributes["mcard:" + x] == "fail");
            bool success = fails < FailsNeeded(n, m);

            var results = Results;
            results.Add(success ? "S" : "F");
            GameData.Attributes["results"] = string.Join(",", results);
            // Public suspicion: everyone on a failed mission gets a mark (drives the resistance AI).
            if (!success)
                foreach (var x in team)
                    GameData.Attributes["susp:" + x] = (Susp(x) + 1).ToString();
            LogMission(m, success, fails);

            int s = results.Count(r => r == "S"), f = results.Count(r => r == "F");
            if (s >= 3) { EndWith("resistance", "The Resistance wins — 3 successful missions!"); return; }
            if (f >= 3) { EndWith("spy", "The Spies win — 3 sabotaged missions!"); return; }

            // Next round.
            GameData.Attributes["mnum"] = (m + 1).ToString();
            GameData.Attributes["voteTrack"] = "0";
            GameData.Attributes["leader"] = NextLeader();
            ClearRoundState();
            GameData.Attributes["phase"] = "team";
        }

        private void EndWith(string winnerRole, string result)
        {
            GameData.Attributes["over"] = "1";
            GameData.Attributes["phase"] = "over";
            GameData.Attributes["result"] = result;
            GameData.Attributes["winnerRole"] = winnerRole;
            var winners = Occupied().Where(id => Role(id) == winnerRole).ToList();
            GameData.Attributes["winnerIds"] = string.Join(",", winners);
            Render();
        }

        private void ClearRoundState()
        {
            GameData.Attributes["team"] = "";
            foreach (var id in Occupied())
            {
                GameData.Attributes.Remove("vote:" + id);
                GameData.Attributes.Remove("mcard:" + id);
            }
        }

        // ============================ logging (public info) ============================
        // Votes are public in The Resistance — record who voted how so everyone can reason from it.
        private void LogVote(bool approved, int approves, int rejects)
        {
            var occ = Occupied();
            string who = string.Join("  ", occ.Select(x =>
                Name(x) + (GameData.Attributes["vote:" + x] == "approve" ? " [approve]" : " [reject]")));
            string team = string.Join(", ", Team.Select(Name));
            AppendLog($"M{MissionNum}: {Name(Leader)} proposed [{team}] - {(approved ? "APPROVED" : "REJECTED")} {approves}-{rejects}");
            AppendLog("   " + who);
        }

        private void LogMission(int m, bool success, int fails)
            => AppendLog(success ? $"Mission {m}: SUCCESS" : $"Mission {m}: FAILED — {fails} sabotage" + (fails == 1 ? "" : "s"));

        private void AppendLog(string line)
        {
            var cur = GameData.Attributes.GetValueOrDefault("log", "");
            GameData.Attributes["log"] = string.IsNullOrEmpty(cur) ? line : cur + "\n" + line;
        }

        // ============================ helpers ============================
        private string Phase => GameData.Attributes.GetValueOrDefault("phase", "reveal");
        private string Leader => GameData.Attributes.GetValueOrDefault("leader", "");
        private int MissionNum => int.TryParse(GameData.Attributes.GetValueOrDefault("mnum", "1"), out var v) ? v : 1;
        private int VoteTrack => int.TryParse(GameData.Attributes.GetValueOrDefault("voteTrack", "0"), out var v) ? v : 0;
        private List<string> Team => (GameData.Attributes.GetValueOrDefault("team", "") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private List<string> Results => (GameData.Attributes.GetValueOrDefault("results", "") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        private string Role(string id) => GameData.Attributes.GetValueOrDefault("role:" + id, "resistance");
        private bool IsSpy(string id) => Role(id) == "spy";
        // How many failed missions this player has been seen on (public info, used by the AI).
        private int Susp(string id) => int.TryParse(GameData.Attributes.GetValueOrDefault("susp:" + id, "0"), out var v) ? v : 0;

        private List<string> Occupied()
        {
            // Stable seating order once the game has started.
            var stored = GameData.Attributes.GetValueOrDefault("order", "");
            if (!string.IsNullOrEmpty(stored))
                return stored.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Where(id => GameData.Players.Any(p => p.Id == id && p.Type != PlayerTypeEnum.EMPTY_SEAT))
                             .ToList();
            return GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
        }

        private bool IsOccupied(string id) => Occupied().Contains(id);

        private string NextLeader()
        {
            var occ = Occupied();
            if (occ.Count == 0) return "";
            int i = occ.IndexOf(Leader);
            return occ[(i < 0 ? 0 : i + 1) % occ.Count];
        }

        private string Name(string id)
        {
            var p = GameData.Players.Find(x => x.Id == id);
            return p != null ? PlayerDisplayName(p) : "?";
        }

        // ============================ rendering (minimal 3D) ============================
        // The real interface is the per-player HTML console. In the 3D scene we simply lay the
        // game's map/tableau flat on the table as a backdrop.
        private AssetData MapAsset() => addAsset(new TokenAssetData("resistance/map.png"));

        private void Render()
        {
            GameData.Table = ItemData.Table();
            const double W = 22.0;                 // map is 865×577 (≈3:2); keep that aspect
            addItem(MapAsset()).SetPosition(0, 0, 0).SetScale(W, 1, W * 577.0 / 865.0)
                .AddAttribute("board", "1");
        }
    }
}
