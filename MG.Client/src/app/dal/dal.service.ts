import {Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
// import {EnvironmentParamsService} from '../services/env-params.service';
import {Observable} from 'rxjs';
import {GameData} from '../entities/gameData';
import {environment} from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class DALService{

  private baseUrl = environment.serverURL+ '/api/Game';

  constructor(private http: HttpClient) {}

  getGameById(gameId:string): Observable<GameData> {
    return this.http.get(this.baseUrl + `/GetGameByID?gameId=${gameId}`);
  }
}
