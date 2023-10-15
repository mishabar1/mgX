using MG.Server.BL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MG.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        readonly DataRepository _authorRepository;
        IServiceProvider _serviceProvider;
        Microsoft.AspNetCore.SignalR.IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<GameController> _logger;
        public GameController(DataRepository authorRepository, 
            ILogger<GameController> logger,
            Microsoft.AspNetCore.SignalR.IHubContext<NotificationHub> hubContext, IServiceProvider serviceProvider)
        {
            _authorRepository = authorRepository;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;


        }

        [HttpGet("test1")]
        public async Task<IActionResult> Test1()
        {

            _hubContext.Clients.All.SendAsync("test", "aa bb cc");
            //_hubContext.Clients.Client("").SendAsync("ff");
            //_hubContext.Clients.Group("").SendAsync("ss");
            //var context = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();
            //context.Clients.All.addMessage("Hello");

            //var c = _serviceProvider.GetRequiredService<SignalIRClient>();
            //c.SendNotificationAsync(0, "aaa", "bbb");
            ;

            return Ok(await _authorRepository.test1());
        }


        [HttpGet("test2")]
        public async Task<IActionResult> Test12()
        {
            return Ok(await _authorRepository.test2());
        }
    }
}
