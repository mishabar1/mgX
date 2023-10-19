using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using MG.Server.GameFlows;
using MG.Server.Services;

namespace MG.Server.BL
{
    public class GameBL
    {
        ILogger<GameBL> _logger;
        DataRepository _dataRepository;
        //NotificationHub _notificationHub;
        public GameBL(ILogger<GameBL> logger,
            DataRepository dataRepository)
        {
            _logger = logger;
            _dataRepository = dataRepository;
        }


        internal async Task<List<GameData>> GetAllGames()
        {
            var list = _dataRepository.Games.ToList();

            return list;
        }


        internal async Task<GameData?> GetGameByID(string gameId)
        {
            return _dataRepository.Games.Find(x => x.Id == gameId);
        }

        internal async Task<GameData> CreateGame(CreateGameData data)
        {
            var game = BaseGameFlow.CreateGame(GameTypeEnum.TIK_TAK_TOE);
            _dataRepository.Games.Add(game);

            await game.GameFlow.Setup();

            //update all clients
            _dataRepository.HubGamesUpdated(game);
            return game;
        }

        internal async Task<object?> ExecuteAction(ExecuteActionData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {

                await game.GameFlow.ExecuteAction(data);

                _dataRepository.HubGameUpdated(game);
            }

            return new { x = "TODO !!! ExecuteAction" };
        }

        internal async Task<object?> SetupGame(SetupGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {
                await game.GameFlow.Setup();
                _dataRepository.HubGamesUpdated(game);
                _dataRepository.HubGameUpdated(game);

            }

            return new { x = "TODO !!! SetupGame" };
        }

        internal async Task<object?> StartGame(StartGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {
                await game.GameFlow.StartGame();
                _dataRepository.HubGamesUpdated(game);
                _dataRepository.HubGameUpdated(game);

            }

            return new { x = "TODO !!! StartGame" };
        }

        internal async Task<object> DeleteGame(StartGameData data)
        {

            _dataRepository.Games.RemoveAll(x => x.Id == data.gameId);
            _dataRepository.HubGameDeleted(data.gameId);

            return new { x = "TODO !!! DeleteGame" };
        }
    }
}
