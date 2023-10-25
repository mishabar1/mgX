using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using Microsoft.AspNetCore.SignalR;
using static System.Net.Mime.MediaTypeNames;

namespace MG.Server.Services
{
    //public class NotificationModel
    //{
    //    public string Data { get; set; }
    //    public string NotificationType { get; set; }
    //    public NotificationModel(string t, string d)
    //    {
    //        Data = d;
    //        NotificationType = t;
    //    }
    //}

    public class NotificationHub : Hub
    {
        readonly GameBL _gameBL;
        private readonly ILogger<NotificationHub> _logger;
        public NotificationHub(GameBL gameBL, DataRepository dataRepository, ILogger<NotificationHub> logger) :base()
        {
            _gameBL = gameBL;
            _logger = logger;            
        }

        public async void SetConnectionIDUser(string userId)
        {
            Console.WriteLine("NotificationHub SetConnectionIDUser");
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.ToString());
        }


        public async void ExecuteAction(ExecuteActionData s)
        {
            Console.WriteLine("NotificationHub ExecuteAction");
            await _gameBL.ExecuteAction(s);
        }

        
    }
}
