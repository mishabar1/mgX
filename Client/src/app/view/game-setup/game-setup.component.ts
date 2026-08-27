import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges, ViewChild, ChangeDetectionStrategy, NgZone} from '@angular/core';
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
import {ThumbService} from '../../bl/thumb.service';
import {GAMES_BASE} from '../../bl/mg.three';

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
  assetsBase = GAMES_BASE;   // game assets are hosted by the server

  // Show/hide the player avatar heads in the 3D scene. Now saved ON THE GAME (shared,
  // survives reload) — default shown when the setting is absent.
  get showHeads(): boolean { return this.gameData?.attributes?.['showHeads'] === '1'; }   // OFF by default
  setShowHeads(enabled: boolean) {
    this.dalService.setShowHeads(this.gameId!, enabled).subscribe();
  }

  // Voice-chat settings (stored on the game, shared by everyone).
  get allowVoice(): boolean { return !!this.gameData?.attributes?.['allowVoice']; }
  get voiceSpectators(): boolean { return !!this.gameData?.attributes?.['voiceSpectators']; }
  setVoiceEnabled(enabled: boolean) {
    this.dalService.setVoice(this.gameId!, enabled, this.voiceSpectators).subscribe();
  }
  setVoiceSpectators(spectators: boolean) {
    this.dalService.setVoice(this.gameId!, this.allowVoice, spectators).subscribe();
  }

  // Generic, server-driven setup options (no game type hard-coded):
  //  • a game sets "usesCardBack"=1 to offer the card-back chooser
  //  • a game sets "noAvatars"=1 to hide the "show avatars" toggle
  get usesCardBack(): boolean { return this.gameData?.attributes?.['usesCardBack'] === '1'; }
  get noAvatars(): boolean { return this.gameData?.attributes?.['noAvatars'] === '1'; }
  // Raw game-data dump is debug-only: shown when the URL has ?debug.
  get showDebug(): boolean { return typeof location !== 'undefined' && location.search.includes('debug'); }
  get cardBack(): string { return this.gameData?.attributes?.['cardBack'] || 'red'; }
  backFile(c: string): string {
    return ({red: 'red-56.jpg', blue: 'blue-57.jpg', green: 'green-15.jpg', brown: 'brown-14.jpg'} as any)[c] || 'red-56.jpg';
  }
  setCardBack(value: string) {
    this.dalService.setCardBack(this.gameId!, value).subscribe();
  }

  // Openable once started (PLAY) and still afterwards (ENDED) so a finished game
  // can be reviewed/analysed.
  get canOpen(): boolean { const s = String(this.gameData?.gameStatus); return s === 'PLAY' || s === 'ENDED'; }
  get isStarted(): boolean { return String(this.gameData?.gameStatus) === 'PLAY'; }

  // Seat occupancy — a game can only start once all mandatory seats are taken (by a
  // human or an AI). EMPTY_SEAT doesn't count.
  get occupiedSeats(): number {
    return (this.gameData?.players || []).filter(p => p.type !== 'EMPTY_SEAT').length;
  }
  get minPlayers(): number {
    return this.gameData?.minPlayers ?? (this.gameData?.players?.length || 0);
  }
  get seatsReady(): boolean { return this.occupiedSeats >= this.minPlayers; }
  seatEmpty(player: PlayerData): boolean { return player.type === 'EMPTY_SEAT'; }

  // You can only remove your OWN seat — unless you created the game (admin), who can remove anyone.
  get isCreator(): boolean { return !!this.user?.id && this.gameData?.creatorId === this.user?.id; }
  canLeave(player: PlayerData): boolean {
    return this.isCreator || (!!player.user?.id && player.user?.id === this.user?.id);
  }

  // Do I already occupy a seat? A non-creator may join only ONE seat and can't add AIs.
  get hasMySeat(): boolean {
    return (this.gameData?.players || []).some(p => !!p.user?.id && p.user?.id === this.user?.id);
  }
  get canJoin(): boolean { return this.isCreator || !this.hasMySeat; }
  get canAddAi(): boolean { return this.isCreator; }

  // Tracks the last-seen status so we only auto-open on the transition INTO play (not when
  // opening the setup page of an already-running game to tweak it).
  private lastStatus = '';

  // Rendered hero portraits keyed by seat id (D&D seats carry a "heroUrl").
  heroThumbs: {[id: string]: string} = {};

  constructor(public signalRService: SignalrService,
              private router: Router,
              private zone: NgZone,
              private unsubscriberService: UnsubscriberService,
              private activatedRoute: ActivatedRoute,
              private generalService: GeneralService,
              private thumb: ThumbService,
              private dalService: DALService) {

  }

  // Render a portrait for each seat that has a hero model (once per seat).
  private loadHeroThumbs() {
    (this.gameData?.players || []).forEach((p: PlayerData) => {
      const url = (p as any).attributes?.['heroUrl'];
      if (url && !this.heroThumbs[p.id]) {
        this.thumb.render(url).then(d => { if (d) this.zone.run(() => this.heroThumbs[p.id] = d); });
      }
    });
  }

  // Is the current user sitting in an actual seat (not a spectator / empty seat)?
  private isSeatedPlayer(game: GameData): boolean {
    return (game?.players || []).some(p => p.type !== 'EMPTY_SEAT' && p.user?.id === this.user?.id);
  }

  ngOnInit(): void {
    this.user = this.generalService.User!;
    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');

    // Receive updates for THIS game only (the server sends nothing else to this connection).
    this.signalRService.watchGame(this.gameId);

    this.updateGame();

    this.signalRService.hubConnection?.off('GameUpdated');
    this.signalRService.hubConnection?.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      if (String(data?.id) !== String(this.gameId)) return;   // belt-and-braces; the server now sends only this game
      // SignalR fires OUTSIDE Angular's zone — run inside so every client's seat list refreshes
      // immediately when anyone joins/leaves (not just on the next change-detection tick).
      this.zone.run(() => {
        const wasStarted = this.lastStatus === 'PLAY';
        const nowStarted = String(data.gameStatus) === 'PLAY';
        this.gameData = data;
        this.lastStatus = String(data.gameStatus);
        this.loadHeroThumbs();

        // The game just started while I'm still on the setup page → open it for me,
        // exactly as if I'd clicked Open (only for seated players, not spectators).
        if (nowStarted && !wasStarted && this.isSeatedPlayer(data)) {
          this.router.navigate([RouteNames.GamePlay, this.gameId]);
        }
      });
    });
    // this.signalRService.hubConnection?.off('GamesUpdated');
    // this.signalRService.hubConnection?.on('GamesUpdated', data => {
    //   console.log('GamesUpdated', data);
    //   // TODO !!!
    // });
    this.signalRService.hubConnection?.off('GameDeleted');
    this.signalRService.hubConnection?.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      // This game was deleted out from under us → send everyone back to the list.
      if (String(data) === String(this.gameId)) {
        this.zone.run(() => this.router.navigate([RouteNames.GamesList]));
      }
    });

    // RESYNC AFTER A RECONNECT — updates sent while the socket was down are gone, so seat
    // changes (and a game that started without us) would otherwise be missed silently.
    // updateGame() already navigates away if the game no longer exists.
    this.unsubscriberService.takeUntilDestroy(this.signalRService.reconnected$).subscribe(() => {
      console.log('[resync] reconnected — refetching game');
      this.zone.run(() => this.updateGame());
    });



  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection?.off('GameUpdated');
    this.signalRService.hubConnection?.off('GamesUpdated');
    this.signalRService.hubConnection?.off('GameDeleted');
    this.signalRService.unwatchGame(this.gameId);
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
      // Seed the status baseline so we don't treat an already-running game as a fresh start.
      this.lastStatus = String(this.gameData.gameStatus);
      this.loadHeroThumbs();
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
    if (!this.seatsReady) return; // not all mandatory seats are filled yet
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
