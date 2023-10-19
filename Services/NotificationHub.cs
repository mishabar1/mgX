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

        //public async Task SendNotificationData(string user_id, string notification_type, string notification_data)
        //{
        //    await Clients.Group(user_id).SendAsync("test", new NotificationModel(notification_type, notification_data));
        //}

        public async void SetConnectionIDUser(int user_id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, user_id.ToString());
        }

        //public async void GameUpdated(int i, string s)
        //{
        //    Console.WriteLine("sss" + i.ToString() + s);
        //    //await Groups.AddToGroupAsync(Context.ConnectionId, user_id.ToString());
            
        //}

        public async void ExecuteAction(ExecuteActionData s)
        {
            Console.WriteLine("ExecuteAction "   + s);
            await _gameBL.ExecuteAction(s);
            // await Groups.AddToGroupAsync(Context.ConnectionId, user_id.ToString());
            // Clients.All.SendAsync("test");
        }

        //internal void GameUpdated(GameData game)
        //{
            
        //}
        
    }
}
