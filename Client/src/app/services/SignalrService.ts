import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr"
import {environment} from '../../environments/environment';
import { Observable, Subject } from 'rxjs';
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
      // Group membership is per CONNECTION and a reconnect gets a brand new connection id, so
      // every watch is gone. Without this the view stays mounted and looks fine but never
      // receives another update for the rest of the session.
      this.restoreWatches();

      // Re-subscribing is not enough: every update sent WHILE we were down is gone for good.
      // SignalR replays nothing. Tell the views to refetch authoritative state.
      this.reconnectedSubject.next();
    });

    this.hubConnection
      .start()
      .then(() => {
        console.log('Connection started');
        this.hubConnection.send('SetConnectionIDUser', userId);
        // A view can already be mounted when the socket (re)opens — e.g. startConnection runs
        // again after login while a component is up. Re-assert whatever it asked to watch.
        this.restoreWatches();
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
    this.watchedGameId = null;      // the new socket must not inherit the old user's watches
    this.watchingLobby = false;
    conn?.stop().catch(() => { });
  }

  // ------------------------------------------------------------------------------------
  // UPDATE SUBSCRIPTIONS
  //
  // The server used to push every game's full state to every client (Clients.All) and each
  // view threw away everything but its own game — so a phone sitting in the lobby was
  // parsing and diffing ~200 KB Small World payloads it could not use. Now a view declares
  // what it is looking at and the server sends nothing else:
  //   watchGame(id) — game-play / game-setup: full GameUpdated for THAT game only
  //   watchLobby()  — games-list: GamesUpdated / GameDeleted list pings only
  //
  // Tracked here (not in the components) because a reconnect silently drops every group
  // membership and these are what have to be re-asserted. See NotificationHub.
  // ------------------------------------------------------------------------------------
  private watchedGameId: string | null = null;
  private watchingLobby = false;

  watchGame(gameId: string | null) {
    if (!gameId) return;
    // Defensive: leave a previous game if a view forgot to (or navigation raced).
    if (this.watchedGameId && this.watchedGameId !== gameId) this.unwatchGame(this.watchedGameId);
    this.watchedGameId = gameId;
    this.send('WatchGame', gameId);
  }

  unwatchGame(gameId?: string | null) {
    const id = gameId || this.watchedGameId;
    if (!id) return;
    if (this.watchedGameId === id) this.watchedGameId = null;
    this.send('UnwatchGame', id);
  }

  watchLobby() {
    this.watchingLobby = true;
    this.send('WatchLobby');
  }

  unwatchLobby() {
    this.watchingLobby = false;
    this.send('UnwatchLobby');
  }

  // ------------------------------------------------------------------------------------
  // RECONNECT RESYNC
  //
  // `withAutomaticReconnect()` restores the SOCKET, not the messages missed while it was down.
  // Any GameUpdated pushed during the gap is lost, so the board silently sits at whatever it
  // showed when the connection dropped — with no error and no indication anything is stale.
  // Views subscribe here and refetch once, which is the only way back to a known-good state.
  // ------------------------------------------------------------------------------------
  private readonly reconnectedSubject = new Subject<void>();
  readonly reconnected$: Observable<void> = this.reconnectedSubject.asObservable();

  /** Re-assert the current watches on a brand new connection (fresh start or reconnect). */
  private restoreWatches() {
    if (this.watchingLobby) this.send('WatchLobby');
    if (this.watchedGameId) this.send('WatchGame', this.watchedGameId);
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
