import {Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {EnvironmentParamsService} from '../services/env-params.service';
import {Observable} from 'rxjs';
import {GameData} from '../entities/gameData';

@Injectable({
  providedIn: 'root'
})
export class DALService{

  private baseUrl = this.environmentParamsService.getAPIServerURL() + '/api/Game';

  constructor(private http: HttpClient, private environmentParamsService: EnvironmentParamsService) {}

  getGameById(gameId:string): Observable<GameData> {
    return this.http.get(this.baseUrl + `/GetGameByID?gameId=${gameId}`);
  }
}
