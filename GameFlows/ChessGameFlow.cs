using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // Virtual-tabletop chess: full board + 32 pieces you can move freely (no rule
    // enforcement, no auto-win — players self-enforce, like Tabletop Simulator).
    public class ChessGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            // The client normalizes every model so max(width,depth) == asset.scale.
            // Pieces sit on x/z from -3.5..3.5 (8 squares), so the board must span ~8 units.
            internal static AssetData BOARD = new ObjectAssetData("chess/board.glb") { Scale = new V3(8) };

            internal static AssetData KING_W = new ObjectAssetData("chess/king_w.gltf");
            internal static AssetData QUEEN_W = new ObjectAssetData("chess/queen_w.gltf");
            internal static AssetData ROOK_W = new ObjectAssetData("chess/rook_w.gltf");
            internal static AssetData BISHOP_W = new ObjectAssetData("chess/bishop_w.gltf");
            internal static AssetData KNIGHT_W = new ObjectAssetData("chess/knight_white.glb"); // only .glb exists for white knight
            internal static AssetData PAWN_W = new ObjectAssetData("chess/pawn_w.gltf");

            internal static AssetData KING_B = new ObjectAssetData("chess/king_b.gltf");
            internal static AssetData QUEEN_B = new ObjectAssetData("chess/queen_b.gltf");
            internal static AssetData ROOK_B = new ObjectAssetData("chess/rook_b.gltf");
            internal static AssetData BISHOP_B = new ObjectAssetData("chess/bishop_b.gltf");
            internal static AssetData KNIGHT_B = new ObjectAssetData("chess/knight_black.gltf");
            internal static AssetData PAWN_B = new ObjectAssetData("chess/pawn_b.gltf");
        }

        public ChessGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.CHESS;
        }

        protected override Task Create()
        {
            addAsset(Assets.BOARD);
            addAsset(Assets.KING_W); addAsset(Assets.QUEEN_W); addAsset(Assets.ROOK_W);
            addAsset(Assets.BISHOP_W); addAsset(Assets.KNIGHT_W); addAsset(Assets.PAWN_W);
            addAsset(Assets.KING_B); addAsset(Assets.QUEEN_B); addAsset(Assets.ROOK_B);
            addAsset(Assets.BISHOP_B); addAsset(Assets.KNIGHT_B); addAsset(Assets.PAWN_B);

            GameData.Observer.Position.Set(0, 10, 0);

            // two seats: white and black
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "white")
                .SetCameraPosition(0, 5, -6)
                .SetAvatarPosition(0, 2, -5);

            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "black")
                .SetCameraPosition(0, 5, 6)
                .SetAvatarPosition(0, 2, 5);

            return Task.CompletedTask;
        }

        protected override Task Setup()
        {
            // Nothing to reset for chess; the board and pieces are placed on StartGame.
            return Task.CompletedTask;
        }

        protected override Task StartGame()
        {
            // The board is the surface you click to drop the selected piece.
            makeMoveSurface(addItem(Assets.BOARD).SetPosition(0, 0, 0));

            // files a..h mapped to x = -3.5 .. 3.5 (one unit per square, centered on origin)
            double[] files = { -3.5, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5, 3.5 };

            AssetData[] whiteBack = { Assets.ROOK_W, Assets.KNIGHT_W, Assets.BISHOP_W, Assets.QUEEN_W, Assets.KING_W, Assets.BISHOP_W, Assets.KNIGHT_W, Assets.ROOK_W };
            AssetData[] blackBack = { Assets.ROOK_B, Assets.KNIGHT_B, Assets.BISHOP_B, Assets.QUEEN_B, Assets.KING_B, Assets.BISHOP_B, Assets.KNIGHT_B, Assets.ROOK_B };

            for (int i = 0; i < 8; i++)
            {
                makeMovable(addItem(whiteBack[i]).SetPosition(files[i], 0, -3.5).AddAttribute("color", "white"));
                makeMovable(addItem(Assets.PAWN_W).SetPosition(files[i], 0, -2.5).AddAttribute("color", "white"));
                makeMovable(addItem(blackBack[i]).SetPosition(files[i], 0, 3.5).AddAttribute("color", "black"));
                makeMovable(addItem(Assets.PAWN_B).SetPosition(files[i], 0, 2.5).AddAttribute("color", "black"));
            }

            return Task.CompletedTask;
        }

        protected override Task EndGame()
        {
            // Tabletop: nothing to finalize.
            return Task.CompletedTask;
        }

        protected override Task<bool> IsEndGame()
        {
            return Task.FromResult(false); // no automatic end — players decide
        }

        protected override List<PlayerData> GetGameWinners()
        {
            return new List<PlayerData>();
        }
    }
}
