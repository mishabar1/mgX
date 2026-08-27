import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges, ChangeDetectionStrategy, NgZone} from '@angular/core';
import {SignalrService} from '../../services/SignalrService';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {RouteNames} from '../../app-routing.module';
import {Router} from '@angular/router';
import {UserData} from '../../entities/user.data';
import {GeneralService} from '../../bl/general.service';
import {ConfirmationService} from 'primeng/api';
import {GAMES_BASE} from '../../bl/mg.three';
import {Subscription} from 'rxjs';

@Component({
    selector: 'app-games-list',
    templateUrl: './games-list.component.html',
    styleUrls: ['./games-list.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GamesListComponent  implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  games: GameData[]=[];
  private reconnectSub?: Subscription;
  gameTypes: {type: string, label: string, icon: string, image: string}[] = [];   // creatable games, from the server
  assetsBase = GAMES_BASE;   // cover images are hosted by the server
  user!:UserData;
  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private zone: NgZone,
              private confirmationService: ConfirmationService,
              private dalService: DALService) {
  }

  ngOnInit(): void {
    this.user = this.generalService.User!;
    this.updateGamesList();
    this.dalService.getGameTypes().subscribe(list => this.gameTypes = list);   // server-driven "Create" buttons

    // Subscribe to LIST pings only. This view has no use for any game's full state, and the
    // server no longer sends it here — see NotificationHub / SignalrService.
    this.signalRService.watchLobby();

    // SignalR callbacks fire OUTSIDE Angular's zone, so re-run the refresh inside
    // the zone — otherwise this.games updates but change detection never runs and
    // the list only repaints on the next in-app click (e.g. Refresh).
    this.signalRService.hubConnection?.on('GamesUpdated', data => {
      console.log('GamesUpdated', data);
      this.zone.run(() => this.updateGamesList());
    });

    this.signalRService.hubConnection?.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      this.zone.run(() => this.updateGamesList());
    });

    // RESYNC AFTER A RECONNECT — games created, started or deleted while the socket was down
    // never reach us, so the list would keep showing a stale world until the user hit Refresh.
    this.reconnectSub = this.signalRService.reconnected$.subscribe(() => {
      console.log('[resync] reconnected — refetching games list');
      this.zone.run(() => this.updateGamesList());
    });


  }
  ngAfterViewInit(): void {

  }

  ngOnChanges(changes: SimpleChanges): void {
  }

  ngOnDestroy(): void {
    this.reconnectSub?.unsubscribe();
    // These were never removed. `off(name)` clears ALL handlers for that name, so the leaked
    // 'GamesUpdated' handler kept firing updateGamesList() — a full HTTP refetch of every game
    // — long after this view was gone, on top of whatever the new view registered.
    this.signalRService.hubConnection?.off('GamesUpdated');
    this.signalRService.hubConnection?.off('GameDeleted');
    this.signalRService.unwatchLobby();
  }
  /**
   * Sign out: drop the token + cached user, close the hub socket, and go back to the login
   * screen. There was no logout path in the app at all before this — GeneralService.clearAuth()
   * existed but nothing ever called it — so the only way to "log out" was to clear localStorage
   * by hand, which is also why the same name coming back as a different user was easy to miss.
   */
  logout() {
    this.signalRService.stopConnection();
    this.generalService.clearAuth();
    this.router.navigate([RouteNames.Home]);
  }

  updateGamesList(){
    this.dalService.getGamesList().subscribe(list=>{
      this.games=list;
    })
  }
  createGame(gameType:string) {
    this.dalService.createGame(this.user.id,gameType).subscribe((game)=>{
      this.router.navigate([RouteNames.GameSetup,game.id]);
    });
  }

  // Result text (e.g. "White wins!" / "X wins!" / "It's a tie.") for finished games.
  endedResult(game: GameData): string {
    return String(game.gameStatus) === 'ENDED' ? (game.attributes?.result || 'Finished') : '';
  }

  // Participants, e.g. "A (white) vs AI (black)".
  playersLabel(game: GameData): string {
    return (game.players || [])
      .filter(p => p.type !== 'EMPTY_SEAT')          // hide untaken/open seats
      .map(p => {
        const who = p.user?.name || (p.type === 'AI' ? 'AI' : 'open');
        const seat = p.attributes?.['type'];
        return seat ? `${who} (${seat})` : who;
      }).join(' vs ');
  }

  setup(game: GameData) {
    this.router.navigate([RouteNames.GameSetup,game.id]);
  }

  // Click a game → jump to the play view if it's running or finished (to review),
  // otherwise to its settings.
  openGame(game: GameData) {
    const s = String(game.gameStatus);
    if (s === 'PLAY' || s === 'ENDED') {
      this.router.navigate([RouteNames.GamePlay, game.id]);
    } else {
      this.router.navigate([RouteNames.GameSetup, game.id]);
    }
  }



  // Housekeeping: clear the whole list. Same confirm treatment as a single delete, but it names
  // the count, because "delete all" with no number is exactly the prompt people click through.
  deleteAll() {
    const n = this.games.length;
    if (!n) return;
    this.confirmationService.confirm({
      header: 'Delete all games',
      message: `Delete all ${n} game${n === 1 ? '' : 's'} on the server? This can't be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: `Delete all ${n}`,
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-secondary p-button-outlined',
      accept: () => this.dalService.deleteAllGames().subscribe({
        // The server broadcasts a GameDeleted per game, which already refreshes the list — this
        // refetch is just belt-and-braces for a dropped socket.
        next: () => this.updateGamesList(),
        error: () => this.updateGamesList(),
      }),
    });
  }

  delete(game: GameData) {
    this.confirmationService.confirm({
      header: 'Delete game',
      message: `Delete "${game.name}"? This can't be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-secondary p-button-outlined',
      accept: () => this.dalService.deleteGame(game.id).subscribe()
    });
  }

  backClick() {
    this.router.navigate([RouteNames.Home]);
  }
}
