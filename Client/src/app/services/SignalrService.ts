import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr"
import {environment} from '../../environments/environment';
import { Observable } from 'rxjs';
import {V3} from '../entities/V3';
@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  hubConnection!: signalR.HubConnection

  static singletone:SignalrService;
  constructor() {
    SignalrService.singletone = this;
  }
  startConnection (userId:string) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.serverURL+ '/notifications')
      .withAutomaticReconnect()
      .build();
    this.hubConnection
      .start()
      .then(() => {
        console.log('Connection started');
        this.hubConnection.send('SetConnectionIDUser', userId);
      })
      .catch(err => console.log('Error while starting connection: ' + err))

  }

  executeAction(gameId: string, playerId: string, itemId: string, actionId: string, dragTargetItemId: string, point: V3){
    const data = {
      gameId,
      playerId,
      itemId,
      actionId,
      dragTargetItemId,
      point
    }
    this.hubConnection.send("ExecuteAction", data);
    // this.hubConnection.invoke("ExecuteAction", data);
  }

}
