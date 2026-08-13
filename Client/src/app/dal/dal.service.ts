import {Injectable} from '@angular/core';
import { HttpClient } from '@angular/common/http';
// import {EnvironmentParamsService} from '../services/env-params.service';
import {Observable} from 'rxjs';
import {GameData} from '../entities/game.data';
import {environment} from '../../environments/environment';
import {UserData} from '../entities/user.data';

// server now returns the signed token alongside the user
export interface LoginResult {
  token: string;
  user: UserData;
}

@Injectable({
  providedIn: 'root'
})
export class DALService{

  private baseGameUrl = environment.serverURL+ '/api/Game';
  private baseUserUrl = environment.serverURL+ '/api/User';

  constructor(private http: HttpClient) {}


  login(name:string): Observable<LoginResult> {
    return this.http.post<LoginResult>(this.baseUserUrl + `/Login`,{name});
  }

  getGameById(gameId:string): Observable<GameData> {
    return this.http.get<GameData>(this.baseGameUrl + `/GetGameByID?GameId=${gameId}`);
  }

  getGamesList(): Observable<any> {
    return this.http.get<any>(this.baseGameUrl + `/GetGamesList`);
  }

  // The catalog of creatable games (type/label/icon) — the client renders its "Create" buttons
  // from this, so adding a new game needs no client change.
  getGameTypes(): Observable<{type: string, label: string, icon: string, image: string}[]> {
    return this.http.get<{type: string, label: string, icon: string, image: string}[]>(this.baseGameUrl + `/GameTypes`);
  }

  createGame(userId:string,gameType:string): Observable<GameData> {
    return this.http.post<GameData>(this.baseGameUrl + `/CreateGame`, {userId,gameType});
  }
  setupGame(gameId:string,userId:string): Observable<GameData> {
    return this.http.post<GameData>(this.baseGameUrl + `/SetupGame`, {gameId,userId});
  }
  startGame(gameId:string): Observable<GameData> {
    return this.http.post<GameData>(this.baseGameUrl + `/StartGame`, {gameId});
  }
  deleteGame(gameId:string): Observable<GameData> {
    return this.http.post<GameData>(this.baseGameUrl + `/DeleteGame`, {gameId});
  }
  undoGame(gameId:string): Observable<any> {
    return this.http.post<any>(this.baseGameUrl + `/UndoGame`, {gameId});
  }




  executeAction(GameId: string, PlayerId: string, ActionId: string, ItemId: string, ClientX: number, ClientY: number): Observable<any> {
    const data = {
      GameId, PlayerId, ActionId, ItemId, ClientX, ClientY
    }
    return this.http.post<any>(this.baseGameUrl + `/ExecuteAction`, data);
  }

  // Invoke a game action from a UI (the DM's HTML console) with key/value params instead of a
  // clicked 3D item. `args` is sent as `Args` and read server-side from ExecuteActionData.args.
  executeActionArgs(GameId: string, PlayerId: string, ActionId: string, Args: {[k: string]: string}): Observable<any> {
    return this.http.post<any>(this.baseGameUrl + `/ExecuteAction`, { GameId, PlayerId, ActionId, ItemId: '', Args });
  }

  joinGame(gameId: string, playerId: string, user: UserData|null, type: string) {
    return this.http.post<GameData>(this.baseGameUrl + `/JoinGame`, {gameId,playerId,user,type});
  }

  // Toggle the game's voice-chat settings (enabled at all, and whether spectators may join).
  setVoice(gameId: string, enabled: boolean, spectators: boolean): Observable<any> {
    return this.http.post<any>(this.baseGameUrl + `/SetVoice`, {gameId, enabled, spectators});
  }

  // Toggle the game's "show player heads" setting (saved on the game).
  setShowHeads(gameId: string, enabled: boolean): Observable<any> {
    return this.http.post<any>(this.baseGameUrl + `/SetShowHeads`, {gameId, enabled});
  }

  // Choose the card back for card games (saved on the game).
  setCardBack(gameId: string, value: string): Observable<any> {
    return this.http.post<any>(this.baseGameUrl + `/SetCardBack`, {gameId, value});
  }
}
