import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges, ViewChild, ChangeDetectionStrategy} from '@angular/core';
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
    providers: [UnsubscriberService],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GameSetupComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  gameId:string|null = "";
  gameData!: GameData;
  user!:UserData;

  // Show/hide the player avatar heads in the 3D scene (persisted, read on game load).
  get showHeads(): boolean { return localStorage.getItem('mg.showHeads') !== 'false'; }
  set showHeads(v: boolean) { localStorage.setItem('mg.showHeads', v ? 'true' : 'false'); }

  // Openable once started (PLAY) and still afterwards (ENDED) so a finished game
  // can be reviewed/analysed.
  get canOpen(): boolean { const s = String(this.gameData?.gameStatus); return s === 'PLAY' || s === 'ENDED'; }
  get isStarted(): boolean { return String(this.gameData?.gameStatus) === 'PLAY'; }

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

    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      //this.iterate(this.gameData,data);
      this.gameData = data;

    });
    // this.signalRService.hubConnection.off('GamesUpdated');
    // this.signalRService.hubConnection.on('GamesUpdated', data => {
    //   console.log('GamesUpdated', data);
    //   // TODO !!!
    // });
    this.signalRService.hubConnection.off('GameDeleted');
    this.signalRService.hubConnection.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      debugger;
      // TODO !!!
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
      if(!this.gameData) {
        this.gameData =game;
      }else{
        this.iterate(this.gameData,game);
      }
    });
  }

  iterate(oldObj:any, newObj:any) {
    for (let property in oldObj) {
      if (newObj.hasOwnProperty(property)) {
        if (typeof oldObj[property] == "object") {
          this.iterate(oldObj[property], newObj[property] );
        } else {
          oldObj[property] = newObj[property];
          // console.log(property + "   " + oldObj[property]);
          //$('#output').append($("<div/>").text(stack + '.' + property))
        }
      }
    }
  }


  ngAfterViewInit(): void {

  }

  ngOnChanges(changes: SimpleChanges): void {
  }



  start() {
    if (this.isStarted) return; // already running — use Restart instead
    // Once started, jump straight into the game (same as clicking Open).
    const go = () => this.router.navigate([RouteNames.GamePlay, this.gameId]);
    const status = String(this.gameData?.gameStatus);
    if (status === 'SETUP') {
      this.dalService.startGame(this.gameId!).subscribe(() => go());
    } else {
      // Never set up → run Setup first, then Start.
      this.dalService.setupGame(this.gameId!, this.user.id).subscribe(() => {
        this.dalService.startGame(this.gameId!).subscribe(() => go());
      });
    }
  }

  // Restart a running game: reset the board (Setup) then Start again.
  restart() {
    this.dalService.setupGame(this.gameId!, this.user.id).subscribe(() => {
      this.dalService.startGame(this.gameId!).subscribe();
    });
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
    if (!this.canOpen) return; // only openable once started
    this.router.navigate([RouteNames.GamePlay,this.gameId]);
  }

  backClick() {
    this.router.navigate([RouteNames.GamesList]);
  }
}
