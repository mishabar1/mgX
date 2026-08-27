using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Durak (Дурак) — 2..6 player attack/defend around an octagon table.
    // State lives entirely in GameData.Attributes (hands, deck, field, roles) so the scene can
    // be rebuilt from scratch on every action.
    //
    // Seats: 8 are created (a ring around the table); only OCCUPIED seats (HUMAN/AI) play, and
    // just 2 are mandatory (MinPlayers = 2). Turn order follows the seats around the ring.
    //
    // Flow (simplified podkidnoy, one undefended card at a time): the attacker plays a card; the
    // defender (next player round the ring) must beat it (higher same-suit, or a trump) or Take.
    // Once beaten, the attacker OR any other non-defender player may throw in another card whose
    // rank is already on the table; the primary attacker presses Done to end the bout. Cards are
    // discarded and the turn advances (a successful defender becomes the next attacker; a defender
    // who Took is skipped). After each bout everyone refills to 6 (attacker first, defender last).
    // When the deck is empty, a player who empties their hand is out (safe); the last player still
    // holding cards is the Durak (loser).
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
        private const int SEATS = 6;   // 6 × 6 cards = the full 36-card deck

        // Only two players are required; the rest of the ring is optional.
        public override int MinPlayers => 2;

        // ===================== SECRECY =====================
        // Durak's hands live in GameData.Attributes ("hand:<seatId>") and used to be broadcast to
        // everyone: the client drew the backs, but the wire carried every card. Now each viewer
        // gets their own redacted copy — and the card ART is swapped server-side too, so a hand a
        // viewer may not see never reaches them as a URL, a code, or a clickable action.
        public override bool HasHiddenInfo => true;

        public override void RedactFor(GameData view, string? userId)
        {
            // Once it is over, everything is public — the loser's hand is the reveal.
            if (view.GameStatus == GameStatusEnum.ENDED || view.Attributes?.ContainsKey("over") == true) return;

            var mine = SeatIdsOf(view, userId);

            // Your own hand only. The deck order and the (face-down) discard are nobody's to see.
            RedactOtherSeatKeys(view, new[] { "hand:" }, mine);
            RedactAllKeys(view, new[] { "deck", "discard" });

            // The hand now lives in the ITEM TREE as a HAND-anchored holder. Another player's
            // client already refuses to draw it (a hand/camera anchor is private to its owner), but
            // that is a rendering rule, not secrecy — the cards would still be on the wire. So strip
            // them here: swap every card in someone else's holder to the back asset and remove what
            // would name it regardless of the picture.
            var backKey = BackAsset().Name;
            foreach (var holder in HoldersOf(view))
            {
                if (holder.Owner == null || mine.Contains(holder.Owner)) continue;
                SwapItems(holder, backKey!, i => i.GetStringAttribute("card") == "1", "code", "playable");
            }
        }

        public DurakGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DURAK;
        }

        // ============================ lifecycle ============================
        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            addAsset(Assets.TABLE);

            GameData.Attributes["usesCardBack"] = "1";   // offer the card-back chooser in setup

            GameData.Observer.Position.Set(0, 15, 0);

            // Six empty seats evenly spaced around the ring. Seat 0 is the near side (-Z);
            // each seat carries its ring "angle" (degrees) so the per-seat UI can be placed.
            const int Ra = 9;   // avatar ring radius
            const int Rc = 12;  // camera ring radius
            for (int i = 0; i < SEATS; i++)
            {
                double deg = i * (360.0 / SEATS);
                double t = deg * Math.PI / 180.0;
                int ax = (int)Math.Round(Ra * Math.Sin(t));
                int az = (int)Math.Round(-Ra * Math.Cos(t));
                int cx = (int)Math.Round(Rc * Math.Sin(t));
                int cz = (int)Math.Round(-Rc * Math.Cos(t));

                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .AddAttribute("angle", ((int)Math.Round(deg)).ToString(CultureInfo.InvariantCulture))
                    .SetCameraPosition(cx, 9, cz)
                    .SetAvatarPosition(ax, 2, az);
            }

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        // Reposition the OCCUPIED seats evenly around the ring (360/N apart), so 2, 3, … players
        // are spread across the table instead of clustered in the fixed 6-slot layout. Updates
        // each seat's camera, avatar and "angle" (which drives its hand/buttons/status placement).
        private void RespaceSeats()
        {
            var occ = GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).ToList();
            int n = occ.Count;
            if (n == 0) return;
            const int Ra = 9;   // avatar ring radius
            const int Rc = 12;  // camera ring radius
            for (int i = 0; i < n; i++)
            {
                double deg = i * (360.0 / n);
                double t = deg * Math.PI / 180.0;
                int ax = (int)Math.Round(Ra * Math.Sin(t));
                int az = (int)Math.Round(-Ra * Math.Cos(t));
                int cx = (int)Math.Round(Rc * Math.Sin(t));
                int cz = (int)Math.Round(-Rc * Math.Cos(t));
                occ[i].SetCameraPosition(cx, 9, cz).SetAvatarPosition(ax, 2, az);
                occ[i].Attributes["angle"] = ((int)Math.Round(deg)).ToString(CultureInfo.InvariantCulture);
            }
        }

        protected override Task StartGame()
        {
            RespaceSeats(); // spread the actual players evenly around the ring
            var rnd = new Random();
            var deck = DurakRules.BuildDeck();
            DurakRules.Shuffle(deck, rnd);

            var trump = deck[0];                    // bottom card is the trump
            GameData.Attributes["trump"] = trump.Suit.ToString();
            GameData.Attributes["trumpCard"] = trump.Code;

            // Pre-register ALL 36 card faces (+ back, trump symbol, button plate) so every asset
            // the client will need is present from the first load — no later "unknown asset key".
            foreach (var s in DurakRules.Suits)
                foreach (var r in DurakRules.Ranks)
                    CardAsset(new DurakRules.Card(r, s));
            BackAsset();
            SuitAsset(trump.Suit);
            ButtonBgAsset();

            // Deal 6 to every occupied seat.
            var seats = Occupied();
            for (int i = 0; i < DurakRules.HandSize; i++)
                foreach (var id in seats)
                {
                    var h = GetHand(id);
                    h.Add(DrawTop(deck));
                    SetHand(id, h);
                }
            SetDeck(deck);

            GameData.Attributes["field"] = "";
            GameData.Attributes["discard"] = "0";
            // First occupied seat opens; the next occupied seat defends.
            string first = seats.FirstOrDefault() ?? "";
            GameData.Attributes["attacker"] = first;
            GameData.Attributes["defender"] = NextInPlay(first);
            GameData.Attributes.Remove("over");
            GameData.Attributes.Remove("result");
            GameData.Attributes.Remove("winnerIds");
            foreach (var id in seats) GameData.Attributes.Remove("out:" + id);

            Render();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));

        // Undo restores our attributes (hands/deck/field/roles); re-render so the scene —
        // including each player's hand — reflects the reverted state.
        protected override void AfterUndo() => Render();

        protected override List<PlayerData> GetGameWinners()
        {
            var ids = GameData.Attributes.GetValueOrDefault("winnerIds", "");
            if (string.IsNullOrEmpty(ids)) return new List<PlayerData>();
            var set = ids.Split(',').ToHashSet();
            return GameData.Players.Where(p => set.Contains(p.Id)).ToList();
        }

        // ============================ player actions ============================
        /// <summary>
        /// Which card was played. A panel activation carries no clicked board item, so the code now
        /// comes through args; the d.Item path is kept as a fallback so anything still binding a
        /// click to a board card keeps working.
        /// </summary>
        private static string CodeOf(ExecuteActionData d)
        {
            var fromArgs = d?.args != null && d.args.TryGetValue("code", out var v) ? v : null;
            if (!string.IsNullOrEmpty(fromArgs)) return fromArgs!;
            return d?.Item?.GetStringAttribute("code") ?? "";
        }

        [GameAction] public async Task AttackCard(ExecuteActionData d) { DoAttack(d.Player!.Id, CodeOf(d)); Render(); await Task.CompletedTask; }
        [GameAction] public async Task DefendCard(ExecuteActionData d) { DoDefend(d.Player!.Id, CodeOf(d)); Render(); await Task.CompletedTask; }
        [GameAction] public async Task TakeCards(ExecuteActionData d) { DoTake(d.Player!.Id); Render(); await Task.CompletedTask; }
        [GameAction] public async Task DoneAttack(ExecuteActionData d) { DoDone(d.Player!.Id); Render(); await Task.CompletedTask; }

        // ============================ AI ============================
        // Only the primary attacker and the current defender ever take an AI turn; co-attacker
        // throw-ins are left to humans, so the bout always ends on the attacker's Done.
        public override bool IsAITurn(PlayerData player)
        {
            if (GameData.Attributes.ContainsKey("over")) return false;
            return Undefended().Count > 0 ? player.Id == Defender : player.Id == Attacker;
        }

        // Whose turn is it (the defender when there's a card to beat, otherwise the attacker)?
        // Used by undo to rewind past AI moves back to a human.
        protected override PlayerData? CurrentTurnPlayer()
        {
            string id = Undefended().Count > 0 ? Defender : Attacker;
            return GameData.Players.FirstOrDefault(p => p.Id == id);
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
            if (Undefended().Count > 0) return;                 // wait until the current card is beaten
            if (!InPlay(actorId) || actorId == Defender) return;
            var field = GetField();
            if (field.Count >= 6) return;
            if (field.Count == 0 && actorId != Attacker) return; // only the primary attacker opens
            if (GetHand(Defender).Count == 0) return;            // nothing to throw at an empty-handed defender
            var hand = GetHand(actorId);
            var card = ParseCode(code);
            if (!hand.Any(c => c.Code == code)) return;
            if (field.Count > 0)
            {
                var ranks = field.SelectMany(p => p.def == null ? new[] { p.att.Rank } : new[] { p.att.Rank, p.def.Value.Rank }).ToHashSet();
                if (!ranks.Contains(card.Rank)) return;          // throw-in must match a rank on the table
            }
            SaveUndoPoint();
            hand.RemoveAll(c => c.Code == code);
            field.Add((card, null));
            SetHand(actorId, hand);
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
            SaveUndoPoint();
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
            SaveUndoPoint();
            var oldAtt = Attacker; var oldDef = Defender;
            var hand = GetHand(oldDef);
            foreach (var (att, def) in field) { hand.Add(att); if (def != null) hand.Add(def.Value); }
            SetHand(oldDef, hand);
            SetField(new List<(DurakRules.Card, DurakRules.Card?)>());
            RefillFrom(oldAtt, oldDef);        // attacker & others refill; defender kept the cards
            // Defender is loaded up and skipped: the player after them attacks next.
            string na = NextInPlay(oldDef);
            GameData.Attributes["attacker"] = na;
            GameData.Attributes["defender"] = NextInPlay(na);
            EndCheck();
        }

        private void DoDone(string actorId)
        {
            if (GameData.Attributes.ContainsKey("over")) return;
            if (actorId != Attacker) return;
            var field = GetField();
            if (field.Count == 0 || field.Any(p => p.def == null)) return; // only when all beaten
            SaveUndoPoint();
            int discarded = int.Parse(GameData.Attributes.GetValueOrDefault("discard", "0"));
            discarded += field.Count * 2;
            GameData.Attributes["discard"] = discarded.ToString();
            SetField(new List<(DurakRules.Card, DurakRules.Card?)>());
            var oldAtt = Attacker; var oldDef = Defender;
            RefillFrom(oldAtt, oldDef);
            // Successful defender becomes the next attacker (or the next player, if they went out).
            string na = InPlay(oldDef) ? oldDef : NextInPlay(oldDef);
            GameData.Attributes["attacker"] = na;
            GameData.Attributes["defender"] = NextInPlay(na);
            EndCheck();
        }

        // Refill to 6 in turn order: attacker first, everyone else round the ring, defender last.
        private void RefillFrom(string firstAttacker, string defender)
        {
            var occ = Occupied();
            if (occ.Count == 0) return;
            int start = Math.Max(0, occ.IndexOf(firstAttacker));
            var order = new List<string>();
            for (int k = 0; k < occ.Count; k++)
            {
                var id = occ[(start + k) % occ.Count];
                if (id != defender) order.Add(id);
            }
            if (occ.Contains(defender)) order.Add(defender);

            var deck = GetDeck();
            foreach (var id in order)
            {
                var hand = GetHand(id);
                while (hand.Count < DurakRules.HandSize && deck.Count > 0) hand.Add(DrawTop(deck));
                SetHand(id, hand);
            }
            SetDeck(deck);
        }

        private void EndCheck()
        {
            if (GetDeck().Count > 0) return; // still cards to draw — game continues

            // Deck empty: seats that emptied their hand are out (safe). Mark them.
            var occ = Occupied();
            foreach (var id in occ)
                if (GetHand(id).Count == 0) GameData.Attributes["out:" + id] = "1";

            var withCards = occ.Where(id => GetHand(id).Count > 0).ToList();
            if (withCards.Count > 1) return; // game continues among those still holding cards

            string result; List<string> winners;
            if (withCards.Count == 0)
            {
                result = "Draw!";
                winners = occ.ToList();               // everyone emptied together
            }
            else
            {
                var durak = withCards[0];
                result = $"{Name(durak)} is the DURAK!";
                winners = occ.Where(id => id != durak).ToList();
            }
            GameData.Attributes["over"] = "1";
            GameData.Attributes["result"] = result;
            GameData.Attributes["winnerIds"] = string.Join(",", winners);
        }

        // ============================ rendering ============================
        private void Render()
        {
            GameData.Table = ItemData.Table();
            foreach (var p in GameData.Players)
            {
                p.Hand = new ItemData("", null) { Name = "PLAYER HAND" };
                p.Screen = null;   // rebuilt below by RenderHand for any seat holding cards
            }

            addItem(Assets.TABLE).SetPosition(0, -0.05, 0).SetScale(20, 1, 20);

            bool over = GameData.Attributes.ContainsKey("over");
            var field = GetField();
            int undef = field.Count(p => p.def == null);
            int defenderCards = string.IsNullOrEmpty(Defender) ? 0 : GetHand(Defender).Count;

            foreach (var seat in GameData.Players)
            {
                if (seat.Type == PlayerTypeEnum.EMPTY_SEAT) continue;

                bool canDefend = !over && undef > 0 && seat.Id == Defender;
                bool canAttack = !over && undef == 0 && InPlay(seat.Id) && seat.Id != Defender
                                 && defenderCards > 0
                                 && (seat.Id == Attacker || field.Count > 0); // opener opens; others throw in
                HashSet<string>? playable = canAttack ? LegalAttacks(seat.Id)
                                          : canDefend ? LegalDefends(seat.Id)
                                          : null;
                RenderHand(seat, canAttack ? nameof(AttackCard) : canDefend ? nameof(DefendCard) : null, playable);
            }

            RenderField(field);
            RenderDeckAndTrump();

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
            if (cards.Count == 0) return;
            // Tidy the fan: group by suit, ascending rank, trump suit last.
            char trump = Trump;
            cards = cards
                .OrderBy(c => c.Suit == trump ? 1 : 0)
                .ThenBy(c => c.Suit)
                .ThenBy(c => c.Rank)
                .ToList();
            // THE HAND IS A HOLDER — not a player.Hand zone, and not a uikit panel either.
            //
            // Anchor = HAND, which means: the owner's LEFT CONTROLLER in VR, and their CAMERA
            // outside it (mg.game resolves that, and re-resolves it when the XR session changes).
            // So the hand is a private HUD across the bottom of your view that becomes something you
            // physically hold in a headset — and nobody else's client draws it at all.
            //
            // Every card's position is set HERE. The client parents the holder and applies these
            // transforms; it measures and arranges nothing, so the hand cannot reflow or resize.
            const double CARD_W = 0.23;    // spacing between cards, in world units
            const double CARD_S = 0.30;    // card scale (a card is ~1 unit, so this is its height)
            const double FAN = 4.0;        // degrees of fan per card
            // A TOKEN card's printed face is its +Y (TOP) face — cards are built to lie face-up on a
            // table, which is why flat field cards read correctly from an overhead camera. Held in
            // front of the eye it must be STOOD UP so that +Y points at the viewer (+Z in camera
            // space): Rx(+90) maps (0,1,0) to (0,0,1) exactly. +84 leaves a slight backward lean,
            // the way a real hand sits.
            //
            // The SIGN matters and is easy to get backwards: Rx(-84) turns the face AWAY and you see
            // the card's back (its -Y side, which carries the back texture). If this ever shows
            // backs, flip this one number.
            const double CARD_TILT = 84.0;

            var hand = addHolder(ItemAnchorEnum.HAND, seat)
                .SetPosition(0, -0.62, -1.35)   // camera space: low and centred, just off the bottom
                .SetRotation(-6, 0, 0);         // a touch of tilt; the cards supply the rest

            double mid = (cards.Count - 1) / 2.0;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                bool can = playable != null && playable.Contains(c.Code) && clickAction != null;

                // Rotation order is XYZ (R = Rx·Ry·Rz), so Y is applied BEFORE the X tilt — i.e. the
                // fan is a spin in the card's own plane, which is what fans a hand. Putting the fan
                // on Z instead would lift one edge off the plane rather than rotate the card.
                var card = addItemTo(hand, CardAsset(c))
                    .SetPosition((i - mid) * CARD_W, 0, i * 0.001)   // a hair of Z so they layer cleanly
                    .SetRotation(CARD_TILT, (i - mid) * -FAN, 0)
                    .SetScale(CARD_S)
                    .AddAttribute("card", "1")
                    .AddAttribute("code", c.Code)
                    .AddAttribute("owner", seat.Id);

                if (can)
                {
                    card.AddAttribute("playable", "1");
                    card.ClickActions[seat.Id] = clickAction!;   // a board-item click: code comes off d.Item
                }
            }

            // ...and a PUBLIC row of backs lying on the felt in front of the seat, so the table can
            // still see HOW MANY cards you hold. Durak needs that — it is public information in the
            // real game, and the private hand above is invisible to everyone else.
            //
            // WORLD-anchored and placed from the seat's own ring position, NOT anchored to the
            // avatar: the avatar group is not turned to face the table, so a local offset would
            // land along the world axes and put another seat's row somewhere random.
            var av = seat.Avatar?.Position ?? new V3(0, 1, 9);
            var len = Math.Sqrt(av.X * av.X + av.Z * av.Z);
            var pull = len > 0.001 ? (len - 2.4) / len : 0;      // in off the seat, onto the felt
            var yaw = Math.Atan2(av.X, av.Z) * 180 / Math.PI;    // turn the row to face that seat

            var shown = addHolder(ItemAnchorEnum.WORLD, seat)
                .SetPosition(av.X * pull, 0.04, av.Z * pull)
                .SetRotation(0, yaw, 0);
            for (int i = 0; i < cards.Count; i++)
                addItemTo(shown, BackAsset())
                    .SetPosition((i - mid) * 0.55, 0, 0)          // flat on the felt, face up = a back
                    .SetScale(0.8)
                    .AddAttribute("cardback", "1");
        }

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

        private HashSet<string> LegalDefends(string seatId)
        {
            var set = new HashSet<string>();
            var undef = GetField().FirstOrDefault(p => p.def == null);
            if (undef.att.Rank == 0) return set;
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

            // Trump indicator: suit symbol flat on the felt beside the deck; shown all game.
            addItem(SuitAsset(Trump)).SetPosition(7.4, 0.02, 0).SetRotation(0, -90, 0).SetScale(1.6)
                .AddAttribute("trumpSuit", "1");

            if (deckCount > 0)
                addItem(BackAsset()).SetPosition(6.0, 0.06, 0).SetScale(CARD_SCALE).AddAttribute("deckPile", "1");
        }

        private void RenderButton(string label, string seatId, string action)
        {
            var seat = GameData.Players.Find(p => p.Id == seatId);
            if (seat == null) return;
            double deg = SeatAngle(seat);
            double t = deg * Math.PI / 180.0;
            double sin = Math.Sin(t), cos = Math.Cos(t);
            // 8.2 out toward the player + 3.2 to the side, oriented to that seat around the ring.
            double bx = 8.2 * sin + 3.2 * cos;
            double bz = -8.2 * cos + 3.2 * sin;
            double roll = 180 - deg;

            var plate = addItem(ButtonBgAsset()).SetPosition(bx, 0.05, bz).SetRotation(0, -deg, 0).SetScale(2.0)
                .AddAttribute("button", "1");
            plate.ClickActions[seatId] = action;
            plate.Visible[seatId] = true;

            addTextItem(Assets.TEXT).SetText(label)
                .SetPosition(bx, 0.14, bz).SetScale(0.45).SetRotation(-90, 0, roll)
                .AddAttribute("buttonLabel", "1")
                .AddAttribute("textColor", "ffffff")
                .Visible[seatId] = true;
        }

        private void RenderStatusText()
        {
            bool over = GameData.Attributes.ContainsKey("over");
            int undef = Undefended().Count;
            foreach (var seat in GameData.Players)
            {
                if (seat.Type == PlayerTypeEnum.EMPTY_SEAT) continue;

                string label;
                if (over)
                    label = GameData.Attributes.GetValueOrDefault("result", "Game over");
                else if (undef > 0)
                    label = seat.Id == Defender ? "YOU DEFENDING" : "";
                else
                    label = seat.Id == Attacker ? "YOU ATTACKING" : "";
                if (string.IsNullOrEmpty(label)) continue;

                double deg = SeatAngle(seat);
                double t = deg * Math.PI / 180.0;
                double sx = 8.6 * Math.Sin(t);
                double sz = -8.6 * Math.Cos(t);
                double roll = 180 - deg;

                var it = addTextItem(Assets.TEXT).SetText(label)
                    .SetPosition(sx, 0.12, sz).SetScale(0.42).SetRotation(-90, 0, roll)
                    .AddAttribute("statusText", "1")
                    .AddAttribute("textColor", "ffffff");
                it.Visible[seat.Id] = true;
            }
        }

        // ============================ helpers ============================
        private string Attacker => GameData.Attributes.GetValueOrDefault("attacker", "");
        private string Defender => GameData.Attributes.GetValueOrDefault("defender", "");
        private char Trump => GameData.Attributes.TryGetValue("trump", out var t) && t.Length > 0 ? t[0] : 'S';

        private static double SeatAngle(PlayerData seat)
            => int.TryParse(seat.GetStringAttribute("angle"), out var a) ? a : 0;

        // Occupied seats (HUMAN or AI), in ring order.
        private List<string> Occupied()
            => GameData.Players.Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT).Select(p => p.Id).ToList();

        // A seat still in the game: occupied and still holding cards.
        private bool InPlay(string id)
            => !string.IsNullOrEmpty(id)
               && GameData.Players.Any(p => p.Id == id && p.Type != PlayerTypeEnum.EMPTY_SEAT)
               && GetHand(id).Count > 0;

        // Next still-in-play seat after the given one, going round the ring.
        private string NextInPlay(string afterId)
        {
            var occ = Occupied();
            if (occ.Count == 0) return "";
            int start = occ.IndexOf(afterId);
            if (start < 0) start = 0;
            for (int k = 1; k <= occ.Count; k++)
            {
                var cand = occ[(start + k) % occ.Count];
                if (cand != afterId && InPlay(cand)) return cand;
            }
            return "";
        }

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

        private static string SuitSymbolFile(char suit) => suit switch
        {
            'C' => "common/suits/club.png",
            'D' => "common/suits/diamond.png",
            'H' => "common/suits/heart.png",
            _   => "common/suits/spade.png",
        };
        private AssetData SuitAsset(char suit) => addAsset(new TokenAssetData(SuitSymbolFile(suit), SuitSymbolFile(suit)));

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
