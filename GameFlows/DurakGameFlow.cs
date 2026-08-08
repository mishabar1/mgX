using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Durak (Дурак) — first slice: deal a real hand and show it.
    // 36-card deck, trump from the bottom card, 6 cards dealt to each of 2 players. Both
    // hands + the draw pile + the trump card are rendered. Attack/defend interaction is the
    // NEXT slice — this one is about getting the deal and the layout right.
    public class DurakGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("durak");
            internal static AssetData TABLE = new TokenAssetData("durak/table.png");
        }

        // z-rows where each player's hand is laid out (near their seat).
        private const double P1_Z = -3.4;
        private const double P2_Z = 3.4;
        private const double CARD_SCALE = 1.3;
        private const double HAND_SPACING = 0.95;

        public DurakGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DURAK;
        }

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
            // Register all 36 card faces with the currently-chosen back so any card can render.
            foreach (var s in DurakRules.Suits)
                foreach (var r in DurakRules.Ranks)
                    CardAsset(new DurakRules.Card(r, s));

            // Green octagon felt table as the play surface (just below the cards).
            addItem(Assets.TABLE).SetPosition(0, -0.05, 0).SetScale(20, 1, 20);

            var rnd = new Random();
            var deck = DurakRules.BuildDeck();
            DurakRules.Shuffle(deck, rnd);

            // The bottom card (index 0) is the trump; we draw from the top (end of the list).
            var trump = deck[0];
            GameData.Attributes["trump"] = trump.Suit.ToString();
            GameData.Attributes["trumpCard"] = trump.Code;

            // Deal 6 to each player, alternating.
            var p1 = GameData.Players[0];
            var p2 = GameData.Players[1];
            var p1cards = new List<DurakRules.Card>();
            var p2cards = new List<DurakRules.Card>();
            for (int i = 0; i < DurakRules.HandSize; i++)
            {
                p1cards.Add(DrawTop(deck));
                p2cards.Add(DrawTop(deck));
            }

            // Remaining undealt cards (index 0 = bottom trump, end = next to draw).
            GameData.Attributes["deck"] = string.Join(",", deck.Select(c => c.Code));
            GameData.Attributes["attacker"] = "p1";

            // Cards go into each player's HAND zone. Each card is ONE item, made visible only
            // to its owner via the item's own Visible property — so nobody else's client
            // renders it, from any camera angle.
            RenderHand(p1cards, p1);
            RenderHand(p2cards, p2);
            RenderDeckAndTrump(deck.Count, trump);

            // A private, correctly-oriented status label in front of each player.
            RenderText(deck.Count, trump, p1, -5.0, 180);
            RenderText(deck.Count, trump, p2, 5.0, 0);

            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(false); // no win condition yet
        protected override List<PlayerData> GetGameWinners() => new List<PlayerData>();

        // ------------------------------------------------------------------
        private static DurakRules.Card DrawTop(List<DurakRules.Card> deck)
        {
            var c = deck[^1];
            deck.RemoveAt(deck.Count - 1);
            return c;
        }

        // The chosen card back — a per-game setting, defaults to red.
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

        // Get (adding if needed) the TOKEN asset for a card. Names are deterministic, so
        // addAsset is idempotent.
        private AssetData CardAsset(DurakRules.Card c)
            => addAsset(new TokenAssetData(DurakRules.FrontUrl(c), BackUrl()));

        // A "hidden card": both faces are the card back, so it never reveals a face from any
        // angle. Used for opponents' hand slots and the draw pile.
        private AssetData BackAsset()
            => addAsset(new TokenAssetData(BackUrl(), BackUrl()));

        private ItemData AddCard(DurakRules.Card c)
        {
            return addItem(CardAsset(c))
                .SetScale(CARD_SCALE)
                .AddAttribute("card", "1")
                .AddAttribute("rank", c.Rank.ToString())
                .AddAttribute("suit", c.Suit.ToString())
                .AddAttribute("code", c.Code);
        }

        // Lay out a player's hand in their HAND zone (attached to their avatar). One two-sided
        // card each — the owner sees the face, opponents see the back, thanks to the token
        // geometry + the hand's orientation. Positions are LOCAL to the hand.
        private void RenderHand(List<DurakRules.Card> cards, PlayerData owner)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                double x = (i - (cards.Count - 1) / 2.0) * HAND_SPACING;

                // ONE item per card. The "owner" attribute (the seat id) tells every client
                // to draw the FACE only for the owner and the BACK for everyone else — so
                // opponents always see a back, from any angle, with a single card.
                addItemToPlayerHand(owner, CardAsset(cards[i]))
                    .SetPosition(x, 0, 0)
                    .SetScale(CARD_SCALE)
                    .AddAttribute("card", "1")
                    .AddAttribute("rank", cards[i].Rank.ToString())
                    .AddAttribute("suit", cards[i].Suit.ToString())
                    .AddAttribute("code", cards[i].Code)
                    .AddAttribute("ownerType", owner.GetStringAttribute("type"))
                    .AddAttribute("owner", owner.Id);
            }
        }

        private void RenderDeckAndTrump(int deckCount, DurakRules.Card trump)
        {
            // Trump card laid sideways, face up, at the draw-pile spot.
            AddCard(trump)
                .SetPosition(5.4, 0.01, 0)
                .SetRotation(0, 90, 0)
                .AddAttribute("trumpCard", "1");

            // A single faceless-back card standing in for the draw pile — no real face to
            // reveal if the camera orbits under it.
            if (deckCount > 1)
            {
                addItem(BackAsset())
                    .SetPosition(5.0, 0.06, 0)
                    .SetScale(CARD_SCALE)
                    .AddAttribute("deckPile", "1");
            }
        }

        // A status label placed in front of one player, oriented to read from their side,
        // and visible only to them.
        private void RenderText(int deckCount, DurakRules.Card trump, PlayerData who, double z, double roll)
        {
            string label = $"TRUMP: {DurakRules.SuitName(trump.Suit).ToUpper()}   DECK: {deckCount}";
            var t = addTextItem(Assets.TEXT)
                .SetText(label)
                .SetPosition(0, 0.12, z)
                .SetScale(0.6)
                .SetRotation(-90, 0, roll)   // flat on the table, rolled to face this player
                .AddAttribute("durakText", "1");
            t.Visible[who.Id] = true;
        }
    }
}
