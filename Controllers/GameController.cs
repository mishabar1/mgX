using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        readonly DataRepository _dataRepository;
        private readonly ILogger<GameController> _logger;
        public GameController(DataRepository dataRepository, ILogger<GameController> logger)
        {
            _dataRepository = dataRepository;
            _logger = logger;
        }

        [HttpGet("GetGameByID")]
        public async Task<IActionResult> GetGameByID(string? gameId)
        {
            _logger.LogTrace("GetGameByID");

            return Ok(await _dataRepository.GetGameByID(gameId));
        }


        [HttpGet("test2")]
        public async Task<IActionResult> Test12()
        {
            return Ok(await _dataRepository.test2());
        }
    }
}
