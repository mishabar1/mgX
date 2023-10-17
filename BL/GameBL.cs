using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using MG.Server.GameFlows;

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
            var game = BaseGameFlow.CreateGame(GameTypeEnum.TIK_TAK_TOE);
            _dataRepository.Games.Add(game);

            await game.GameFlow.Setup();                    
                       
            return game;
        }

        internal async Task<object?> ExecuteAction(ExecuteActionData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x=>x.Id == data.GameId).FirstOrDefault();

            if (game != null)
            {

                await game.GameFlow.ExecuteAction(data);
            }

            // 
            return "OK ExecuteAction";
        }

        internal async Task<object?> SetupGame(SetupGameData data)
        {
            return "OK SetupGame";
        }

        internal async Task<object?> StartGame(StartGameData data)
        {
            return "OK StartGame";
        }
    }
}
