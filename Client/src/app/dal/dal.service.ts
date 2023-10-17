import {Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
// import {EnvironmentParamsService} from '../services/env-params.service';
import {Observable} from 'rxjs';
import {GameData} from '../entities/game.data';
import {environment} from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class DALService{

  private baseUrl = environment.serverURL+ '/api/Game';

  constructor(private http: HttpClient) {}

  getGameById(gameId:string): Observable<GameData> {
    return this.http.get<GameData>(this.baseUrl + `/GetGameByID?GameId=${gameId}`);
  }

  createGame(): Observable<GameData> {
    return this.http.post<GameData>(this.baseUrl + `/CreateGame`, {});
  }

  executeAction(GameId: string, PlayerId: string, ActionId: string, ItemId: string, ClientX: number, ClientY: number): Observable<any> {
    const data = {
      GameId, PlayerId, ActionId, ItemId, ClientX, ClientY
    }
    return this.http.post<any>(this.baseUrl + `/ExecuteAction`, data);
  }
}
