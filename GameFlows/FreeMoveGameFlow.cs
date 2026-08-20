using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    /// <summary>
    /// Base for games that want the virtual-tabletop interaction model: click a piece to select
    /// it, then click the map/board to drop it at the clicked point. No rule enforcement — the
    /// players self-enforce, exactly like a physical tabletop. Used by D&amp;D and Demo.
    ///
    /// WHY THIS CLASS EXISTS
    /// ---------------------
    /// <see cref="GameActionAttribute"/> is <c>Inherited = true</c>, so a <c>[GameAction]</c>
    /// declared on <see cref="BaseGameFlow"/> is dispatchable in EVERY game. SelectPiece and
    /// MoveHere used to live there, and MoveHere writes <c>piece.Position</c> straight from the
    /// client-supplied point with no turn check, no ownership check and no rules. That handed
    /// every one of the 14 games a "teleport any piece anywhere, at any time" action nobody
    /// asked for.
    ///
    /// The mechanics still live on BaseGameFlow (as <c>SelectPieceCore</c> / <c>MoveHereCore</c>,
    /// which Chess reuses inside its own rule-checked ChessSelect). Only the DISPATCHABLE
    /// wrappers moved here, so a game opts in by inheriting this instead of BaseGameFlow.
    ///
    /// Note the actions are still only reachable on items the game actually bound them to:
    /// BaseGameFlow.DispatchAction checks the clicked item offers the action to that seat.
    /// </summary>
    public abstract class FreeMoveGameFlow : BaseGameFlow
    {
        protected FreeMoveGameFlow(GameData gameData) : base(gameData) { }

        /// <summary>Make an item selectable so it can be picked up and moved.</summary>
        protected void makeMovable(ItemData piece)
        {
            piece.AddAction(SelectPiece);
        }

        /// <summary>Make an item (board/map) a surface that moves the selected piece to the click point.</summary>
        protected void makeMoveSurface(ItemData surface)
        {
            surface.AddAction(MoveHere);
        }

        [GameAction]
        public Task SelectPiece(ExecuteActionData data) => SelectPieceCore(data);

        [GameAction]
        public Task MoveHere(ExecuteActionData data) => MoveHereCore(data);
    }
}
