import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr"
import {environment} from '../../environments/environment';
import { Observable } from 'rxjs';
@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  hubConnection!: signalR.HubConnection

  constructor() {
  }
  startConnection () {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.serverURL+ '/notifications')
      .withAutomaticReconnect()
      .build();
    this.hubConnection
      .start()
      .then(() => {
        console.log('Connection started');
        this.hubConnection.send('SetConnectionIDUser', 123);
      })
      .catch(err => console.log('Error while starting connection: ' + err))

    // this.hubConnection.on('test',data=>{
    //
    //   console.log(data);
    // })
  }

  testSendXXX1(){
    this.hubConnection.send("XXX1",123,"abc");
    // this.hubConnection.invoke()
  }

  executeAction(GameId: string, PlayerId: string, ItemId: string, ActionId: string, DragTargetItemId: string, ClientX: number, ClientY: number){
    const data = {
      GameId,
      PlayerId,
      ItemId,
      ActionId,
      DragTargetItemId,
      ClientX,
      ClientY
    }
    this.hubConnection.send("ExecuteAction", data);
    // this.hubConnection.invoke("ExecuteAction", data);
  }

}
