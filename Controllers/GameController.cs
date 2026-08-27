using MG.Server.BL;
using MG.Server.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json.Serialization;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    public class GameController : ControllerBase
    {
        readonly GameBL _gameBL;
        private readonly ILogger<GameController> _logger;
        public GameController(GameBL gameBL, ILogger<GameController> logger)
        {
            _gameBL = gameBL;
            _logger = logger;
        }

        [HttpGet("GetGamesList")]
        public async Task<IActionResult> GetGamesList()
        {
            _logger.LogTrace("GetGamesList");

            return Ok(await _gameBL.GetAllGames());
        }

        // The catalog of creatable games (type/label/icon). The client's "Create a game" list is
        // built from this — so adding a game needs no client change.
        [HttpGet("GameTypes")]
        public IActionResult GameTypes()
        {
            _logger.LogTrace("GameTypes");
            return Ok(GameFlows.BaseGameFlow.GameCatalog());
        }


        /// <summary>
        /// The signed-in user id, from the JWT's NameIdentifier claim (TokenService.CreateToken).
        /// These endpoints carry no [Authorize] (a known POC gap), but UseAuthentication still
        /// populates User whenever a valid bearer token is present — and the Angular client always
        /// sends one. Null here therefore means "anonymous", which redaction treats as "sees no
        /// secrets" rather than "sees everything".
        /// </summary>
        private string? CallerUserId =>
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        [HttpGet("GetGameByID")]
        public async Task<IActionResult> GetGameByID(string gameId)
        {
            _logger.LogTrace("GetGameByID");

            var game = await _gameBL.GetGameByID(gameId);
            if (game == null) return Ok(null);

            // Redacted exactly like the SignalR push — see GameBL.ViewFor.
            return Ok(_gameBL.ViewFor(game, CallerUserId));
        }


        [HttpPost("CreateGame")]
        public async Task<IActionResult> CreateGame(CreateGameData data)
        {
            _logger.LogTrace("CreateGame");
            return Ok(await _gameBL.CreateGame(data));
        }

        [HttpPost("SetupGame")]
        public async Task<IActionResult> SetupGame(SetupGameData data)
        {
            _logger.LogTrace("SetupGame");
            return Ok(await _gameBL.SetupGame(data));
        }

        [HttpPost("StartGame")]
        public async Task<IActionResult> StartGame(StartGameData data)
        {
            _logger.LogTrace("StartGame");
            return Ok(await _gameBL.StartGame(data));
        }
        [HttpPost("DeleteGame")]
        public async Task<IActionResult> DeleteGame(StartGameData data)
        {
            _logger.LogTrace("DeleteGame");
            return Ok(await _gameBL.DeleteGame(data));
        }

        [HttpPost("UndoGame")]
        public async Task<IActionResult> UndoGame(StartGameData data)
        {
            _logger.LogTrace("UndoGame");
            return Ok(await _gameBL.UndoGame(data));
        }

        [HttpPost("JoinGame")]
        public async Task<IActionResult> JoinGame(JoinGameData data)
        {
            _logger.LogTrace("JoinGame");
            return Ok(await _gameBL.JoinGame(data));
        }

        // Toggle the per-game voice-chat settings (stored on the game and broadcast to all).
        [HttpPost("SetVoice")]
        public async Task<IActionResult> SetVoice(SetVoiceData data)
        {
            _logger.LogTrace("SetVoice");
            return Ok(await _gameBL.SetVoice(data));
        }

        // Toggle the per-game "show player heads" setting (stored on the game, broadcast to all).
        [HttpPost("SetShowHeads")]
        public async Task<IActionResult> SetShowHeads(SetShowHeadsData data)
        {
            _logger.LogTrace("SetShowHeads");
            return Ok(await _gameBL.SetShowHeads(data));
        }

        // Choose the card back for card games (stored on the game, broadcast to all).
        [HttpPost("SetCardBack")]
        public async Task<IActionResult> SetCardBack(SetCardBackData data)
        {
            _logger.LogTrace("SetCardBack");
            return Ok(await _gameBL.SetCardBack(data));
        }


        //[HttpPost("ExecuteAction")]
        //public async Task<IActionResult> ExecuteAction(ExecuteActionData data)
        //{
        //    _logger.LogTrace("ExecuteAction");
        //    return Ok(await _gameBL.ExecuteAction(data),null);
        //}


    }

    public class JoinGameData
    {
      
        public string gameId { get; set; }
        public string playerId { get; set; }
        public UserData? user { get; set; }
        public string type { get; set; }
    }
    public class SetupGameData
    {
        public string gameId { get; set; }
        public string userId { get; set; }
    }
    public class StartGameData
    {
        public string gameId { get; set; }
    }
    public class SetVoiceData
    {
        public string gameId { get; set; }
        public bool enabled { get; set; }    // voice chat allowed at all
        public bool spectators { get; set; } // if true, spectators may join too (else players only)
    }
    public class SetShowHeadsData
    {
        public string gameId { get; set; }
        public bool enabled { get; set; }    // show player avatar heads in the 3D scene
    }
    public class SetCardBackData
    {
        public string gameId { get; set; }
        public string value { get; set; }    // "red" | "blue" | "green" | "brown"
    }
    public class CreateGameData
    {       
        public string gameType { get; set; }
        public string userId { get; set; }

    }

    public class ExecuteActionData:MGPrintable
    {
        public string gameId { get; set; }
        public string playerId { get; set; }
        public string actionId { get; set; }
        public string itemId { get; set; }
        public string? dragTargetItemId { get; set; }
        public V3 point { get; set; }

        // Optional key/value params for actions invoked from a UI (the DM's HTML console),
        // where there is no clicked 3D item carrying attributes.
        public Dictionary<string, string>? args { get; set; }

        [JsonIgnore]public PlayerData? Player { get; set; }
        [JsonIgnore] public ItemData? Item { get; set; }
        [JsonIgnore] public ItemData? DragTargetItem { get; set; }


    }
}
