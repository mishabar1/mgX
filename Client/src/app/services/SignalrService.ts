import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr"
import {environment} from '../../environments/environment';
import { Observable } from 'rxjs';
import {V3} from '../entities/V3';
import {GeneralService} from '../bl/general.service';
@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  hubConnection!: signalR.HubConnection

  static singletone:SignalrService;
  constructor(private general: GeneralService) {
    SignalrService.singletone = this;
  }
  startConnection (userId:string) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.serverURL+ '/notifications', {
        // sends ?access_token=... on the handshake; the server reads it for the hub path
        accessTokenFactory: () => this.general.Token || ''
      })
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

  // ---- voice chat (WebRTC) signaling ----
  joinVoice(gameId: string, userName: string) {
    this.hubConnection.send('JoinVoice', gameId, userName);
  }
  leaveVoice(gameId: string) {
    this.hubConnection.send('LeaveVoice', gameId);
  }
  voiceSignal(targetConnectionId: string, data: any) {
    this.hubConnection.send('VoiceSignal', targetConnectionId, data);
  }

}
