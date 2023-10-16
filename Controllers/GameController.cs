using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        readonly GameBL _gameBL;
        private readonly ILogger<GameController> _logger;
        public GameController(GameBL gameBL, ILogger<GameController> logger)
        {
            _gameBL = gameBL;
            _logger = logger;
        }

        [HttpGet("GetAllGames")]
        public async Task<IActionResult> GetAllGames()
        {
            _logger.LogTrace("GetAllGames");

            return Ok(await _gameBL.GetAllGames());
        }


        [HttpGet("GetGameByID")]
        public async Task<IActionResult> GetGameByID(string? gameId)
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
    }

    public class CreateGameData
    {
    }
}
