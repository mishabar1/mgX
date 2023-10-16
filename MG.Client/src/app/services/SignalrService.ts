import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr"
import {environment} from '../../environments/environment';
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

}
