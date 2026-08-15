using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // One Night Ultimate Werewolf — a single-round hidden-role social-deduction game (3..10 players).
    //
    // N players are dealt one card each from a deck of N+3; the 3 leftover cards go face-down to the
    // CENTER. One "night" happens: roles act in the official wake order (Werewolves → Minion →
    // Masons → Seer → Robber → Troublemaker → Drunk → Insomniac), some of them swapping cards
    // around. Then one day of discussion and ONE simultaneous vote; the player(s) with the most
    // votes die (2+ votes required). You are on the team of the card in front of you at the END of
    // the night — which may not be the one you were dealt.
    //
    // NIGHT MODEL (digital): instead of sequential wake phases — whose timing would leak who holds
    // which role — EVERY player privately submits one night decision simultaneously (roles with no
    // action just confirm "sleep"), and the server resolves the submissions in canonical wake order.
    // Without the Doppelgänger this is exactly equivalent to the physical game: every
    // information-gaining role (Werewolf/Minion/Mason/Seer/Robber) sees PRE-swap state, and the only
    // post-swap looker (Insomniac) is shown her card after resolution. The Doppelgänger is therefore
    // not in the deck (its mid-night role copy breaks the equivalence).
    //
    // All state lives in GameData.Attributes; the scene + per-seat panels are rebuilt every action
    // (Resistance-style). Card positions are keyed by player id, plus "c1"/"c2"/"c3" for the center:
    // "orig:<pos>" = the dealt card (drives who acts at night), "cur:<pos>" = the card now there
    // (drives teams/win conditions after the swaps).
    //
    // SECRECY (pragmatic, same product decision as Resistance): roles are in Attributes, which is
    // broadcast to every client; the panel only SHOWS a player their own information. True per-player
    // redaction would plug in at DataRepository.HubGameUpdated.
    public class OneNightWerewolfGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("onw");
        }

        private const int MAXSEATS = 10;
        public override int MinPlayers => 3;

        // ============================ roles ============================
        private const string WEREWOLF = "werewolf";
        private const string MINION = "minion";
        private const string MASON = "mason";
        private const string SEER = "seer";
        private const string ROBBER = "robber";
        private const string TROUBLEMAKER = "troublemaker";
        private const string DRUNK = "drunk";
        private const string INSOMNIAC = "insomniac";
        private const string HUNTER = "hunter";
        private const string TANNER = "tanner";
        private const string VILLAGER = "villager";

        private static readonly HashSet<string> VILLAGE_TEAM = new()
            { VILLAGER, SEER, ROBBER, TROUBLEMAKER, DRUNK, INSOMNIAC, MASON, HUNTER };

        private static string RoleName(string role) => role.Length == 0 ? "?"
            : char.ToUpper(role[0]) + role.Substring(1);

        // Deck = players + 3 center cards, growing with the seat count (all box roles except the
        // Doppelgänger — see the header note). Masons only ever enter as a pair.
        private static List<string> BuildDeck(int n)
        {
            var deck = new List<string> { WEREWOLF, WEREWOLF, SEER, ROBBER, TROUBLEMAKER, VILLAGER };
            if (n >= 4) deck.Add(INSOMNIAC);
            if (n >= 5) deck.Add(VILLAGER);
            if (n >= 6) deck.Add(DRUNK);
            if (n >= 7) deck.Add(MINION);
            if (n >= 8) deck.Add(HUNTER);
            if (n >= 9) deck.Add(TANNER);
            if (n >= 10) { deck.Remove(VILLAGER); deck.Add(MASON); deck.Add(MASON); }
            return deck;   // always n + 3
        }

        public OneNightWerewolfGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.ONE_NIGHT_WEREWOLF;
        }

        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            GameData.Observer.Position.Set(0, 16, 16);

            // Ten empty seats around a ring (only occupied ones play; min 3).
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
            var seats = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT)
                                        .Select(p => p.Id).ToList();
            int n = seats.Count;

            // Restart hygiene: clear every per-game key (a fresh Start after a finished game).
            var prefixes = new[] { "orig:", "cur:", "nact:", "ninfo:", "ack:", "ready:", "vote:" };
            foreach (var k in GameData.Attributes.Keys
                         .Where(k => prefixes.Any(p => k.StartsWith(p))).ToList())
                GameData.Attributes.Remove(k);
            foreach (var k in new[] { "over", "result", "winnerIds", "deaths", "log" })
                GameData.Attributes.Remove(k);

            GameData.Attributes["order"] = string.Join(",", seats);

            // Deal: shuffle N+3 cards; one per player, the rest to the center.
            var deck = BuildDeck(n);
            for (int i = deck.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); }

            var positions = seats.Concat(new[] { "c1", "c2", "c3" }).ToList();
            for (int i = 0; i < positions.Count; i++)
            {
                GameData.Attributes["orig:" + positions[i]] = deck[i];
                GameData.Attributes["cur:" + positions[i]] = deck[i];
            }

            // Passive night knowledge (what you'd learn just by opening your eyes at your wake call).
            var wolves = seats.Where(x => Orig(x) == WEREWOLF).ToList();
            var masons = seats.Where(x => Orig(x) == MASON).ToList();
            foreach (var id in seats)
            {
                switch (Orig(id))
                {
                    case WEREWOLF:
                        AddInfo(id, wolves.Count >= 2
                            ? "Your fellow Werewolf: " + string.Join(", ", wolves.Where(x => x != id).Select(Name))
                            : "You are the ONLY Werewolf — you may peek at one center card.");
                        break;
                    case MINION:
                        AddInfo(id, wolves.Count > 0
                            ? "The Werewolves are: " + string.Join(", ", wolves.Select(Name))
                            : "There are NO Werewolves among the players — make sure someone else dies!");
                        break;
                    case MASON:
                        AddInfo(id, masons.Count >= 2
                            ? "The other Mason: " + string.Join(", ", masons.Where(x => x != id).Select(Name))
                            : "You are the only Mason — the other Mason card is in the center.");
                        break;
                }
            }

            GameData.Attributes["phase"] = "reveal";
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
        [GameAction] public async Task Ready(ExecuteActionData d)    { DoAck(d.Player!.Id); await Task.CompletedTask; }
        [GameAction] public async Task Night(ExecuteActionData d)    { DoNight(d.Player!.Id, Arg(d, "tm") != "" ? "tm:" + Arg(d, "tm") : Arg(d, "act")); await Task.CompletedTask; }
        [GameAction] public async Task DayReady(ExecuteActionData d) { DoDayReady(d.Player!.Id); await Task.CompletedTask; }
        [GameAction] public async Task Vote(ExecuteActionData d)     { DoVote(d.Player!.Id, Arg(d, "target")); await Task.CompletedTask; }
        // Manual re-check for a stuck phase (see TryAdvancePhase) — shown to waiting players.
        [GameAction] public async Task Poke(ExecuteActionData d)
        {
            if (Playing && IsOccupied(d.Player!.Id)) { TryAdvancePhase(); Render(); }
            await Task.CompletedTask;
        }

        // ============================ AI ============================
        public override bool IsAITurn(PlayerData player)
        {
            if (GameData.Attributes.ContainsKey("over")) return false;
            var id = player.Id;
            if (!IsOccupied(id)) return false;
            switch (Phase)
            {
                case "reveal": return GameData.Attributes.GetValueOrDefault("ack:" + id) != "1";
                case "night":  return !GameData.Attributes.ContainsKey("nact:" + id);
                case "day":    return GameData.Attributes.GetValueOrDefault("ready:" + id) != "1";
                case "vote":   return !GameData.Attributes.ContainsKey("vote:" + id);
                default:       return false;
            }
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            await Task.CompletedTask;
            if (GameData.Attributes.ContainsKey("over")) return false;
            var id = player.Id;
            if (!IsOccupied(id)) return false;
            switch (Phase)
            {
                case "reveal":
                    if (GameData.Attributes.GetValueOrDefault("ack:" + id) == "1") return false;
                    DoAck(id);
                    return true;
                case "night":
                    if (GameData.Attributes.ContainsKey("nact:" + id)) return false;
                    DoNight(id, AiPickNightAction(id, rnd));
                    return true;
                case "day":
                    if (GameData.Attributes.GetValueOrDefault("ready:" + id) == "1") return false;
                    DoDayReady(id);
                    return true;
                case "vote":
                    if (GameData.Attributes.ContainsKey("vote:" + id)) return false;
                    DoVote(id, AiPickVote(id, rnd));
                    return true;
                default:
                    return false;
            }
        }

        private string AiPickNightAction(string id, Random rnd)
        {
            var others = Occupied().Where(x => x != id).ToList();
            string Pick() => others[rnd.Next(others.Count)];
            switch (Orig(id))
            {
                case WEREWOLF:
                    return Occupied().Count(x => Orig(x) == WEREWOLF) == 1 ? "peek:c" + (rnd.Next(3) + 1) : "sleep";
                case SEER:
                    if (rnd.NextDouble() < 0.7) return "seerp:" + Pick();
                    var pairs = new[] { "c1,c2", "c1,c3", "c2,c3" };
                    return "seerc:" + pairs[rnd.Next(3)];
                case ROBBER: return "rob:" + Pick();
                case TROUBLEMAKER:
                    if (others.Count < 2) return "sleep";
                    var a = Pick(); string b; do { b = Pick(); } while (b == a);
                    return "tm:" + a + "," + b;
                case DRUNK: return "drunk:c" + (rnd.Next(3) + 1);
                default: return "sleep";
            }
        }

        // Vote with only the knowledge this seat could FAIRLY have (its own deal + its night info).
        private string AiPickVote(string id, Random rnd)
        {
            var others = Occupied().Where(x => x != id).ToList();
            string orig = Orig(id);
            string act = GameData.Attributes.GetValueOrDefault("nact:" + id, "");

            bool wolfTeam = false;
            var knownWolves = new List<string>();

            if (orig == WEREWOLF) { wolfTeam = true; knownWolves = others.Where(x => Orig(x) == WEREWOLF).ToList(); }
            else if (orig == MINION) { wolfTeam = true; knownWolves = others.Where(x => Orig(x) == WEREWOLF).ToList(); }
            else if (orig == ROBBER && act.StartsWith("rob:") && Orig(act.Substring(4)) == WEREWOLF)
                wolfTeam = true;   // stole a Werewolf card — now on the wolf team
            else if (orig == INSOMNIAC && Cur(id) == WEREWOLF)
                wolfTeam = true;   // woke up holding a Werewolf card

            if (wolfTeam)
            {
                var innocents = others.Where(x => !knownWolves.Contains(x)).ToList();
                return innocents.Count > 0 ? innocents[rnd.Next(innocents.Count)] : others[rnd.Next(others.Count)];
            }

            // Seer who saw a Werewolf card on a player: vote them.
            if (orig == SEER && act.StartsWith("seerp:"))
            {
                var target = act.Substring(6);
                if (Orig(target) == WEREWOLF && others.Contains(target)) return target;
            }
            return others[rnd.Next(others.Count)];
        }

        // ============================ state mutation / rules ============================

        // The state machine only means something once StartGame has dealt (and stops once the
        // game ends). Without this gate, clients could drive reveal→…→over on a game that was
        // never started ("phase" absent defaults to "reveal", orig=="" passes "sleep").
        private bool Playing => GameData.GameStatus == GameStatusEnum.PLAY
                                && GameData.Attributes.ContainsKey("order");

        // A phase completes when every occupied seat has submitted — but a seat can VACATE
        // (JoinGame can flip it to EMPTY_SEAT mid-game) after everyone else already submitted,
        // and no further submission would ever re-run the inline checks. So completion lives
        // here, every mutation re-runs it, and waiting players get a "check again" button.
        private void TryAdvancePhase()
        {
            var occ = Occupied();
            if (occ.Count == 0) return;
            switch (Phase)
            {
                case "reveal":
                    if (occ.All(x => GameData.Attributes.GetValueOrDefault("ack:" + x) == "1"))
                        GameData.Attributes["phase"] = "night";
                    break;
                case "night":
                    if (occ.All(x => GameData.Attributes.ContainsKey("nact:" + x)))
                        ResolveNight();
                    break;
                case "day":
                    if (occ.All(x => GameData.Attributes.GetValueOrDefault("ready:" + x) == "1"))
                        GameData.Attributes["phase"] = "vote";
                    break;
                case "vote":
                    if (occ.All(x => GameData.Attributes.ContainsKey("vote:" + x)))
                        ResolveVotes();
                    break;
            }
        }

        private void DoAck(string id)
        {
            if (!Playing || Phase != "reveal" || !IsOccupied(id)) return;
            GameData.Attributes["ack:" + id] = "1";
            TryAdvancePhase();
            Render();
        }

        // One night decision per player, validated against the DEALT role, resolved when all are in.
        private void DoNight(string id, string act)
        {
            if (!Playing || Phase != "night" || !IsOccupied(id)) return;
            if (GameData.Attributes.ContainsKey("nact:" + id)) return;   // one action each
            if (string.IsNullOrEmpty(act)) return;

            string orig = Orig(id);
            var occ = Occupied();
            bool ok = false;

            if (act == "sleep")
            {
                ok = orig != DRUNK;   // the Drunk MUST exchange with a center card
            }
            else if (act.StartsWith("peek:c"))
            {
                // Lone werewolf only: peek one center card.
                ok = orig == WEREWOLF && occ.Count(x => Orig(x) == WEREWOLF) == 1 && IsCenter(act.Substring(5));
                if (ok) AddInfo(id, $"Center card {act.Substring(6)}: {RoleName(Cur(act.Substring(5)))}");
            }
            else if (act.StartsWith("seerp:"))
            {
                var target = act.Substring(6);
                ok = orig == SEER && target != id && occ.Contains(target);
                if (ok) AddInfo(id, $"{Name(target)}'s card: {RoleName(Cur(target))}");
            }
            else if (act.StartsWith("seerc:"))
            {
                var cs = act.Substring(6).Split(',');
                ok = orig == SEER && cs.Length == 2 && cs[0] != cs[1] && cs.All(IsCenter);
                if (ok) AddInfo(id, $"Center cards {cs[0].Substring(1)} & {cs[1].Substring(1)}: " +
                                    $"{RoleName(Cur(cs[0]))}, {RoleName(Cur(cs[1]))}");
            }
            else if (act.StartsWith("rob:"))
            {
                var target = act.Substring(4);
                ok = orig == ROBBER && target != id && occ.Contains(target);
                // The Robber takes the target's (pre-swap) card and looks at it. Recorded now; the
                // actual swap is applied in wake order at resolution.
                if (ok) AddInfo(id, $"You robbed {Name(target)} — your new card: {RoleName(Cur(target))}");
            }
            else if (act.StartsWith("tm:"))
            {
                var ps = act.Substring(3).Split(',', StringSplitOptions.RemoveEmptyEntries);
                ok = orig == TROUBLEMAKER && ps.Length == 2 && ps[0] != ps[1]
                     && ps.All(x => x != id && occ.Contains(x));
                if (ok)
                {
                    act = "tm:" + ps[0] + "," + ps[1];   // store the CANONICAL form of what was
                                                         // validated (raw client csv may carry
                                                         // empty segments that break resolution)
                    AddInfo(id, $"You swapped {Name(ps[0])} ↔ {Name(ps[1])} (unseen).");
                }
            }
            else if (act.StartsWith("drunk:c"))
            {
                ok = orig == DRUNK && IsCenter(act.Substring(6));
                if (ok) AddInfo(id, $"You exchanged your card with center card {act.Substring(7)} (unseen).");
            }

            if (!ok) return;
            GameData.Attributes["nact:" + id] = act;
            TryAdvancePhase();
            Render();
        }

        // Apply the submitted swaps in the official wake order: Robber → Troublemaker → Drunk,
        // then show the Insomniac her (possibly new) card.
        private void ResolveNight()
        {
            var occ = Occupied();

            var robber = occ.FirstOrDefault(x => Orig(x) == ROBBER);
            if (robber != null)
            {
                var act = GameData.Attributes["nact:" + robber];
                if (act.StartsWith("rob:")) SwapCards(robber, act.Substring(4));
            }

            var tm = occ.FirstOrDefault(x => Orig(x) == TROUBLEMAKER);
            if (tm != null)
            {
                var act = GameData.Attributes["nact:" + tm];
                if (act.StartsWith("tm:"))
                {
                    var ps = act.Substring(3).Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (ps.Length == 2) SwapCards(ps[0], ps[1]);
                }
            }

            var drunk = occ.FirstOrDefault(x => Orig(x) == DRUNK);
            if (drunk != null)
            {
                var act = GameData.Attributes["nact:" + drunk];
                if (act.StartsWith("drunk:")) SwapCards(drunk, act.Substring(6));
            }

            var insomniac = occ.FirstOrDefault(x => Orig(x) == INSOMNIAC);
            if (insomniac != null)
                AddInfo(insomniac, $"You woke at dawn — your card is: {RoleName(Cur(insomniac))}");

            GameData.Attributes["phase"] = "day";
        }

        private void DoDayReady(string id)
        {
            if (!Playing || Phase != "day" || !IsOccupied(id)) return;
            GameData.Attributes["ready:" + id] = "1";
            TryAdvancePhase();
            Render();
        }

        private void DoVote(string id, string target)
        {
            if (!Playing || Phase != "vote" || !IsOccupied(id)) return;
            if (GameData.Attributes.ContainsKey("vote:" + id)) return;      // one vote each
            if (target == id || !IsOccupied(target)) return;                // must point at someone else
            GameData.Attributes["vote:" + id] = target;
            TryAdvancePhase();
            Render();
        }

        private void ResolveVotes()
        {
            var occ = Occupied();
            var counts = occ.ToDictionary(x => x, _ => 0);
            foreach (var v in occ)
            {
                // A vote's target can have vacated its seat between casting and resolution
                // (JoinGame can flip a seat back to EMPTY_SEAT mid-game) — such votes just lapse.
                var t = GameData.Attributes["vote:" + v];
                if (counts.ContainsKey(t)) counts[t]++;
            }

            // Most votes die (ties all die) — but somebody must have at least 2 votes, otherwise
            // no one dies.
            int max = counts.Values.Max();
            var deaths = new HashSet<string>();
            if (max >= 2)
                foreach (var kv in counts)
                    if (kv.Value == max) deaths.Add(kv.Key);

            // Hunter: if the Hunter dies, whoever the Hunter voted for dies too (loop in case a
            // chain ever becomes possible).
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var d in deaths.ToList())
                    if (Cur(d) == HUNTER)
                    {
                        var t = GameData.Attributes.GetValueOrDefault("vote:" + d, "");
                        if (!string.IsNullOrEmpty(t) && deaths.Add(t)) changed = true;
                    }
            }

            // Public record: every vote, then who died.
            foreach (var v in occ)
                AppendLog($"{Name(v)} voted for {Name(GameData.Attributes["vote:" + v])}");
            AppendLog(deaths.Count == 0
                ? "No player received more than one vote — nobody died."
                : "Died: " + string.Join(", ", deaths.Select(x => $"{Name(x)} (the {RoleName(Cur(x))})")));
            GameData.Attributes["deaths"] = string.Join(",", deaths);

            // ---- win conditions (teams = the card in front of you NOW) ----
            var wwPlayers = occ.Where(x => Cur(x) == WEREWOLF).ToList();
            bool wwDied = deaths.Any(x => Cur(x) == WEREWOLF);
            bool tannerDied = deaths.Any(x => Cur(x) == TANNER);

            bool villageWins = wwDied || (wwPlayers.Count == 0 && deaths.Count == 0);
            bool wwTeamWins = wwPlayers.Count > 0 && !wwDied && !tannerDied;
            bool minionSoloWins = wwPlayers.Count == 0 && occ.Any(x => Cur(x) == MINION)
                                  && deaths.Any(x => Cur(x) != MINION);

            var winners = new HashSet<string>();
            if (villageWins) winners.UnionWith(occ.Where(x => VILLAGE_TEAM.Contains(Cur(x))));
            if (wwTeamWins) winners.UnionWith(occ.Where(x => Cur(x) == WEREWOLF || Cur(x) == MINION));
            if (minionSoloWins) winners.UnionWith(occ.Where(x => Cur(x) == MINION));
            winners.UnionWith(deaths.Where(x => Cur(x) == TANNER));   // the Tanner wins by dying

            string result;
            if (tannerDied)
                result = "The Tanner wins — he tricked you into killing him!"
                         + (wwDied ? " The Village wins too — a Werewolf died." : " The Werewolves lose.")
                         + (minionSoloWins ? " The Minion also wins — someone other than him died." : "");
            else if (villageWins && wwDied)
                result = "The Village wins — " + string.Join(", ",
                             deaths.Where(x => Cur(x) == WEREWOLF).Select(Name)) + " was a Werewolf!";
            else if (villageWins)
                result = "The Village wins — no Werewolves among you, and nobody died.";
            else if (wwTeamWins)
                result = deaths.Count == 0 ? "The Werewolves win — nobody died!"
                                           : "The Werewolves win — an innocent died.";
            else if (minionSoloWins)
                result = "The Minion wins — an innocent died, and the Werewolves were all in the center.";
            else
                result = "Nobody wins — an innocent died, and there were no Werewolves among you.";

            GameData.Attributes["over"] = "1";
            GameData.Attributes["phase"] = "over";
            GameData.Attributes["result"] = result;
            GameData.Attributes["winnerIds"] = string.Join(",", winners);
        }

        private void SwapCards(string a, string b)
        {
            var ka = "cur:" + a; var kb = "cur:" + b;
            (GameData.Attributes[ka], GameData.Attributes[kb]) = (GameData.Attributes[kb], GameData.Attributes[ka]);
        }

        // ============================ helpers ============================
        private string Phase => GameData.Attributes.GetValueOrDefault("phase", "reveal");
        private string Orig(string pos) => GameData.Attributes.GetValueOrDefault("orig:" + pos, "");
        private string Cur(string pos) => GameData.Attributes.GetValueOrDefault("cur:" + pos, "");
        private static bool IsCenter(string pos) => pos == "c1" || pos == "c2" || pos == "c3";

        private void AddInfo(string id, string line)
        {
            var cur = GameData.Attributes.GetValueOrDefault("ninfo:" + id, "");
            GameData.Attributes["ninfo:" + id] = string.IsNullOrEmpty(cur) ? line : cur + "\n" + line;
        }

        private void AppendLog(string line)
        {
            var cur = GameData.Attributes.GetValueOrDefault("log", "");
            GameData.Attributes["log"] = string.IsNullOrEmpty(cur) ? line : cur + "\n" + line;
        }

        private List<string> Occupied()
        {
            var stored = GameData.Attributes.GetValueOrDefault("order", "");
            if (!string.IsNullOrEmpty(stored))
                return stored.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Where(id => GameData.Players.Any(p => p.Id == id && p.Type != PlayerTypeEnum.EMPTY_SEAT))
                             .ToList();
            return GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
        }

        private bool IsOccupied(string id) => Occupied().Contains(id);

        private string Name(string id)
        {
            var p = GameData.Players.Find(x => x.Id == id);
            return p != null ? PlayerDisplayName(p) : "?";
        }

        // ============================ rendering (3D — for spectators) ============================
        // Face-down cards in front of every seat + 3 in the center; everything flips face-up at the
        // end. During play the "front" of every table token IS the card back (Durak's hidden-card
        // trick), so the 3D scene itself never leaks a role.
        private static string A(string file) => "one_night_werewolf/" + file;

        private AssetData BackAsset() => addAsset(new TokenAssetData(A("back.png"), A("back.png")));
        private AssetData RoleAsset(string role) => addAsset(new TokenAssetData(A(role + ".jpg"), A("back.png")));

        private void Render()
        {
            GameData.Table = ItemData.Table();
            bool over = GameData.Attributes.ContainsKey("over");
            var deaths = (GameData.Attributes.GetValueOrDefault("deaths", "") ?? "")
                         .Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            GameData.Attributes["hud"] = Phase switch
            {
                "reveal" => "Everyone: look at your role…",
                "night"  => "Night has fallen on the village…",
                "day"    => "Dawn — discuss!",
                "vote"   => "The vote is on…",
                _        => GameData.Attributes.GetValueOrDefault("result", "Game over"),
            };

            const double CardR = 5.5, LabelR = 7.2;
            foreach (var id in Occupied())
            {
                var seat = GameData.Players.Find(p => p.Id == id);
                if (seat == null) continue;
                // Seat index from the "type"="p<i+1>" attribute set at Create.
                int idx = int.TryParse((seat.GetStringAttribute("type") ?? "p1").Substring(1), out var v) ? v - 1 : 0;
                double deg = idx * (360.0 / MAXSEATS);
                double t = deg * Math.PI / 180.0;
                double x = CardR * Math.Sin(t), z = -CardR * Math.Cos(t);

                bool dead = over && deaths.Contains(id);
                addItem(over ? RoleAsset(Cur(id)) : BackAsset())
                    .SetPosition(x, 0.05, z)
                    .SetRotation(0, -deg + (dead ? 90 : 0), 0)   // a dead player's card lies sideways
                    .SetScale(2.3, 1, 3.15);

                addTextItem(Assets.TEXT).SetText(Name(id))
                    .SetPosition(LabelR * Math.Sin(t), 0.05, -LabelR * Math.Cos(t))
                    .SetRotation(-90, 0, 180 - deg)
                    .SetScale(0.45)
                    .AddAttribute("textColor", "ffffff");
            }

            for (int i = 1; i <= 3; i++)
                addItem(over ? RoleAsset(Cur("c" + i)) : BackAsset())
                    .SetPosition((i - 2) * 3.0, 0.05, 0)
                    .SetScale(2.3, 1, 3.15);

            BuildScreens();
        }

        // =====================================================================================
        // SERVER-DRIVEN PANEL — the entire per-seat UI as UiNode trees (the client just draws it).
        // =====================================================================================
        private void BuildScreens()
        {
            GameData.Attributes["panelMode"] = "full";
            foreach (var seat in GameData.Players)
                seat.Screen = seat.Type == PlayerTypeEnum.EMPTY_SEAT ? null : BuildSeatScreen(seat.Id);
        }

        private List<UiNode> BuildSeatScreen(string id)
        {
            var s = new List<UiNode> { UiNode.Title("ONE NIGHT WEREWOLF") };
            var occ = Occupied();
            int n = occ.Count;
            string phase = Phase;

            switch (phase)
            {
                // ---------- reveal: see your dealt card ----------
                case "reveal":
                {
                    s.Add(UiNode.Text_("Night is coming — memorise your role", "d9b98a"));
                    int acks = occ.Count(x => GameData.Attributes.GetValueOrDefault("ack:" + x) == "1");
                    if (GameData.Attributes.GetValueOrDefault("ack:" + id) == "1")
                    {
                        s.Add(UiNode.Text_("✓ Role memorised", "5fd08a", 19, "big"));
                        s.Add(UiNode.Note($"Waiting for the others… ({acks}/{n})"));
                        s.Add(PokeButton());
                    }
                    else
                    {
                        string role = Orig(id);
                        s.Add(UiNode.Row(
                            UiNode.Image(A(role + ".jpg"), 170),
                            UiNode.Col(
                                UiNode.Text_("You are the " + RoleName(role), TeamColor(role), 20, "big"),
                                UiNode.Text_(RoleBlurb(role), "cbb493"),
                                UiNode.Note("Careful: your card can be swapped away during the night. " +
                                            "Your team is whatever lies in front of you at dawn."))));
                        s.Add(UiNode.Button("I know my role", nameof(Ready), null, null, "ok big"));
                        s.Add(UiNode.Note($"{acks}/{n} ready."));
                    }
                    s.Add(RolesInPlayNode());
                    return s;
                }

                // ---------- night: one private decision each ----------
                case "night":
                {
                    s.Add(UiNode.Text_("🌙 Night — everyone acts in secret", "8fa8d9"));
                    int done = occ.Count(x => GameData.Attributes.ContainsKey("nact:" + x));
                    AddInfoNodes(s, id);
                    if (GameData.Attributes.ContainsKey("nact:" + id))
                    {
                        s.Add(UiNode.Note($"Your night is over. Waiting for dawn… ({done}/{n})"));
                        s.Add(PokeButton());
                    }
                    else
                    {
                        AddNightChoices(s, id, occ);
                        s.Add(UiNode.Note($"{done}/{n} are asleep already."));
                    }
                    return s;
                }

                // ---------- day: discussion ----------
                case "day":
                {
                    s.Add(UiNode.Text_("☀ Day — find the Werewolf", "ffe0a8"));
                    s.Add(UiNode.Text_("Discuss! Claim, bluff, accuse. When the village is ready, everyone votes " +
                                       "at once; the player with the most votes dies (at least 2 votes needed).", "cbb493"));
                    AddInfoNodes(s, id);
                    int ready = occ.Count(x => GameData.Attributes.GetValueOrDefault("ready:" + x) == "1");
                    if (GameData.Attributes.GetValueOrDefault("ready:" + id) == "1")
                    {
                        s.Add(UiNode.Note($"Waiting for the rest of the village… ({ready}/{n} ready)"));
                        s.Add(PokeButton());
                    }
                    else
                    {
                        s.Add(UiNode.Button("I'm ready to vote", nameof(DayReady), null, null, "ok big"));
                        s.Add(UiNode.Note($"{ready}/{n} ready to vote."));
                    }
                    s.Add(RolesInPlayNode());
                    return s;
                }

                // ---------- vote ----------
                case "vote":
                {
                    s.Add(UiNode.Text_("⚖ The vote — who dies?", "ff6b6b"));
                    AddInfoNodes(s, id);
                    int voted = occ.Count(x => GameData.Attributes.ContainsKey("vote:" + x));
                    if (GameData.Attributes.ContainsKey("vote:" + id))
                    {
                        s.Add(UiNode.Note($"You voted for {Name(GameData.Attributes["vote:" + id])}. " +
                                          $"Waiting… ({voted}/{n} voted)"));
                        s.Add(PokeButton());
                    }
                    else
                    {
                        s.Add(UiNode.Text_("Point at a player:"));
                        AddPlayerButtons(s, occ.Where(x => x != id),
                            x => UiNode.Button(Name(x), nameof(Vote), new() { { "target", x } }, null, "no"));
                        s.Add(UiNode.Note($"{voted}/{n} have voted. Votes are simultaneous and public afterwards."));
                    }
                    return s;
                }

                // ---------- over: full reveal ----------
                default:
                {
                    var winnerIds = (GameData.Attributes.GetValueOrDefault("winnerIds", "") ?? "")
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    var deaths = (GameData.Attributes.GetValueOrDefault("deaths", "") ?? "")
                                 .Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    s.Add(UiNode.Banner(GameData.Attributes.GetValueOrDefault("result", "Game over"),
                                        winnerIds.Contains(id) ? "win" : "lose"));
                    s.Add(UiNode.Text_(winnerIds.Contains(id) ? "🏆 You win!" : "You lose.",
                                       winnerIds.Contains(id) ? "5fd08a" : "ff6b6b", 20, "big"));
                    s.Add(UiNode.Text_("The truth of the night:", "d9b98a"));
                    foreach (var x in occ)
                    {
                        string mark = (deaths.Contains(x) ? " 💀" : "") + (winnerIds.Contains(x) ? " 🏆" : "");
                        string change = Orig(x) == Cur(x)
                            ? "stayed the " + RoleName(Cur(x))
                            : $"was dealt the {RoleName(Orig(x))} — ended as the {RoleName(Cur(x))}";
                        s.Add(UiNode.Row(
                            UiNode.Image(A(Cur(x) + ".jpg"), 64),
                            UiNode.Text_($"{Name(x)}{mark} — {change}", x == id ? "ffe0a8" : null)));
                    }
                    s.Add(UiNode.Text_("Center cards:", "d9b98a"));
                    s.Add(UiNode.Row(Enumerable.Range(1, 3)
                        .Select(i => UiNode.Col(
                            UiNode.Image(A(Cur("c" + i) + ".jpg"), 90),
                            UiNode.Text_(RoleName(Cur("c" + i)))))
                        .ToArray()));
                    string log = GameData.Attributes.GetValueOrDefault("log", "");
                    if (!string.IsNullOrEmpty(log)) s.Add(UiNode.Log(log));
                    return s;
                }
            }
        }

        // The night decision UI for this seat's DEALT role.
        private void AddNightChoices(List<UiNode> s, string id, List<string> occ)
        {
            string role = Orig(id);
            s.Add(UiNode.Row(
                UiNode.Image(A(role + ".jpg"), 90),
                UiNode.Text_("Your dealt role: " + RoleName(role), TeamColor(role), 18, "big")));

            var sleep = UiNode.Button("Go back to sleep", nameof(Night), new() { { "act", "sleep" } }, null, "big");

            switch (role)
            {
                case WEREWOLF when occ.Count(x => Orig(x) == WEREWOLF) == 1:
                    s.Add(UiNode.Text_("You may peek at ONE center card:"));
                    s.Add(UiNode.Row(Enumerable.Range(1, 3)
                        .Select(i => UiNode.Button("Center " + i, nameof(Night), new() { { "act", "peek:c" + i } }, null, "ok"))
                        .ToArray()));
                    s.Add(sleep);
                    break;

                case SEER:
                    s.Add(UiNode.Text_("Look at ONE player's card…"));
                    AddPlayerButtons(s, occ.Where(x => x != id),
                        x => UiNode.Button(Name(x), nameof(Night), new() { { "act", "seerp:" + x } }, null, "ok"));
                    s.Add(UiNode.Text_("…or TWO center cards:"));
                    s.Add(UiNode.Row(
                        UiNode.Button("Center 1 & 2", nameof(Night), new() { { "act", "seerc:c1,c2" } }, null, "ok"),
                        UiNode.Button("Center 1 & 3", nameof(Night), new() { { "act", "seerc:c1,c3" } }, null, "ok"),
                        UiNode.Button("Center 2 & 3", nameof(Night), new() { { "act", "seerc:c2,c3" } }, null, "ok")));
                    s.Add(sleep);
                    break;

                case ROBBER:
                    s.Add(UiNode.Text_("Swap cards with a player and look at your new card:"));
                    AddPlayerButtons(s, occ.Where(x => x != id),
                        x => UiNode.Button("Rob " + Name(x), nameof(Night), new() { { "act", "rob:" + x } }, null, "ok"));
                    s.Add(sleep);
                    break;

                case TROUBLEMAKER:
                    s.Add(new UiNode
                    {
                        Type = "checks",
                        Options = occ.Where(x => x != id).Select(x => new UiOption(Name(x), x)).ToList(),
                        Need = 2,
                        Action = nameof(Night),
                        ArgKey = "tm",
                        Text = "Swap these two players' cards"
                    });
                    s.Add(sleep);
                    break;

                case DRUNK:
                    s.Add(UiNode.Text_("You MUST exchange your card with a center card (without looking):"));
                    s.Add(UiNode.Row(Enumerable.Range(1, 3)
                        .Select(i => UiNode.Button("Center " + i, nameof(Night), new() { { "act", "drunk:c" + i } }, null, "ok"))
                        .ToArray()));
                    break;

                default:
                    s.Add(UiNode.Text_(role == WEREWOLF
                        ? "You've seen your packmate. Nothing more to do tonight."
                        : "Your role does nothing more tonight."));
                    s.Add(sleep);
                    break;
            }
        }

        // This seat's accumulated private knowledge (partners seen, cards peeked, swap receipts).
        private void AddInfoNodes(List<UiNode> s, string id)
        {
            var info = GameData.Attributes.GetValueOrDefault("ninfo:" + id, "");
            var role = Orig(id);
            s.Add(UiNode.Text_($"Your dealt role: {RoleName(role)}", TeamColor(role)));
            if (string.IsNullOrEmpty(info)) return;
            s.Add(UiNode.Col(info.Split('\n')
                .Select(line => UiNode.Text_("• " + line, "9fd0ff"))
                .ToArray()));
        }

        // Refresh/unstick control for waiting players (a vacated seat can otherwise strand a
        // phase whose last expected submission will never arrive — see TryAdvancePhase).
        private UiNode PokeButton() => UiNode.Button("Check again", nameof(Poke));

        // Which roles are in the game is PUBLIC knowledge in One Night Werewolf (the box shows the
        // cards before dealing) and the discussion depends on it — e.g. "the Drunk isn't even in
        // play tonight". Built from the roles actually DEALT (order seats + center), not from the
        // live seat count, so it stays truthful if a seat vacates mid-game.
        private UiNode RolesInPlayNode()
        {
            var order = (GameData.Attributes.GetValueOrDefault("order", "") ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries);
            var dealt = order.Concat(new[] { "c1", "c2", "c3" }).Select(Orig)
                             .Where(r => r != "").ToList();
            var counts = new Dictionary<string, int>();
            var firstSeen = new List<string>();
            foreach (var r in dealt)
            {
                if (!counts.ContainsKey(r)) { counts[r] = 0; firstSeen.Add(r); }
                counts[r]++;
            }
            var tiles = firstSeen.Select(r => UiNode.Col(
                UiNode.Image(A("token_" + r + ".png"), 54),
                UiNode.Text_(RoleName(r) + (counts[r] > 1 ? " ×" + counts[r] : ""), "cbb493", 12)
            )).ToList();

            var rows = new List<UiNode> { UiNode.Text_("Roles in play — 3 of them lie in the center:", "d9b98a") };
            for (int i = 0; i < tiles.Count; i += 6)
                rows.Add(UiNode.Row(tiles.Skip(i).Take(6).ToArray()));
            return UiNode.Col(rows.ToArray());
        }

        // Lay button lists out three per row so 9 players don't become one endless column.
        private static void AddPlayerButtons(List<UiNode> s, IEnumerable<string> ids, Func<string, UiNode> make)
        {
            var buttons = ids.Select(make).ToList();
            for (int i = 0; i < buttons.Count; i += 3)
                s.Add(UiNode.Row(buttons.Skip(i).Take(3).ToArray()));
        }

        private static string TeamColor(string role) => role switch
        {
            WEREWOLF or MINION => "ff6b6b",
            TANNER => "d9a45f",
            _ => "5fd08a",
        };

        private static string RoleBlurb(string role) => role switch
        {
            WEREWOLF => "Werewolf team. Survive the vote — deny everything.",
            MINION => "Werewolf team. Protect the wolves — even by dying in their place.",
            MASON => "Village team. You and the other Mason can vouch for each other.",
            SEER => "Village team. Tonight you may peek at a player's card, or two center cards.",
            ROBBER => "Village team. Tonight you may steal someone's card — and become it.",
            TROUBLEMAKER => "Village team. Tonight you may swap two OTHER players' cards.",
            DRUNK => "Village team. Tonight you swap into a center card. You won't know which role you got.",
            INSOMNIAC => "Village team. You wake at dawn and check what card you ended up with.",
            HUNTER => "Village team. If you die, whoever you voted for dies with you.",
            TANNER => "No team. You HATE your life — you win only if the village kills you.",
            _ => "Village team. No night power — use your eyes and ears by day.",
        };
    }
}
