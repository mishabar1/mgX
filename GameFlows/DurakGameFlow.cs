using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Durak (Дурак) — 2-player attack/defend.
    // State lives entirely in GameData.Attributes (hands, deck, field, roles) so the scene can
    // be rebuilt from scratch on every action; Render() lays out the octagon table, both hands
    // (owner-private via the card's "owner" property), the field, the deck/trump, the action
    // buttons and per-player status text.
    //
    // Flow (simplified podkidnoy): the attacker plays a card; the defender must beat it (higher
    // same-suit, or a trump) or Take it. Once all attacks are beaten the attacker may throw in
    // another card whose rank is already on the table, or press Done to end the bout (cards
    // discarded, roles swap). After each bout both refill to 6 (attacker first). Deck empty and a
    // player out of cards ends the game — the last player holding cards is the Durak (loser).
    public class DurakGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("durak");
            internal static AssetData TABLE = new TokenAssetData("durak/table.png");
        }

        private const double CARD_SCALE = 1.3;
        private const double HAND_SPACING = 0.95;
        private const double FIELD_SPACING = 1.2;

        public DurakGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DURAK;
        }

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            addAsset(Assets.TABLE);

            GameData.Observer.Position.Set(0, 13, 0);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "p1").SetCameraPosition(0, 9, -11).SetAvatarPosition(0, 2, -9);
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "p2").SetCameraPosition(0, 9, 11).SetAvatarPosition(0, 2, 9);

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            var rnd = new Random();
            var deck = DurakRules.BuildDeck();
            DurakRules.Shuffle(deck, rnd);

            var trump = deck[0];                    // bottom card is the trump
            GameData.Attributes["trump"] = trump.Suit.ToString();
            GameData.Attributes["trumpCard"] = trump.Code;

            // Pre-register ALL 36 card faces (+ the back) up front, so every card the client will
            // ever need is in the asset list from the first load — no later "unknown asset key".
            foreach (var s in DurakRules.Suits)
                foreach (var r in DurakRules.Ranks)
                    CardAsset(new DurakRules.Card(r, s));
            BackAsset();
            SuitAsset(trump.Suit);   // the trump suit symbol shown by the deck
            ButtonBgAsset();         // the action-button plate

            var p1 = GameData.Players[0];
            var p2 = GameData.Players[1];
            var h1 = new List<DurakRules.Card>();
            var h2 = new List<DurakRules.Card>();
            for (int i = 0; i < DurakRules.HandSize; i++) { h1.Add(DrawTop(deck)); h2.Add(DrawTop(deck)); }

            SetHand(p1.Id, h1);
            SetHand(p2.Id, h2);
            SetDeck(deck);
            GameData.Attributes["field"] = "";
            GameData.Attributes["discard"] = "0";
            GameData.Attributes["attacker"] = p1.Id;
            GameData.Attributes["defender"] = p2.Id;
            GameData.Attributes.Remove("over");
            GameData.Attributes.Remove("result");
            GameData.Attributes.Remove("winnerId");

            Render();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));

        protected override List<PlayerData> GetGameWinners()
        {
            if (GameData.Attributes.TryGetValue("winnerId", out var wid) && !string.IsNullOrEmpty(wid))
            {
                var p = GameData.Players.Find(x => x.Id == wid);
                if (p != null) return new List<PlayerData> { p };
            }
            return new List<PlayerData>();
        }

        // ============================ player actions ============================
        [GameAction] public async Task AttackCard(ExecuteActionData d) { DoAttack(d.Player!.Id, d.Item!.GetStringAttribute("code")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task DefendCard(ExecuteActionData d) { DoDefend(d.Player!.Id, d.Item!.GetStringAttribute("code")); Render(); await Task.CompletedTask; }
        [GameAction] public async Task TakeCards(ExecuteActionData d) { DoTake(d.Player!.Id); Render(); await Task.CompletedTask; }
        [GameAction] public async Task DoneAttack(ExecuteActionData d) { DoDone(d.Player!.Id); Render(); await Task.CompletedTask; }

        // ============================ AI ============================
        public override bool IsAITurn(PlayerData player)
        {
            if (GameData.Attributes.ContainsKey("over")) return false;
            return Undefended().Count > 0 ? player.Id == Defender : player.Id == Attacker;
        }

        public override async Task<bool> PlayAI(PlayerData player, Random rnd)
        {
            if (GameData.Attributes.ContainsKey("over")) { await Task.CompletedTask; return false; }
            char trump = Trump;
            var undef = Undefended();

            if (undef.Count > 0)
            {
                if (player.Id != Defender) { await Task.CompletedTask; return false; }
                var att = ParseCode(undef[0]);
                var hand = GetHand(Defender);
                var beat = hand.Where(c => DurakRules.Beats(c, att, trump))
                               .OrderBy(c => Val(c, trump)).Cast<DurakRules.Card?>().FirstOrDefault();
                if (beat != null) DoDefend(Defender, beat.Value.Code);
                else DoTake(Defender);
            }
            else
            {
                if (player.Id != Attacker) { await Task.CompletedTask; return false; }
                var field = GetField();
                if (field.Count == 0)
                {
                    var hand = GetHand(Attacker).OrderBy(c => Val(c, trump)).ToList();
                    if (hand.Count > 0) DoAttack(Attacker, hand[0].Code);
                }
                else
                {
                    DoDone(Attacker); // simple AI: doesn't throw in
                }
            }
            Render();
            await Task.CompletedTask;
            return true;
        }

        // ============================ rules / state mutation ============================
        private void DoAttack(string actorId, string code)
        {
            if (GameData.Attributes.ContainsKey("over")) return;
            if (Undefended().Count > 0 || actorId != Attacker) return;    // attacker's turn only
            var hand = GetHand(Attacker);
            if (!hand.Any(c => c.Code == code)) return;
            var field = GetField();
            if (field.Count >= 6) return;
            if (field.Count > 0)
            {
                var ranks = field.SelectMany(p => p.def == null ? new[] { p.att.Rank } : new[] { p.att.Rank, p.def.Value.Rank }).ToHashSet();
                if (!ranks.Contains(ParseCode(code).Rank)) return;         // throw-in must match a rank on the table
            }
            hand.RemoveAll(c => c.Code == code);
            field.Add((ParseCode(code), null));
            SetHand(Attacker, hand);
            SetField(field);
        }

        private void DoDefend(string actorId, string code)
        {
            if (GameData.Attributes.ContainsKey("over")) return;
            if (actorId != Defender) return;
            var field = GetField();
            int idx = field.FindIndex(p => p.def == null);
            if (idx < 0) return;                                          // nothing to defend
            var hand = GetHand(Defender);
            var card = ParseCode(code);
            if (!hand.Any(c => c.Code == code)) return;
            if (!DurakRules.Beats(card, field[idx].att, Trump)) return;   // illegal defense
            hand.RemoveAll(c => c.Code == code);
            field[idx] = (field[idx].att, card);
            SetHand(Defender, hand);
            SetField(field);
        }

        private void DoTake(string actorId)
        {
            if (GameData.Attributes.ContainsKey("over")) return;
            if (actorId != Defender) return;
            var field = GetField();
            if (field.Count == 0) return;
            var hand = GetHand(Defender);
            foreach (var (att, def) in field) { hand.Add(att); if (def != null) hand.Add(def.Value); }
            SetHand(Defender, hand);
            SetField(new List<(DurakRules.Card, DurakRules.Card?)>());
            Refill(Attacker, Defender);   // attacker refills; defender kept the cards
            // roles unchanged: defender failed, same attacker attacks the next bout
            EndCheck();
        }

        private void DoDone(string actorId)
        {
            if (GameData.Attributes.ContainsKey("over")) return;
            if (actorId != Attacker) return;
            var field = GetField();
            if (field.Count == 0 || field.Any(p => p.def == null)) return; // only when all beaten
            int discarded = int.Parse(GameData.Attributes.GetValueOrDefault("discard", "0"));
            discarded += field.Count * 2;
            GameData.Attributes["discard"] = discarded.ToString();
            SetField(new List<(DurakRules.Card, DurakRules.Card?)>());
            var oldAtt = Attacker; var oldDef = Defender;
            Refill(oldAtt, oldDef);
            GameData.Attributes["attacker"] = oldDef;  // beaten defender becomes attacker
            GameData.Attributes["defender"] = oldAtt;
            EndCheck();
        }

        private void Refill(params string[] seatOrder)
        {
            var deck = GetDeck();
            foreach (var seat in seatOrder)
            {
                var hand = GetHand(seat);
                while (hand.Count < DurakRules.HandSize && deck.Count > 0) hand.Add(DrawTop(deck));
                SetHand(seat, hand);
            }
            SetDeck(deck);
        }

        private void EndCheck()
        {
            if (GetDeck().Count > 0) return; // game continues while cards remain to draw
            int a = GetHand(Attacker).Count, d = GetHand(Defender).Count;
            if (a > 0 && d > 0) return;

            string result; string winnerId = "";
            if (a == 0 && d == 0) result = "Draw!";
            else
            {
                var outSeat = a == 0 ? Attacker : Defender;
                var foolSeat = a == 0 ? Defender : Attacker;
                winnerId = outSeat;
                result = $"{Name(foolSeat)} is the DURAK! ({Name(outSeat)} is out)";
            }
            GameData.Attributes["over"] = "1";
            GameData.Attributes["result"] = result;
            if (winnerId != "") GameData.Attributes["winnerId"] = winnerId;
        }

        // ============================ rendering ============================
        private void Render()
        {
            // Rebuild the whole scene from state.
            GameData.Table = ItemData.Table();
            foreach (var p in GameData.Players) p.Hand = new ItemData("", null) { Name = "PLAYER HAND" };

            addItem(Assets.TABLE).SetPosition(0, -0.05, 0).SetScale(20, 1, 20);

            bool over = GameData.Attributes.ContainsKey("over");
            var field = GetField();
            int undef = field.Count(p => p.def == null);

            // hands — highlight & enable only the cards this player may legally play right now
            foreach (var seat in GameData.Players)
            {
                bool canAttack = !over && undef == 0 && seat.Id == Attacker;
                bool canDefend = !over && undef > 0 && seat.Id == Defender;
                HashSet<string>? playable = canAttack ? LegalAttacks(seat.Id)
                                          : canDefend ? LegalDefends(seat.Id)
                                          : null;
                RenderHand(seat, canAttack ? nameof(AttackCard) : canDefend ? nameof(DefendCard) : null, playable);
            }

            RenderField(field);
            RenderDeckAndTrump();

            // action buttons
            if (!over)
            {
                if (undef > 0)
                    RenderButton("TAKE", Defender, nameof(TakeCards));
                else if (field.Count > 0 && field.All(p => p.def != null))
                    RenderButton("DONE", Attacker, nameof(DoneAttack));
            }

            RenderStatusText();
        }

        private void RenderHand(PlayerData seat, string? clickAction, HashSet<string>? playable)
        {
            var cards = GetHand(seat.Id);
            // Tidy the fan: group by suit, ascending rank within each suit, with the trump
            // suit last so it sits together on one end.
            char trump = Trump;
            cards = cards
                .OrderBy(c => c.Suit == trump ? 1 : 0)
                .ThenBy(c => c.Suit)
                .ThenBy(c => c.Rank)
                .ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                double x = (i - (cards.Count - 1) / 2.0) * HAND_SPACING;
                var item = addItemToPlayerHand(seat, CardAsset(cards[i]))
                    .SetPosition(x, 0, 0)
                    .SetScale(CARD_SCALE)
                    .AddAttribute("card", "1")
                    .AddAttribute("code", cards[i].Code)
                    .AddAttribute("owner", seat.Id);   // only the owner's client draws the face

                // Only legally-playable cards are highlighted AND clickable — a live hint of
                // your options this turn.
                if (playable != null && playable.Contains(cards[i].Code))
                {
                    item.AddAttribute("playable", "1");
                    if (clickAction != null) item.ClickActions[seat.Id] = clickAction;
                }
            }
        }

        // Cards this player could legally attack/throw-in with right now.
        private HashSet<string> LegalAttacks(string seatId)
        {
            var set = new HashSet<string>();
            var field = GetField();
            if (field.Count >= 6) return set;
            var hand = GetHand(seatId);
            if (field.Count == 0) { foreach (var c in hand) set.Add(c.Code); return set; }
            var ranks = field.SelectMany(p => p.def == null ? new[] { p.att.Rank } : new[] { p.att.Rank, p.def.Value.Rank }).ToHashSet();
            foreach (var c in hand) if (ranks.Contains(c.Rank)) set.Add(c.Code);
            return set;
        }

        // Cards this player could legally defend with (beat the current undefended attack).
        private HashSet<string> LegalDefends(string seatId)
        {
            var set = new HashSet<string>();
            var undef = GetField().FirstOrDefault(p => p.def == null);
            if (undef.att.Rank == 0) return set; // none undefended
            foreach (var c in GetHand(seatId))
                if (DurakRules.Beats(c, undef.att, Trump)) set.Add(c.Code);
            return set;
        }

        private void RenderField(List<(DurakRules.Card att, DurakRules.Card? def)> field)
        {
            for (int i = 0; i < field.Count; i++)
            {
                double x = (i - (field.Count - 1) / 2.0) * FIELD_SPACING;
                addItem(CardAsset(field[i].att)).SetPosition(x, 0.02, -0.15).SetScale(CARD_SCALE)
                    .AddAttribute("fieldAtt", "1");
                if (field[i].def != null)
                    addItem(CardAsset(field[i].def!.Value)).SetPosition(x + 0.15, 0.06, 0.25).SetScale(CARD_SCALE)
                        .AddAttribute("fieldDef", "1");
            }
        }

        private void RenderDeckAndTrump()
        {
            int deckCount = GetDeck().Count;

            // Trump indicator: the suit SYMBOL laid FLAT on the table beside the deck. The trump
            // suit is fixed all game, so it stays shown even once the deck is empty.
            addItem(SuitAsset(Trump)).SetPosition(7.4, 0.02, 0).SetRotation(0, -90, 0).SetScale(1.6)
                .AddAttribute("trumpSuit", "1");

            // The face-down draw pile — only while cards remain to draw.
            if (deckCount > 0)
                addItem(BackAsset()).SetPosition(6.0, 0.06, 0).SetScale(CARD_SCALE).AddAttribute("deckPile", "1");
        }

        private void RenderButton(string label, string seatId, string action)
        {
            var seat = GameData.Players.Find(p => p.Id == seatId);
            if (seat == null) return;
            bool p1 = seat.GetStringAttribute("type") == "p1";
            // In front of the hand, pulled in from the corner so it sits fully on the felt.
            double z = p1 ? -8.2 : 8.2;
            double x = p1 ? 3.2 : -3.2;   // toward centre from that player's own view
            double roll = p1 ? 180 : 0;

            // Clickable plate = the whole button (not just the glyph outline).
            var plate = addItem(ButtonBgAsset()).SetPosition(x, 0.05, z).SetScale(2.0)
                .AddAttribute("button", "1");
            plate.ClickActions[seatId] = action;
            plate.Visible[seatId] = true;

            // White label sitting on top of the plate (purely visual — the plate takes clicks).
            addTextItem(Assets.TEXT).SetText(label)
                .SetPosition(x, 0.14, z).SetScale(0.45).SetRotation(-90, 0, roll)
                .AddAttribute("buttonLabel", "1")
                .AddAttribute("textColor", "ffffff")
                .Visible[seatId] = true;
        }

        private void RenderStatusText()
        {
            char trump = Trump;
            int deckCount = GetDeck().Count;
            bool over = GameData.Attributes.ContainsKey("over");
            foreach (var seat in GameData.Players)
            {
                bool p1 = seat.GetStringAttribute("type") == "p1";
                // Compact line down on the felt near the player, in front of the hand.
                double z = p1 ? -8.6 : 8.6;
                double roll = p1 ? 180 : 0;
                string label;
                if (over)
                    label = GameData.Attributes.GetValueOrDefault("result", "Game over");
                else if (Undefended().Count > 0)
                    label = seat.Id == Defender ? "YOU DEFENDING" : "";
                else
                    label = seat.Id == Attacker ? "YOU ATTACKING" : "";
                if (string.IsNullOrEmpty(label)) continue;   // waiting player: no status line
                var t = addTextItem(Assets.TEXT).SetText(label)
                    .SetPosition(0, 0.12, z).SetScale(0.42).SetRotation(-90, 0, roll)
                    .AddAttribute("statusText", "1")
                    .AddAttribute("textColor", "ffffff");
                t.Visible[seat.Id] = true;
            }
        }

        // ============================ helpers ============================
        private string Attacker => GameData.Attributes.GetValueOrDefault("attacker", "");
        private string Defender => GameData.Attributes.GetValueOrDefault("defender", "");
        private char Trump => GameData.Attributes.TryGetValue("trump", out var t) && t.Length > 0 ? t[0] : 'S';

        private string Name(string seatId)
        {
            var p = GameData.Players.Find(x => x.Id == seatId);
            return p != null ? PlayerDisplayName(p) : "?";
        }

        private static int Val(DurakRules.Card c, char trump) => c.Rank + (c.Suit == trump ? 100 : 0);

        private static DurakRules.Card ParseCode(string code)
            => new DurakRules.Card(int.Parse(code.Substring(0, code.Length - 1)), code[^1]);

        private List<DurakRules.Card> GetHand(string seatId)
        {
            var s = GameData.Attributes.GetValueOrDefault("hand:" + seatId, "");
            return string.IsNullOrEmpty(s) ? new List<DurakRules.Card>()
                : s.Split(',').Select(ParseCode).ToList();
        }
        private void SetHand(string seatId, List<DurakRules.Card> cards)
            => GameData.Attributes["hand:" + seatId] = string.Join(",", cards.Select(c => c.Code));

        private List<DurakRules.Card> GetDeck()
        {
            var s = GameData.Attributes.GetValueOrDefault("deck", "");
            return string.IsNullOrEmpty(s) ? new List<DurakRules.Card>()
                : s.Split(',').Select(ParseCode).ToList();
        }
        private void SetDeck(List<DurakRules.Card> deck)
            => GameData.Attributes["deck"] = string.Join(",", deck.Select(c => c.Code));

        private static DurakRules.Card DrawTop(List<DurakRules.Card> deck)
        {
            var c = deck[^1]; deck.RemoveAt(deck.Count - 1); return c;
        }

        // field encoded as "att" or "att>def", entries comma-separated
        private List<(DurakRules.Card att, DurakRules.Card? def)> GetField()
        {
            var s = GameData.Attributes.GetValueOrDefault("field", "");
            var list = new List<(DurakRules.Card, DurakRules.Card?)>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var e in s.Split(','))
            {
                var parts = e.Split('>');
                var att = ParseCode(parts[0]);
                DurakRules.Card? def = parts.Length > 1 && parts[1].Length > 0 ? ParseCode(parts[1]) : (DurakRules.Card?)null;
                list.Add((att, def));
            }
            return list;
        }
        private void SetField(List<(DurakRules.Card att, DurakRules.Card? def)> field)
            => GameData.Attributes["field"] = string.Join(",", field.Select(p => p.def == null ? p.att.Code : $"{p.att.Code}>{p.def.Value.Code}"));

        private List<string> Undefended() => GetField().Where(p => p.def == null).Select(p => p.att.Code).ToList();

        private AssetData CardAsset(DurakRules.Card c) => addAsset(new TokenAssetData(DurakRules.FrontUrl(c), BackUrl()));
        private AssetData BackAsset() => addAsset(new TokenAssetData(BackUrl(), BackUrl()));

        // Suit-symbol token (♠♥♦♣) used as the trump indicator by the deck.
        private static string SuitSymbolFile(char suit) => suit switch
        {
            'C' => "common/suits/club.png",
            'D' => "common/suits/diamond.png",
            'H' => "common/suits/heart.png",
            _   => "common/suits/spade.png",
        };
        private AssetData SuitAsset(char suit) => addAsset(new TokenAssetData(SuitSymbolFile(suit), SuitSymbolFile(suit)));

        // Rounded plate behind the action label so the WHOLE button is clickable.
        private AssetData ButtonBgAsset() => addAsset(new TokenAssetData("common/suits/button_bg.png", "common/suits/button_bg.png"));

        private string BackUrl()
        {
            var key = GameData.Attributes.TryGetValue("cardBack", out var v) ? v : "red";
            return key switch
            {
                "blue" => "common/back/blue-57.jpg",
                "green" => "common/back/green-15.jpg",
                "brown" => "common/back/brown-14.jpg",
                _ => "common/back/red-56.jpg",
            };
        }
    }
}
