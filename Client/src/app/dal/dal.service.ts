import {Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
// import {EnvironmentParamsService} from '../services/env-params.service';
import {Observable} from 'rxjs';
import {GameData} from '../entities/game.data';
import {environment} from '../../environments/environment';
import {UserData} from '../entities/user.data';

@Injectable({
  providedIn: 'root'
})
export class DALService{

  private baseGameUrl = environment.serverURL+ '/api/Game';
  private baseUserUrl = environment.serverURL+ '/api/User';

  constructor(private http: HttpClient) {}


  login(name:string): Observable<UserData> {
    return this.http.post<UserData>(this.baseUserUrl + `/Login`,{name});
  }

  getGameById(gameId:string): Observable<GameData> {
    return this.http.get<GameData>(this.baseGameUrl + `/GetGameByID?GameId=${gameId}`);
  }

  getGamesList(): Observable<any> {
    return this.http.get<any>(this.baseGameUrl + `/GetGamesList`);
  }

  createGame(): Observable<GameData> {
    return this.http.post<GameData>(this.baseGameUrl + `/CreateGame`, {});
  }

  executeAction(GameId: string, PlayerId: string, ActionId: string, ItemId: string, ClientX: number, ClientY: number): Observable<any> {
    const data = {
      GameId, PlayerId, ActionId, ItemId, ClientX, ClientY
    }
    return this.http.post<any>(this.baseGameUrl + `/ExecuteAction`, data);
  }
}
