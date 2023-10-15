using Microsoft.AspNetCore.SignalR.Client;

namespace MG.Server.BL
{
    public class SignalIRClient
    {
        //HubConnection hubConnection;
        //public SignalIRClient()
        //{
        //    hubConnection = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder()
        //        .WithUrl($"http://localhost:7114/notifications/notifications")
        //        .WithAutomaticReconnect()
        //        .Build();

        //    hubConnection.StartAsync();

        //}



        //public async Task SendNotificationAsync(int userID, string notType, string notData)
        //{
        //    await hubConnection.InvokeAsync("SendNotificationData", userID.ToString(), notType, notData);
        //}
    }
}
