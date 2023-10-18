using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
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
            return Ok(await _userBL.Login(data) );
        }
    }

    public class LoginData
    {
        public string name { get; set; }
    }
}
