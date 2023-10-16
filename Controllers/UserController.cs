using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase    
    {
        readonly DataRepository _dataRepository;
        private readonly ILogger<UserController> _logger;
        public UserController(DataRepository dataRepository, ILogger<UserController> logger)
        {
            _dataRepository = dataRepository;
            _logger = logger;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginData data)
        {
            _logger.LogTrace("GetGameByID");
            //await _dataRepository.GetGameByID(gameId)
            return Ok();
        }
    }

    public class LoginData
    {
        public string Name { get; set; }
    }
}
