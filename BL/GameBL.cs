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
            await _dataRepository.Save();

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
            await _dataRepository.Save();


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
            await _dataRepository.Save();


            return new { x = "TODO !!! StartGame" };
        }

        internal async Task<object> DeleteGame(StartGameData data)
        {
            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();
            if (game == null)
            {
                return new { error = "game not found", gameId = data.gameId };
            }
            game.GameStatus = GameStatusEnum.ENDED;
            await game.GameFlow.RunEndGameFlow();

            _dataRepository.Games.Remove(game);
            await DataRepository.Singleton.HubGameDeleted(data.gameId);

            //save db
            await _dataRepository.Save();


            return new { x = "TODO !!! DeleteGame" };
        }

        // Persist the per-game voice settings on the game's Attributes and broadcast so
        // every connected client (setup + in-game) sees the change.
        internal async Task<object?> SetVoice(SetVoiceData data)
        {
            var game = _dataRepository.Games.FirstOrDefault(x => x.Id == data.gameId);
            if (game != null)
            {
                if (data.enabled) game.Attributes["allowVoice"] = "1";
                else game.Attributes.Remove("allowVoice");

                if (data.enabled && data.spectators) game.Attributes["voiceSpectators"] = "1";
                else game.Attributes.Remove("voiceSpectators");

                await DataRepository.Singleton.HubGameUpdated(game);
            }

            await _dataRepository.Save();
            return new { ok = true };
        }

        // Persist the "show heads" setting on the game and broadcast. Stored explicitly as
        // "1"/"0" so absence can still default to shown.
        internal async Task<object?> SetShowHeads(SetShowHeadsData data)
        {
            var game = _dataRepository.Games.FirstOrDefault(x => x.Id == data.gameId);
            if (game != null)
            {
                game.Attributes["showHeads"] = data.enabled ? "1" : "0";
                await DataRepository.Singleton.HubGameUpdated(game);
            }

            await _dataRepository.Save();
            return new { ok = true };
        }

        // Persist the chosen card back on the game and broadcast.
        internal async Task<object?> SetCardBack(SetCardBackData data)
        {
            var game = _dataRepository.Games.FirstOrDefault(x => x.Id == data.gameId);
            if (game != null)
            {
                var allowed = new[] { "red", "blue", "green", "brown" };
                game.Attributes["cardBack"] = allowed.Contains(data.value) ? data.value : "red";
                await DataRepository.Singleton.HubGameUpdated(game);
            }
            await _dataRepository.Save();
            return new { ok = true };
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

                    // Give each AI a unique, friendly name (animal). Keep the seat's own name if
                    // it's not already taken by another occupied seat; otherwise draw a new one.
                    if (data.type == PlayerTypeEnum.AI)
                    {
                        var taken = game.Players
                            .Where(p => p != player && p.Type != PlayerTypeEnum.EMPTY_SEAT)
                            .Select(p => p.User?.Name ?? p.Name)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .ToHashSet();
                        var name = player.Name;
                        for (int guard = 0; (string.IsNullOrEmpty(name) || taken.Contains(name)) && guard < 100; guard++)
                            name = Utils.RandomName();
                        player.Name = name;
                    }
                }

                await DataRepository.Singleton.HubGameUpdated(game);
                await DataRepository.Singleton.HubGamesUpdated(game);
            }

            //save db
            await _dataRepository.Save();

            return new { x = "TODO !!! JoinGame" };
        }
    }
}
