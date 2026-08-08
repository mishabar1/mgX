using System;
using System.Collections.Generic;

namespace MG.Server.GameFlows
{
    // Pure Durak (Дурак) engine — no game-entity deps, so it's unit-testable.
    // 36-card deck: ranks 6..10, J(11), Q(12), K(13), A(14) in four suits.
    // Suits: 'C' clubs, 'D' diamonds, 'H' hearts, 'S' spades.
    // A trump suit is chosen from the bottom card of the deck. A defending card beats an
    // attacking card if it's the same suit and higher rank, or a trump beating a non-trump.
    public static class DurakRules
    {
        public const int HandSize = 6;
        public static readonly char[] Suits = { 'C', 'D', 'H', 'S' };
        public static readonly int[] Ranks = { 6, 7, 8, 9, 10, 11, 12, 13, 14 };

        // A card is just a rank + suit; game state lives as scene items, not as these structs.
        public struct Card
        {
            public int Rank;
            public char Suit;
            public Card(int rank, char suit) { Rank = rank; Suit = suit; }
            public string Code => $"{Rank}{Suit}"; // e.g. "14S" (ace of spades), "6H"
        }

        public static List<Card> BuildDeck() => BuildDeck(6);

        // Build a deck from minRank..Ace. minRank 6 → the standard 36-card Durak deck (up to 6
        // players); minRank 2 → a full 52-card deck so 7–8 players can each get 6 cards.
        public static List<Card> BuildDeck(int minRank)
        {
            var deck = new List<Card>(52);
            foreach (var s in Suits)
                for (int r = minRank; r <= 14; r++)
                    deck.Add(new Card(r, s));
            return deck;
        }

        // Fisher–Yates shuffle in place.
        public static void Shuffle(List<Card> deck, Random rnd)
        {
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        /// <summary>Does <paramref name="defender"/> beat <paramref name="attacker"/> given the trump suit?</summary>
        public static bool Beats(Card defender, Card attacker, char trump)
        {
            if (defender.Suit == attacker.Suit) return defender.Rank > attacker.Rank;
            return defender.Suit == trump && attacker.Suit != trump;
        }

        // ---- display helpers (map to the card art in assets/games/common/PNG-cards) ----

        public static string RankName(int rank) => rank switch
        {
            11 => "jack",
            12 => "queen",
            13 => "king",
            14 => "ace",
            _ => rank.ToString(), // 6..10
        };

        public static string SuitName(char suit) => suit switch
        {
            'C' => "clubs",
            'D' => "diamonds",
            'H' => "hearts",
            'S' => "spades",
            _ => "spades",
        };

        // e.g. Card(11,'H') -> "common/PNG-cards/jack_of_hearts2.png"
        // Face cards (J/Q/K) use the fancier illustrated "2" artwork; numbers & aces stay plain.
        public static string FrontUrl(Card c)
        {
            string suffix = (c.Rank >= 11 && c.Rank <= 13) ? "2" : "";
            return $"common/PNG-cards/{RankName(c.Rank)}_of_{SuitName(c.Suit)}{suffix}.png";
        }

        public const string BackUrl = "common/card_back.png";
    }
}
