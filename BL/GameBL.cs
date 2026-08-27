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


        // The games LIST used to return every game IN FULL — every board, every panel tree and
        // every hidden role on the server, to anyone who asked, on every list refresh. The list
        // screen reads five scalars, each seat's name/type and the result line for a finished
        // game; nothing else. Project exactly that: it is both the leak fix and by a wide margin
        // the biggest payload cut in the app.
        internal async Task<List<GameSummary>> GetAllGames()
        {
            return _dataRepository.Games.Select(GameSummary.Of).ToList();
        }


        internal async Task<GameData?> GetGameByID(string gameId)
        {
            return _dataRepository.Games.Find(x => x.Id == gameId);
        }

        /// <summary>
        /// The view of one game that <paramref name="userId"/> is allowed to see.
        ///
        /// This MUST mirror DataRepository.HubGameUpdated: the SignalR push is redacted, so if
        /// the REST fetch is not, the secret is simply one GET away and redaction is theatre.
        /// An unauthenticated caller gets userId == null, holds no seats, and therefore sees
        /// none of the per-seat secrets — the safe default.
        /// </summary>
        internal GameData ViewFor(GameData game, string? userId)
        {
            var flow = game.GameFlow;
            if (flow == null || !flow.HasHiddenInfo) return game;

            var view = game.DeepCopy();
            flow.RedactFor(view, userId);
            return view;
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

        // callerUserId is the AUTHENTICATED user from the SignalR context — never a value the
        // client put in the payload. The game flow uses it to prove the caller owns the seat.
        internal async Task<object?> ExecuteAction(ExecuteActionData data, string? callerUserId)
        {
            if (data == null) return new { error = "no action data" };

            // find game in db
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();

            // A persisted game whose type is no longer known reloads with a null flow
            // (DataRepository.AttachGameFlow has no default case) — don't NRE on it.
            if (game?.GameFlow == null) return new { error = "game not found", gameId = data.gameId };

            await game.GameFlow.ExecuteAction(data, callerUserId);

            return new { ok = true };
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

            foreach (var p in game.Players) p.AIAgent?.Stop(); // stop AI timers for the deleted game
            _dataRepository.Games.Remove(game);
            await DataRepository.Singleton.HubGameDeleted(data.gameId);

            //save db
            await _dataRepository.Save();


            return new { x = "TODO !!! DeleteGame" };
        }

        internal async Task<object> UndoGame(StartGameData data)
        {
            var game = _dataRepository.Games.Where(x => x.Id == data.gameId).FirstOrDefault();
            if (game == null) return new { error = "game not found", gameId = data.gameId };
            await game.GameFlow.UndoLastMove(); // restores state + re-broadcasts (which saves)
            return new { ok = true };
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
                    // Seat the STORED user, matched by id — not the object the client posted.
                    //
                    // Two reasons. (1) `data.user` is model-bound, so BaseData's constructor has
                    // already stamped it with a random Id and a random Name; a payload missing
                    // `id` used to seat a user nobody can ever be, and the hub's caller check
                    // would then reject that seat forever. (2) user ids are now DERIVED FROM THE
                    // NAME, so anyone who knows a display name can compute that user's id — this
                    // at least stops a forged `name` riding along with it.
                    //
                    // This endpoint is still unauthenticated, so it does NOT stop someone seating
                    // a real user they aren't. Closing that needs [Authorize] on GameController.
                    var user = data.user?.Id != null
                        ? _dataRepository.Users.Find(u => u.Id == data.user.Id)
                        : null;

                    if (data.type == PlayerTypeEnum.HUMAN && user == null)
                    {
                        // Unknown user: refuse rather than seating a ghost.
                        return new { error = "unknown user", playerId = data.playerId };
                    }

                    player.User = user;
                    player.Type = data.type;

                    // Give each AI a unique, friendly name (animal) — EXCEPT in D&D, where each
                    // seat already has a class/role (Warrior, Wizard…) that identifies it.
                    if (data.type == PlayerTypeEnum.AI && game.GameType != GameTypeEnum.DND)
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
