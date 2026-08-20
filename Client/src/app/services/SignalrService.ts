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
    // The hub is [Authorize]d now, so a connection without a token is refused at the handshake.
    // Bail early rather than opening a socket that can only fail.
    if (!this.general.Token) { console.warn('[signalr] no token — not connecting'); return; }

    // startConnection is reachable twice (app boot and again after login). Stopping the previous
    // connection first stops it leaking an open socket with its handlers still attached.
    if (this.hubConnection) { this.hubConnection.stop().catch(() => { }); }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(environment.serverURL+ '/notifications', {
        // sends ?access_token=... on the handshake; the server reads it for the hub path
        accessTokenFactory: () => this.general.Token || ''
      })
      .withAutomaticReconnect()
      .build();

    // A reconnect gets a NEW connection id, so the old one's group membership is gone. Re-register
    // or this connection silently drops out of its user group for the rest of the session.
    this.hubConnection.onreconnected(() => {
      console.log('[signalr] reconnected');
      this.hubConnection.send('SetConnectionIDUser', userId).catch(() => { });
    });

    this.hubConnection
      .start()
      .then(() => {
        console.log('Connection started');
        this.hubConnection.send('SetConnectionIDUser', userId);
      })
      .catch(err => console.log('Error while starting connection: ' + err))

  }

  /**
   * Close the hub connection and forget it. Signing out has to drop the socket: it was opened
   * with the previous user's token and is still in that user's group, so leaving it up would
   * keep delivering their updates to whoever logs in next.
   */
  stopConnection() {
    const conn = this.hubConnection;
    this.hubConnection = undefined as any;
    conn?.stop().catch(() => { });
  }

  /**
   * Fire-and-forget send that survives a closed / reconnecting connection.
   * `HubConnection.send` REJECTS when the connection isn't in the Connected state, and no call
   * site here ever attached a catch — so a click during a reconnect produced an unhandled
   * rejection instead of a no-op.
   */
  private send(method: string, ...args: any[]) {
    this.hubConnection?.send(method, ...args)
      .catch(err => console.warn('[signalr] %s not sent:', method, err));
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
    this.send("ExecuteAction", data);
  }

  // Invoke an action from a UI (the DM console) with key/value params instead of a clicked
  // 3D item — server reads these from ExecuteActionData.args.
  executeActionArgs(gameId: string, playerId: string, actionId: string, args: {[k: string]: string}) {
    this.send("ExecuteAction", { gameId, playerId, itemId: '', actionId, args });
  }

  // ---- voice chat (WebRTC) signaling ----
  joinVoice(gameId: string, userName: string) {
    this.send('JoinVoice', gameId, userName);
  }
  leaveVoice(gameId: string) {
    this.send('LeaveVoice', gameId);
  }
  voiceSignal(targetConnectionId: string, data: any) {
    this.send('VoiceSignal', targetConnectionId, data);
  }
  sendTranscript(gameId: string, userName: string, text: string) {
    this.send('SendTranscript', gameId, userName, text);
  }

}
