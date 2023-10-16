using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;

namespace MG.Server.BL
{
    public class GameBL
    {
        ILogger<GameBL> _logger;
        DataRepository _dataRepository;
        public GameBL(ILogger<GameBL> logger,DataRepository dataRepository)
        {
            _logger = logger;
            _dataRepository = dataRepository;
        }


        internal async Task<List<GameData>> GetAllGames()
        {
            var list = _dataRepository.Games.ToList();

            return list;
        }


        internal async Task<GameData> GetGameByID(string gameId)
        {
            return _dataRepository.Games.First();
        }

        internal async Task<GameData> CreateGame(CreateGameData data)
        {
            var game = new GameData
            {
                Name = "gam x",
                GameType = GameTypeEnum.TIK_TAK_TOE,
            };

            new PlayerData(game) { Type = PlayerTypeEnum.HUMAN };
            new PlayerData(game) { Type = PlayerTypeEnum.AI };
            new PlayerData(game) { Type = PlayerTypeEnum.AI };


            var asset = new AssetData();

            var i1 = new ItemData(game, asset) {
                Position = new V3(0.5, 0.5, 0.5)
            };
            var i2 = new ItemData(game, asset, i1) {
                Position = new V3(0, 0.05, 0.05)
            };
            var i3 = new ItemData(game, asset, i1) {
                Position = new V3(0, -0.05, -0.05)
            };
            var i4 = new ItemData(game, asset) {
                Position = new V3(0, 0.3, 0.1)
            };

            _dataRepository.Games.Add(game);



            return game;
        }
    }
}
