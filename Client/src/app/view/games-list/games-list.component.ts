import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {SignalrService} from '../../services/SignalrService';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {RouteNames} from '../../app-routing.module';
import {Router} from '@angular/router';
import {UserData} from '../../entities/user.data';
import {GeneralService} from '../../bl/general.service';

@Component({
  selector: 'app-games-list',
  templateUrl: './games-list.component.html',
  styleUrls: ['./games-list.component.scss']
})
export class GamesListComponent  implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  games: GameData[]=[];
  user!:UserData;
  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private dalService: DALService) {
  }

  ngOnInit(): void {
    this.user = this.generalService.User!;
    this.updateGamesList();

    this.signalRService.hubConnection.on('GamesUpdated', data => {
      console.log('GamesUpdated', data);
      this.updateGamesList();
    });

    this.signalRService.hubConnection.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      this.updateGamesList();
    });


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
    this.dalService.createGame(this.user.id).subscribe(gameData => {
      //this.loadGame(gameData);
      // this.updateGamesList();
    })
  }


  openGame(game: GameData) {

  }

  setup(game: GameData) {
    this.dalService.setupGame(game.id,this.user.id).subscribe(gameData => {
      //this.loadGame(gameData);
      //this.updateGamesList();
    })
  }

  start(game: GameData) {
    this.dalService.startGame(game.id).subscribe(gameData => {
      this.router.navigate([RouteNames.GamePlay,game.id]);
    })
  }

  delete(game: GameData) {
    this.dalService.deleteGame(game.id).subscribe(gameData => {
      //this.loadGame(gameData);
      //this.updateGamesList();
    })
  }
}
