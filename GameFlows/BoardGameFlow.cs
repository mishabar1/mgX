using System;
using System.Collections.Generic;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Shared base for turn-based board games (chess, checkers, reversi, gomoku).
    // Holds the boilerplate they all repeat: turn resolution from the "turn" attribute,
    // the "over"/"winnerColor"/"result" end-game model, and name capitalisation.
    // Each game still owns its board layout, assets, rules and rendering.
    public abstract class BoardGameFlow : BaseGameFlow
    {
        protected BoardGameFlow(GameData gameData) : base(gameData) { }

        // Games track whose turn it is via the "turn" attribute (a colour / side string).
        protected string Turn => GameData.Attributes.TryGetValue("turn", out var t) ? t : "";

        // Resolve the seat whose turn it is (used by undo to rewind past AI moves).
        protected override PlayerData? CurrentTurnPlayer()
            => GameData.Attributes.TryGetValue("turn", out var t) ? getPlayerByAttribute("type", t) : null;

        // Default end model: the game sets "over" + "winnerColor"/"result" when it detects the
        // end. (Chess overrides these to recompute checkmate/stalemate from the board instead.)
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));

        protected override List<PlayerData> GetGameWinners()
        {
            if (GameData.Attributes.TryGetValue("winnerColor", out var wc) && !string.IsNullOrEmpty(wc))
            {
                var p = getPlayerByAttribute("type", wc);
                if (p != null) return new List<PlayerData> { p };
            }
            return new List<PlayerData>();
        }

        /// <summary>
        /// True when the acting caller controls the seat that is to move.
        ///
        /// Replaces the `current.User != null && data.Player.User?.Id != current.User.Id` form
        /// that was copy-pasted into each of these games. That version SKIPPED the check entirely
        /// whenever the seat to move had no user — i.e. whenever it was an AI's turn — so in any
        /// human-vs-AI game the human could play the AI's moves. ControlsSeat returns false for a
        /// seat with no user, while still allowing hotseat (one user holding both colours).
        /// </summary>
        protected bool CallerToMove(ExecuteActionData data) => ControlsSeat(data, CurrentTurnPlayer());

        protected static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
