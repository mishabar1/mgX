using MG.Server.BL;
using MG.Server.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json.Serialization;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
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


        [HttpGet("GetGameByID")]
        public async Task<IActionResult> GetGameByID(string gameId)
        {
            _logger.LogTrace("GetGameByID");

            return Ok(await _gameBL.GetGameByID(gameId));
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

        [HttpPost("JoinGame")]
        public async Task<IActionResult> JoinGame(JoinGameData data)
        {
            _logger.LogTrace("JoinGame");
            return Ok(await _gameBL.JoinGame(data));
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
        public double clientX { get; set; }
        public double clientY { get; set; }

        [JsonIgnore]public PlayerData? Player { get; set; }
        [JsonIgnore] public ItemData? Item { get; set; }
        [JsonIgnore] public ItemData? DragTargetItem { get; set; }


    }
}
