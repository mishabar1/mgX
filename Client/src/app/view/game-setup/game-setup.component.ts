import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {GameData} from '../../entities/game.data';
import {RouteNames} from '../../app-routing.module';
import {SignalrService} from '../../services/SignalrService';
import {ActivatedRoute, Router} from '@angular/router';
import {GeneralService} from '../../bl/general.service';
import {DALService} from '../../dal/dal.service';
import {UserData} from '../../entities/user.data';
import {join} from 'lodash';
import {PlayerData} from '../../entities/player.data';
import {UnsubscriberService} from '../../services/unsubscriber.service';

@Component({
  selector: 'app-game-setup',
  templateUrl: './game-setup.component.html',
  styleUrls: ['./game-setup.component.scss'],
  providers: [UnsubscriberService]
})
export class GameSetupComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  gameId:string|null = "";
  gameData!: GameData;
  user!:UserData;
  constructor(public signalRService: SignalrService,
              private router: Router,
              private unsubscriberService: UnsubscriberService,
              private activatedRoute: ActivatedRoute,
              private generalService: GeneralService,
              private dalService: DALService) {
  }

  ngOnInit(): void {
    this.user = this.generalService.User!;
    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');

    this.updateGame();


    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      this.gameData = data;

    });
    this.signalRService.hubConnection.on('GamesUpdated', data => {
      console.log('GamesUpdated', data);

    });
    this.signalRService.hubConnection.on('GameDeleted', data => {
      console.log('GameDeleted', data);

    });



  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.off('GamesUpdated');
    this.signalRService.hubConnection.off('GameDeleted');
  }

  updateGame(){
    this.dalService.getGameById(this.gameId!).subscribe(game=>{
      if(!game){
        this.router.navigate([RouteNames.GamesList]);
        return;
      }
      this.gameData = game;
      // this.initThree();
    });
  }
  ngAfterViewInit(): void {

  }

  ngOnChanges(changes: SimpleChanges): void {
  }



  start() {
    this.dalService.startGame(this.gameId!).subscribe();
  }


  leave(player: PlayerData) {
    this.dalService.joinGame(this.gameId!, player.id, null, "EMPTY_SEAT").subscribe();
  }

  set_ai(player: PlayerData) {
    this.dalService.joinGame(this.gameId!, player.id, null, "AI").subscribe();
  }

  join_game(player: PlayerData) {
    this.dalService.joinGame(this.gameId!, player.id, this.user, "HUMAN").subscribe();
  }

  setup() {
    this.dalService.setupGame(this.gameId!, this.user.id).subscribe()
  }
  open() {
    this.router.navigate([RouteNames.GamePlay,this.gameId]);
  }
}
