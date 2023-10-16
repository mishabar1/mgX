using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase    
    {
        UserBL _userBL;
        private readonly ILogger<UserController> _logger;
        public UserController(UserBL userBL, ILogger<UserController> logger)
        {
            _userBL = userBL;
            _logger = logger;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginData data)
        {
            _logger.LogTrace("Login");
            //await _dataRepository.GetGameByID(gameId)
            return Ok();
        }
    }

    public class LoginData
    {
        public string Name { get; set; }
    }
}
