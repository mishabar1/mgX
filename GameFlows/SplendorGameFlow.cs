using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Splendor — a 3D tabletop gem/engine card game (2..4 players), full base rules.
    // The board is REAL 3D (GameData.Table): gem-token piles, 3 rows of development cards, nobles.
    // Each player's own stuff lives in their 3D zones: reserved cards in player.Hand, tokens/bonuses
    // on player.Table. Everything is clickable — no HTML panel. (The client stays a dumb renderer.)
    //
    // Turn: click gem piles to pick, then TAKE (or CLEAR); or click a card to select it then BUY /
    // RESERVE. State lives in GameData.Attributes (see the panel-era comments — unchanged engine).
    public class SplendorGameFlow : BaseGameFlow
    {
        private const int WIN = 15;
        private static readonly string[] GEM = { "e", "s", "r", "d", "o" };
        private static readonly Dictionary<string, string> HEX = new()
        { { "e", "0x16a34a" }, { "s", "0x2563eb" }, { "r", "0xdc2626" }, { "d", "0x9aa4b2" }, { "o", "0x334155" }, { "g", "0xf59e0b" } };
        private static readonly Dictionary<string, string> NAME = new()
        { { "e", "Emerald" }, { "s", "Sapphire" }, { "r", "Ruby" }, { "d", "Diamond" }, { "o", "Onyx" }, { "g", "Gold" } };

        public override int MinPlayers => 2;
        public SplendorGameFlow(GameData gameData) : base(gameData) { gameData.GameType = GameTypeEnum.SPLENDOR; }
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("spl");
            internal static AssetData GEMDISC = new CylinderAssetData("splgem");
            internal static AssetData BTN = new TokenAssetData("splendor/btn.svg");
            internal static AssetData NOBLE = new TokenAssetData("splendor/noble.svg");
            internal static AssetData TABLE = new CylinderAssetData("spltable");
        }
        private AssetData CardAsset(string bonus) => addAsset(new TokenAssetData($"splendor/card_{bonus}.svg"));

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT); addAsset(Assets.GEMDISC); addAsset(Assets.BTN); addAsset(Assets.NOBLE); addAsset(Assets.TABLE);
            foreach (var c in GEM) CardAsset(c);
            GameData.Observer.Position.Set(0, 30, 18);
            // four seats evenly around the round table (near/far/right/left). A fairly steep 3/4
            // view (high + not too far back) shows the whole round table with every seat around it.
            var pos = new (int x, int z)[] { (0, 13), (0, -13), (13, 0), (-13, 0) };
            for (int i = 0; i < 4; i++)
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .SetCameraPosition((int)Math.Round(pos[i].x * 1.2), 24, (int)Math.Round(pos[i].z * 1.2))
                    .SetAvatarPosition(pos[i].x, 0, pos[i].z);
            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var seats = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();
            int n = seats.Count;
            GameData.Attributes["order"] = string.Join(",", seats);
            GameData.CurrentTurnId = seats[0];
            int gemEach = n <= 2 ? 4 : n == 3 ? 5 : 7;
            SetInts("bank", new[] { gemEach, gemEach, gemEach, gemEach, gemEach, 5 });
            foreach (var (tier, count) in new[] { (1, 40), (2, 30), (3, 20) })
            {
                var ids = new List<string>();
                for (int i = 0; i < count; i++) { string id = $"k{tier}_{i}"; GameData.Attributes["card:" + id] = GenCard(tier, rnd); ids.Add(id); }
                Shuffle(ids, rnd);
                GameData.Attributes["row" + tier] = string.Join(",", ids.Take(4));
                GameData.Attributes["deck" + tier] = string.Join(",", ids.Skip(4));
            }
            var nobles = GenNobles(rnd); Shuffle(nobles, rnd);
            var active = nobles.Take(n + 1).ToList();
            for (int i = 0; i < active.Count; i++) GameData.Attributes["noble:n" + i] = active[i];
            GameData.Attributes["nobles"] = string.Join(",", Enumerable.Range(0, active.Count).Select(i => "n" + i));
            foreach (var s in seats)
            {
                SetInts("tok:" + s, new[] { 0, 0, 0, 0, 0, 0 }); SetInts("bon:" + s, new[] { 0, 0, 0, 0, 0 });
                GameData.Attributes["pts:" + s] = "0"; GameData.Attributes["nc:" + s] = "0"; GameData.Attributes["resv:" + s] = "";
                GameData.Attributes.Remove("pending:" + s); GameData.Attributes.Remove("selcard:" + s);
            }
            GameData.Attributes["log"] = "";
            GameData.Attributes.Remove("over"); GameData.Attributes.Remove("final");

            // The client already rotates each seat's zone to face the table centre (group.lookAt),
            // so anchors are just simple LOCAL offsets: put a player's tokens/bonuses (Table) and
            // reserved cards (Hand) ON the felt (y=0.2), a few units in front of the seat toward the
            // middle. One value serves every seat. (handRot stays the base default: -90 = laid flat.)
            GameData.Attributes["tableAnchor"] = "0,0.2,2";
            GameData.Attributes["handAnchor"] = "0,0.2,0.4";
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));
        protected override List<PlayerData> GetGameWinners()
        {
            var set = GameData.Attributes.GetValueOrDefault("winnerIds", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        // ============================ card / noble generation ============================
        private static string GenCard(int tier, Random rnd)
        {
            string bonus = GEM[rnd.Next(5)]; int total, pts;
            if (tier == 1) { total = rnd.Next(3, 6); pts = rnd.Next(10) == 0 ? 1 : 0; }
            else if (tier == 2) { total = rnd.Next(6, 10); pts = rnd.Next(1, 4); }
            else { total = rnd.Next(11, 17); pts = rnd.Next(3, 6); }
            var cost = new int[5];
            for (int k = 0; k < total; k++) { int c = rnd.Next(5); if (c == Idx(bonus)) c = (c + 1) % 5; cost[c]++; }
            return $"{tier}:{bonus}:{pts}:{string.Join("#", cost)}";
        }
        private static List<string> GenNobles(Random rnd)
        {
            var list = new List<string>();
            foreach (var (a, b) in new[] { (0, 1), (0, 2), (0, 3), (0, 4), (1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4) }.OrderBy(_ => rnd.Next()).Take(5))
            { var req = new int[5]; req[a] = 4; req[b] = 4; list.Add("3:" + string.Join("#", req)); }
            foreach (var (a, b, c) in new[] { (0, 1, 2), (0, 1, 3), (0, 2, 4), (1, 3, 4), (2, 3, 4), (0, 3, 4), (1, 2, 3) }.OrderBy(_ => rnd.Next()).Take(5))
            { var req = new int[5]; req[a] = 3; req[b] = 3; req[c] = 3; list.Add("3:" + string.Join("#", req)); }
            return list;
        }

        // ============================ actions (all triggered by clicking 3D items) ============================
        [GameAction] public async Task PickGem(ExecuteActionData d) { DoPick(d.Player!.Id, Arg(d, "color")); await Task.CompletedTask; }
        [GameAction] public async Task ClearPick(ExecuteActionData d) { if (MyTurn(d.Player!.Id)) GameData.Attributes.Remove("pending:" + d.Player!.Id); await Task.CompletedTask; }
        [GameAction] public async Task TakeGems(ExecuteActionData d) { DoTake(d.Player!.Id); await Task.CompletedTask; }
        [GameAction] public async Task SelectCard(ExecuteActionData d) { DoSelect(d.Player!.Id, Arg(d, "card")); await Task.CompletedTask; }
        [GameAction] public async Task Buy(ExecuteActionData d) { var id = Arg(d, "card"); DoBuy(d.Player!.Id, string.IsNullOrEmpty(id) ? Sel(d.Player!.Id) : id); await Task.CompletedTask; }
        [GameAction] public async Task Reserve(ExecuteActionData d) { var id = Arg(d, "card"); DoReserve(d.Player!.Id, string.IsNullOrEmpty(id) ? Sel(d.Player!.Id) : id); await Task.CompletedTask; }

        private bool MyTurn(string seat) => seat == GameData.CurrentTurnId && !GameData.Attributes.ContainsKey("over");
        private string Sel(string seat) => GameData.Attributes.GetValueOrDefault("selcard:" + seat, "");
        private List<string> Pending(string seat) => ListAttr("pending:" + seat);

        private void DoPick(string seat, string color)
        {
            if (!MyTurn(seat) || Array.IndexOf(GEM, color) < 0) return;
            var bank = Ints("bank"); if (bank[Idx(color)] <= 0) return;
            var p = Pending(seat);
            bool dup = p.GroupBy(x => x).Any(g => g.Count() > 1);
            if (p.Contains(color))
            {
                if (p.Count == 1 && bank[Idx(color)] >= 4) p.Add(color);   // going for 2-of-a-kind
                else return;
            }
            else
            {
                if (dup || p.Count >= 3) return;                            // already committed to 2-same, or full
                p.Add(color);
            }
            SetList("pending:" + seat, p);
        }

        private void DoTake(string seat)
        {
            if (!MyTurn(seat)) return;
            var p = Pending(seat); if (p.Count == 0) return;
            GameData.Attributes.Remove("pending:" + seat);
            if (p.Count == 2 && p[0] == p[1]) DoTakeTwo(seat, p[0]);
            else DoTakeThree(seat, string.Join(",", p.Distinct()));
        }

        private void DoSelect(string seat, string card)
        {
            if (!MyTurn(seat) || Card(card) == null) return;
            if (CardRowIndex(card) < 0 && !ResvList(seat).Contains(card)) return;
            GameData.Attributes["selcard:" + seat] = card;
        }

        // ---- internal rules (shared by clicks and AI) ----
        private void DoTakeThree(string seat, string colorsCsv)
        {
            var cols = colorsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct().Where(c => Array.IndexOf(GEM, c) >= 0).Take(3).ToList();
            if (cols.Count == 0) return;
            var bank = Ints("bank"); if (cols.Any(c => bank[Idx(c)] <= 0)) return;
            var tok = Ints("tok:" + seat);
            foreach (var c in cols) { bank[Idx(c)]--; tok[Idx(c)]++; }
            SetInts("bank", bank); SetInts("tok:" + seat, tok);
            Log($"{Name(seat)} took {string.Join(" ", cols.Select(c => NAME[c]))}"); AfterTurn(seat);
        }
        private void DoTakeTwo(string seat, string color)
        {
            var bank = Ints("bank"); if (bank[Idx(color)] < 4) return;
            var tok = Ints("tok:" + seat); bank[Idx(color)] -= 2; tok[Idx(color)] += 2;
            SetInts("bank", bank); SetInts("tok:" + seat, tok);
            Log($"{Name(seat)} took 2 {NAME[color]}"); AfterTurn(seat);
        }
        private void DoReserve(string seat, string card)
        {
            if (!MyTurn(seat)) return;
            var resv = ResvList(seat); if (resv.Count >= 3 || string.IsNullOrEmpty(card)) return;
            string id = card;
            if (CardRowIndex(card) < 0) return;
            int tier = CardTier(card); if (!RemoveFromRow(tier, card)) return;
            resv.Add(id); SetList("resv:" + seat, resv);
            var bank = Ints("bank");
            if (bank[5] > 0) { bank[5]--; var tok = Ints("tok:" + seat); tok[5]++; SetInts("tok:" + seat, tok); SetInts("bank", bank); }
            GameData.Attributes.Remove("selcard:" + seat);
            Log($"{Name(seat)} reserved a card"); AfterTurn(seat);
        }
        private void DoBuy(string seat, string id)
        {
            if (!MyTurn(seat) || string.IsNullOrEmpty(id)) return;
            var def = Card(id); if (def == null) return;
            bool fromReserve = ResvList(seat).Contains(id);
            if (!fromReserve && CardRowIndex(id) < 0) return;
            var tok = Ints("tok:" + seat); var bon = Ints("bon:" + seat);
            int goldNeed = 0; var pay = new int[5];
            for (int c = 0; c < 5; c++) { int need = Math.Max(0, def.cost[c] - bon[c]); pay[c] = Math.Min(need, tok[c]); goldNeed += need - pay[c]; }
            if (goldNeed > tok[5]) return;
            var bank = Ints("bank");
            for (int c = 0; c < 5; c++) { tok[c] -= pay[c]; bank[c] += pay[c]; }
            tok[5] -= goldNeed; bank[5] += goldNeed; bon[Idx(def.bonus)]++;
            SetInts("tok:" + seat, tok); SetInts("bon:" + seat, bon); SetInts("bank", bank);
            GameData.Attributes["pts:" + seat] = (Pts(seat) + def.points).ToString();
            GameData.Attributes["nc:" + seat] = (int.Parse(GameData.Attributes.GetValueOrDefault("nc:" + seat, "0")) + 1).ToString();
            if (fromReserve) { var r = ResvList(seat); r.Remove(id); SetList("resv:" + seat, r); } else RemoveFromRow(def.tier, id);
            GameData.Attributes.Remove("selcard:" + seat);
            VisitNobles(seat); Log($"{Name(seat)} bought a {NAME[def.bonus]} card (+{def.points})"); AfterTurn(seat);
        }
        private void VisitNobles(string seat)
        {
            var bon = Ints("bon:" + seat);
            foreach (var nid in ListAttr("nobles"))
            {
                var nb = Noble(nid);
                if (Enumerable.Range(0, 5).All(c => bon[c] >= nb.req[c]))
                { GameData.Attributes["pts:" + seat] = (Pts(seat) + nb.pts).ToString(); var ns = ListAttr("nobles"); ns.Remove(nid); SetList("nobles", ns); Log($"A noble visits {Name(seat)} (+{nb.pts})"); break; }
            }
        }
        private void AfterTurn(string seat)
        {
            EnforceTokenLimit(seat);
            if (Pts(seat) >= WIN) GameData.Attributes["final"] = "1";
            var order = ListAttr("order"); int i = order.IndexOf(seat); string next = order[(i + 1) % order.Count];
            GameData.CurrentTurnId = next;
            if (GameData.Attributes.ContainsKey("final") && next == order[0]) EndNow();
        }
        private void EnforceTokenLimit(string seat)
        {
            var tok = Ints("tok:" + seat); var bank = Ints("bank");
            while (tok.Sum() > 10)
            {
                int best = -1, bestVal = -1; for (int c = 0; c < 5; c++) if (tok[c] > bestVal) { bestVal = tok[c]; best = c; }
                if (bestVal <= 0) { if (tok[5] > 0) { tok[5]--; bank[5]++; continue; } break; }
                tok[best]--; bank[best]++;
            }
            SetInts("tok:" + seat, tok); SetInts("bank", bank);
        }
        private void EndNow()
        {
            var order = ListAttr("order"); int best = order.Max(Pts);
            var top = order.Where(s => Pts(s) == best).ToList();
            if (top.Count > 1) { int few = top.Min(s => int.Parse(GameData.Attributes.GetValueOrDefault("nc:" + s, "0"))); top = top.Where(s => int.Parse(GameData.Attributes.GetValueOrDefault("nc:" + s, "0")) == few).ToList(); }
            GameData.Attributes["over"] = "1"; GameData.Attributes["winnerIds"] = string.Join(",", top);
            GameData.Attributes["result"] = top.Count == 1 ? $"{Name(top[0])} wins with {best} points!" : "Tie!";
        }

        // ============================ AI ============================
        public override bool IsAITurn(PlayerData player) => MyTurn(player.Id);
        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (!MyTurn(player.Id)) { await Task.CompletedTask; return false; }
            string seat = player.Id;
            var buyable = AllVisibleCards().Concat(ResvList(seat)).Where(id => Card(id) != null && CanAfford(seat, id))
                .OrderByDescending(id => Card(id)!.points).ThenBy(id => Card(id)!.cost.Sum()).ToList();
            if (buyable.Count > 0) { DoBuy(seat, buyable[0]); await Task.CompletedTask; return true; }
            var bank = Ints("bank");
            var avail = GEM.Where(c => bank[Idx(c)] > 0).OrderByDescending(c => bank[Idx(c)]).ToList();
            if (avail.Count >= 1 && Ints("tok:" + seat).Sum() <= 8) { DoTakeThree(seat, string.Join(",", avail.Take(3))); await Task.CompletedTask; return true; }
            var two = GEM.FirstOrDefault(c => bank[Idx(c)] >= 4);
            if (two != null) { DoTakeTwo(seat, two); await Task.CompletedTask; return true; }
            if (ResvList(seat).Count < 3) { var t3 = ListAttr("row3").FirstOrDefault(); if (t3 != null) { DoReserve(seat, t3); await Task.CompletedTask; return true; } }
            var one = GEM.FirstOrDefault(c => bank[Idx(c)] > 0);
            if (one != null) { DoTakeThree(seat, one); await Task.CompletedTask; return true; }
            AfterTurn(seat); await Task.CompletedTask; return true;
        }
        private bool CanAfford(string seat, string id)
        {
            var def = Card(id); if (def == null) return false;
            var tok = Ints("tok:" + seat); var bon = Ints("bon:" + seat); int gold = tok[5], shortfall = 0;
            for (int c = 0; c < 5; c++) shortfall += Math.Max(0, def.cost[c] - bon[c] - tok[c]);
            return shortfall <= gold;
        }

        // ============================ 3D RENDER (board + player zones) ============================
        protected override void RefreshScreens() => Render();

        private void Render()
        {
            GameData.Table = ItemData.Table();
            foreach (var p in GameData.Players) { p.Hand = new ItemData("", null) { Name = "HAND" }; p.Table = new ItemData("", null) { Name = "TABLE" }; }

            addItem(Assets.TABLE).SetPosition(0, -0.2, 0).SetScale(28, 0.3, 28).AddAttribute("tint", "0x0f3d2e"); // round felt

            string cur = GameData.CurrentTurnId ?? "";
            bool over = GameData.Attributes.ContainsKey("over");

            // title floats above the centre of the table
            addTextItem(Assets.TEXT).SetText(over ? GameData.Attributes.GetValueOrDefault("result", "Game over")
                : $"SPLENDOR   ·   {Name(cur)}'s turn").SetPosition(0, 6, 0).SetScale(0.9).AddAttribute("textColor", "ffd166");

            // Card grid CENTRED on the table (3 rows). Gem bank as a column on the LEFT, nobles as a
            // column on the RIGHT — so the play area is symmetric and clear of every seat.
            double[] tierZ = { 0, 3, 0, -3 };   // index by tier (t1 front, t3 back)
            for (int tier = 3; tier >= 1; tier--)
            {
                var row = ListAttr("row" + tier);
                for (int i = 0; i < row.Count; i++) BoardCard(row[i], -4.5 + i * 3, tierZ[tier], cur);
                addTextItem(Assets.TEXT).SetText($"T{tier}·{ListAttr("deck" + tier).Count}").SetPosition(-8.3, 0.1, tierZ[tier]).SetScale(0.4).SetRotation(-90, 0, 0).AddAttribute("textColor", "cbd5e1");
            }

            var bank = Ints("bank");
            for (int i = 0; i < GEM.Length; i++) GemPile(GEM[i], bank[Idx(GEM[i])], -6.5, -4 + i * 1.8, cur);
            GemPile("g", bank[5], -6.5, -4 + 5 * 1.8, "");   // gold column bottom (not directly takeable)

            var nobles = ListAttr("nobles");
            for (int i = 0; i < nobles.Count; i++)
                addItem(Assets.NOBLE).SetPosition(6.5, 0.05, -((nobles.Count - 1) * 2.1) / 2 + i * 2.1).SetScale(1.8, 1, 1.8).AddAttribute("noble", "1");

            // per-player zones (tokens/bonuses on table, reserved cards in hand, points overhead)
            foreach (var seat in GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT))
                PlayerZone(seat, cur);

            // contextual action buttons in front of the current player
            if (!over) ActionButtons(cur);
        }

        private void GemPile(string c, int count, double x, double z, string clickSeat)
        {
            var d = addItem(Assets.GEMDISC).SetPosition(x, 0.25, z).SetScale(1.6, 0.5, 1.6).AddAttribute("tint", HEX[c]).AddAttribute("gem", "1");
            if (!string.IsNullOrEmpty(clickSeat) && count > 0) { d.ClickActions[clickSeat] = nameof(PickGem); d.AddAttribute("color", c); }
            addTextItem(Assets.TEXT).SetText(count.ToString()).SetPosition(x, 0.6, z).SetScale(0.5).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
        }

        private void BoardCard(string id, double x, double z, string clickSeat)
        {
            var def = Card(id)!;
            bool selected = Sel(clickSeat) == id;
            var card = addItem(CardAsset(def.bonus)).SetPosition(x, 0.05, z).SetScale(2.3, 1, 2.8).AddAttribute("card", "1");
            if (!string.IsNullOrEmpty(clickSeat)) { card.ClickActions[clickSeat] = nameof(SelectCard); card.AddAttribute("cardid", id); }
            if (selected) card.AddAttribute("selected", "1");
            if (def.points > 0) addTextItem(Assets.TEXT).SetText(def.points.ToString()).SetPosition(x + 0.85, 0.12, z - 1.35).SetScale(0.5).SetRotation(-90, 0, 0).AddAttribute("textColor", "1f2937");
            // cost pips along the bottom of the card
            int k = 0; for (int c = 0; c < 5; c++) if (def.cost[c] > 0)
            { double px = x - 0.9 + k * 0.55; k++;
              addItem(Assets.GEMDISC).SetPosition(px, 0.12, z + 1.15).SetScale(0.5, 0.3, 0.5).AddAttribute("tint", HEX[GEM[c]]);
              addTextItem(Assets.TEXT).SetText(def.cost[c].ToString()).SetPosition(px, 0.22, z + 1.15).SetScale(0.28).SetRotation(-90, 0, 0).AddAttribute("textColor", GEM[c] == "d" ? "111827" : "ffffff"); }
        }

        private void PlayerZone(PlayerData seat, string cur)
        {
            var bon = Ints("bon:" + seat.Id); var tok = Ints("tok:" + seat.Id);
            // The table anchor is already ON the felt (server-set per seat), so items go at local y≈0.
            addItemToPlayerTable(seat, Assets.TEXT).SetText($"{Name(seat.Id)}  {Pts(seat.Id)}pts").SetPosition(0, 1.3, 0).SetScale(0.55).AddAttribute("textColor", seat.Id == cur ? "ffd166" : "cbd5e1");
            for (int c = 0; c < 5; c++)
            {
                double x = -2.4 + c * 1.2;
                addItemToPlayerTable(seat, Assets.GEMDISC).SetPosition(x, 0, 0).SetScale(0.9, 0.35, 0.9).AddAttribute("tint", HEX[GEM[c]]);
                addItemToPlayerTable(seat, Assets.TEXT).SetText($"{bon[c]}·{tok[c]}").SetPosition(x, 0.35, 0.7).SetScale(0.3).SetRotation(-90, 0, 0).AddAttribute("textColor", "e8edf5");
            }
            if (tok[5] > 0) addItemToPlayerTable(seat, Assets.TEXT).SetText($"gold {tok[5]}").SetPosition(3.4, 0.35, 0.7).SetScale(0.3).SetRotation(-90, 0, 0).AddAttribute("textColor", "f59e0b");
            // reserved cards in the player's hand (clickable by owner on their turn)
            var resv = ResvList(seat.Id);
            for (int i = 0; i < resv.Count; i++)
            {
                var def = Card(resv[i])!;
                var card = addItemToPlayerHand(seat, CardAsset(def.bonus)).SetPosition(-1.6 + i * 1.6, 0, 0).SetScale(1.3, 1, 1.7).AddAttribute("card", "1");
                if (seat.Id == cur) { card.ClickActions[cur] = nameof(SelectCard); card.AddAttribute("cardid", resv[i]); if (Sel(cur) == resv[i]) card.AddAttribute("selected", "1"); }
            }
        }

        private void ActionButtons(string cur)
        {
            var buttons = new List<(string label, string action, string color)>();
            var p = Pending(cur);
            if (p.Count > 0) { buttons.Add(("TAKE " + string.Join("", p.Select(x => x.ToUpper())), nameof(TakeGems), "0x2f7a45")); buttons.Add(("CLEAR", nameof(ClearPick), "0x7a2f2f")); }
            var sel = Sel(cur);
            if (!string.IsNullOrEmpty(sel) && Card(sel) != null)
            {
                if (CanAfford(cur, sel)) buttons.Add(("BUY", nameof(Buy), "0x2f7a45"));
                if (CardRowIndex(sel) >= 0 && ResvList(cur).Count < 3) buttons.Add(("RESERVE", nameof(Reserve), "0x6a4a25"));
            }
            // Place the buttons on the felt just in front of the current player, spread tangentially
            // and turned to face the centre — so "your controls" are always by whoever's turn it is.
            var cp = GameData.Players.Find(p => p.Id == cur);
            double ax = cp?.Avatar.Position.X ?? 0, az = cp?.Avatar.Position.Z ?? 8, len = Math.Sqrt(ax * ax + az * az); if (len < 0.1) len = 1;
            double ux = -ax / len, uz = -az / len;                 // toward centre
            double baseX = ax + ux * 4.5, baseZ = az + uz * 4.5;   // 4.5 in from the seat
            double tx = uz, tz = -ux, yaw = Math.Atan2(ux, uz) * 180 / Math.PI;   // tangent + facing
            for (int i = 0; i < buttons.Count; i++)
            {
                double off = -((buttons.Count - 1) * 3.4) / 2 + i * 3.4;
                double bx = baseX + tx * off, bz = baseZ + tz * off;
                addItem(Assets.BTN).SetPosition(bx, 0.15, bz).SetRotation(0, yaw, 0).SetScale(3, 1, 1.1)
                    .AddAttribute("tint", buttons[i].color).AddAttribute("button", "1").ClickActions[cur] = buttons[i].action;
                addTextItem(Assets.TEXT).SetText(buttons[i].label).SetPosition(bx, 0.25, bz).SetScale(0.4).SetRotation(-90, 0, 0).AddAttribute("textColor", "ffffff");
            }
        }

        // ============================ helpers ============================
        private class CardDef { public int tier; public string bonus = "e"; public int points; public int[] cost = new int[5]; }
        private CardDef? Card(string id)
        {
            var s = GameData.Attributes.GetValueOrDefault("card:" + id, ""); if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(':'); return new CardDef { tier = int.Parse(p[0]), bonus = p[1], points = int.Parse(p[2]), cost = p[3].Split('#').Select(int.Parse).ToArray() };
        }
        private int CardTier(string id) => Card(id)?.tier ?? int.Parse(id.Substring(1, 1));
        private (int pts, int[] req) Noble(string nid) { var p = GameData.Attributes.GetValueOrDefault("noble:" + nid, "3:0#0#0#0#0").Split(':'); return (int.Parse(p[0]), p[1].Split('#').Select(int.Parse).ToArray()); }
        private static int Idx(string c) => Array.IndexOf(GEM, c);
        private int[] Ints(string key) => (GameData.Attributes.GetValueOrDefault(key, "") ?? "").Split('#', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        private void SetInts(string key, int[] v) => GameData.Attributes[key] = string.Join("#", v);
        private List<string> ListAttr(string key) => (GameData.Attributes.GetValueOrDefault(key, "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        private void SetList(string key, List<string> v) => GameData.Attributes[key] = string.Join(",", v);
        private List<string> ResvList(string seat) => ListAttr("resv:" + seat);
        private int Pts(string seat) => int.TryParse(GameData.Attributes.GetValueOrDefault("pts:" + seat, "0"), out var v) ? v : 0;
        private List<string> AllVisibleCards() => ListAttr("row1").Concat(ListAttr("row2")).Concat(ListAttr("row3")).ToList();
        private int CardRowIndex(string id) { int t = CardTier(id); return ListAttr("row" + t).IndexOf(id); }
        private bool RemoveFromRow(int tier, string id)
        {
            var row = ListAttr("row" + tier); if (!row.Remove(id)) return false;
            var deck = ListAttr("deck" + tier); if (deck.Count > 0) { row.Add(deck[0]); deck.RemoveAt(0); SetList("deck" + tier, deck); }
            SetList("row" + tier, row); return true;
        }
        private string Name(string seat) { var p = GameData.Players.Find(x => x.Id == seat); return p != null ? PlayerDisplayName(p) : "?"; }
        private void Log(string line) { var cur = GameData.Attributes.GetValueOrDefault("log", ""); var lines = (cur + (string.IsNullOrEmpty(cur) ? "" : "\n") + line).Split('\n'); GameData.Attributes["log"] = string.Join("\n", lines.TakeLast(12)); }
        private static void Shuffle<T>(List<T> l, Random r) { for (int i = l.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }
    }
}
