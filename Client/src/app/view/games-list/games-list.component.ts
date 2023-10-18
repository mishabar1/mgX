import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {SignalrService} from '../../services/SignalrService';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {RouteNames} from '../../app-routing.module';
import {Router} from '@angular/router';

@Component({
  selector: 'app-games-list',
  templateUrl: './games-list.component.html',
  styleUrls: ['./games-list.component.scss']
})
export class GamesListComponent  implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  games: GameData[]=[];
  constructor(public signalRService: SignalrService,
              private router: Router,
              private dalService: DALService) {
  }

  ngOnInit(): void {
    this.updateGamesList();
  }
  ngAfterViewInit(): void {

  }

  ngOnChanges(changes: SimpleChanges): void {
  }

  ngOnDestroy(): void {
  }
  updateGamesList(){
    this.dalService.getGamesList().subscribe(list=>{
      this.games=list;
    })
  }
  createGame() {
    this.dalService.createGame().subscribe(gameData => {
      //this.loadGame(gameData);
      this.updateGamesList();
    })
  }


  openGame(game: GameData) {
    this.router.navigate([RouteNames.GamePlay,game.id]);
  }
}
