using Microsoft.AspNetCore.SignalR;
using static System.Net.Mime.MediaTypeNames;

namespace MG.Server.BL
{
    public class NotificationModel
    {
        public string Data { get; set; }
        public string NotificationType { get; set; }
        public NotificationModel(string t, string d)
        {
            Data = d;
            NotificationType = t;
        }
    }

    public class NotificationHub : Hub
    {

        public async Task SendNotificationData(string user_id, string notification_type, string notification_data)
        {
            await Clients.Group(user_id).SendAsync("test", new NotificationModel(notification_type, notification_data));
        }

        public async void SetConnectionIDUser(int user_id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, user_id.ToString());
        }

        public async void XXX1(int i,string s)
        {
            Console.WriteLine("sss" + i.ToString() + s);
            //await Groups.AddToGroupAsync(Context.ConnectionId, user_id.ToString());
            Clients.All.SendAsync("test");
        }

    }
}
