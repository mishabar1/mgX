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


        internal async Task<GameData?> GetGameByID(string gameId)
        {
            return _dataRepository.Games.Find(x => x.Id == gameId);
        }

        internal async Task<GameData> CreateGame(CreateGameData data)
        {
            var game = BaseGameFlow.CreateGame(data.gameType, data.userId);
            _dataRepository.Games.Add(game);


            //update all clients
            await DataRepository.Singleton.HubGamesUpdated(game);

            //save db
            _dataRepository.Save();

            return game;
        }

        internal async Task<object?> ExecuteAction(ExecuteActionData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {

                await game.GameFlow.ExecuteAction(data);

               
            }

            return new { x = "TODO !!! ExecuteAction" };
        }

        internal async Task<object?> SetupGame(SetupGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {
                await game.GameFlow.RunSetupFlow();

            }

            //save db
            _dataRepository.Save();


            return new { x = "TODO !!! SetupGame" };
        }

        internal async Task<object?> StartGame(StartGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {
                await game.GameFlow.RunStartFlow();
            }

            //save db
            _dataRepository.Save();


            return new { x = "TODO !!! StartGame" };
        }

        internal async Task<object> DeleteGame(StartGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();
            game.GameStatus = GameStatusEnum.ENDED;
            await game.GameFlow.RunEndGameFlow();

            _dataRepository.Games.Remove(game);
            await DataRepository.Singleton.HubGameDeleted(data.gameId);

            //save db
            _dataRepository.Save();


            return new { x = "TODO !!! DeleteGame" };
        }

        internal async Task<object?> JoinGame(JoinGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            if (game != null)
            {
                var player = game.Players.Find(x => x.Id == data.playerId);
                if (player != null)
                {
                    player.User = data.user;
                    player.Type = data.type;
                }

                await DataRepository.Singleton.HubGameUpdated(game);
                await DataRepository.Singleton.HubGamesUpdated(game);
            }

            //save db
            _dataRepository.Save();

            return new { x = "TODO !!! JoinGame" };
        }
    }
}
