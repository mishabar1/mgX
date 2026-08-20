using MG.Server.BL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
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

            var result = await _userBL.Login(data);
            if (result == null) return BadRequest(new { error = "A name is required." });

            return Ok(result);
        }

        [HttpGet("TensofFlowTest")]
        public async Task<IActionResult> TensofFlowTest()
        {
            _logger.LogTrace("TensofFlowTest");
            await _userBL.TensofFlowTest();
            return Ok(new {ok=true});
        }
    }

    public class LoginData
    {
        public string name { get; set; }
    }
}
