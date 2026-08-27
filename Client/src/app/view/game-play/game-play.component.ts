import {
  AfterViewInit,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  ChangeDetectionStrategy,
  NgZone
} from '@angular/core';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader.js';
// import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
import {MgPanel3d, PanelSource} from '../../bl/mg.panel3d';
import { HttpClient } from '@angular/common/http';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {ItemData} from '../../entities/item.data';
import * as _ from 'lodash';
import {debounce, filter, find, forEach, isEqual, keys} from 'lodash';
import * as dayjs from 'dayjs';
import {PlayerData} from '../../entities/player.data';
import {UserData} from '../../entities/user.data';
import {V3} from '../../entities/V3';
import {
  AnimationClip,
  AnimationMixer,
  BufferGeometry,
  Clock,
  Line, Loader,
  LoopOnce, MathUtils, Matrix4, Mesh, MeshBasicMaterial, PlaneGeometry, Raycaster, TextureLoader,
  Vector3,
  VectorKeyframeTrack
} from 'three';
import * as TWEEN from "@tweenjs/tween.js";
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory.js';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController.js';
import {ActivatedRoute, Router} from '@angular/router';
// removed: unused TSL import 'color' (path not present in three r157+)
import {RouteNames} from '../../app-routing.module';
import {environment} from '../../../environments/environment';
import {Group} from 'three/src/objects/Group.js';
import {GeneralService} from '../../bl/general.service';
import {UnsubscriberService} from '../../services/unsubscriber.service';
import {MgThree} from '../../bl/mg.three';
import {MgGame} from '../../bl/mg.game';
import {VoiceService} from '../../bl/voice.service';

@Component({
    selector: 'app-game-play',
    templateUrl: './game-play.component.html',
    styleUrls: ['./game-play.component.scss'],
    providers: [UnsubscriberService],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GamePlayComponent implements OnInit, OnDestroy, AfterViewInit {

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;

  gameId: string | null = "";

  mgThree!:MgThree;

  mgGame!:MgGame;

  endMessage = ''; // friendly "game over" message shown when the game ends
  private lastStatus = ''; // to only pop the overlay on the transition into ENDED

  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private zone: NgZone,
              private activatedRoute: ActivatedRoute,
              private unsubscriberService: UnsubscriberService,
              public voice: VoiceService,
              private dalService: DALService) {
  }

  // ---- voice chat ----
  // Is the current user a seated player (vs a spectator)? mgGame.playerData is set on load.
  get isPlayer(): boolean { return !!this.mgGame?.playerData; }

  /**
   * The server's hint that this seat's panel is content-heavy and wants the view to itself
   * (Resistance, One Night Werewolf, Small World's pick phase). Generic: an attribute, never a
   * game name.
   *
   * NOTE this no longer means "the panel covers the screen" — since the panel became geometry
   * docked to the edges of the view, nothing is covered. It now only trims the app's own bottom
   * chrome so a bottom-docked panel is not fighting it for the same strip of screen. The VR
   * button is deliberately NOT part of that: it lives outside this bar (see the template),
   * because a headset has to stay reachable in every game — which the old markup broke by
   * hiding the whole bar, VR button included, in exactly these games.
   */
  get panelFull(): boolean { return this.mgGame?.gameData?.attributes?.['panelMode'] === 'full'; }

  // Only the game's creator may restart it ("Play again").
  get isCreator(): boolean {
    const me = this.generalService.User?.id;
    return !!me && this.mgGame?.gameData?.creatorId === me;
  }

  // Undo is only for the player who made the last move — and only their own move.
  get canUndo(): boolean {
    const me = this.mgGame?.playerData?.id;
    const last = this.mgGame?.gameData?.attributes?.['lastHumanActor'];
    return !!me && !!last && me === last;
  }
  // Show the voice panel only if the game allows it AND (spectators allowed OR you're a player).
  get voiceAllowed(): boolean {
    const a = this.mgGame?.gameData?.attributes;
    if (!a || !a['allowVoice']) return false;
    return !!a['voiceSpectators'] || this.isPlayer;
  }
  joinVoice() { this.voice.join(this.gameId!, this.generalService.User?.name || 'player'); }
  leaveVoice() { this.voice.leave(); }
  toggleMute() { this.voice.toggleMute(); }

  ngOnInit() {

    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');
    console.log("ngOnInit", this.gameId);

    this.loadPanelPlacement();

    // Receive updates for THIS game only. Previously every client got every game's full state
    // and diffed it here before discarding it, which is what made a slow machine or a phone
    // stutter on other people's turns in games it wasn't even in.
    this.signalRService.watchGame(this.gameId);

    this.signalRService.hubConnection?.off('GameDeleted');
    this.signalRService.hubConnection?.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      // The game we're playing was deleted → return to the games list.
      if (String(data) === String(this.gameId)) {
        this.zone.run(() => this.router.navigate([RouteNames.GamesList]));
      }
    });

    this.signalRService.hubConnection?.off('GameUpdated');
    this.signalRService.hubConnection?.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      if (String(data?.id) !== String(this.gameId)) return;   // belt-and-braces; the server now sends only this game
      this.applyGameUpdate(data);
    });

    // RESYNC AFTER A RECONNECT. The socket comes back on its own; the updates it missed do not.
    // Refetch the authoritative state so the board can't sit silently stale after a tunnel, a
    // sleeping laptop or a dropped phone connection.
    this.unsubscriberService.takeUntilDestroy(this.signalRService.reconnected$).subscribe(() => {
      console.log('[resync] reconnected — refetching game state');
      this.dalService.getGameById(this.gameId!).subscribe(game => {
        if (!game) {
          // Deleted while we were away.
          this.zone.run(() => this.router.navigate([RouteNames.GamesList]));
          return;
        }
        this.applyGameUpdate(game);
      });
    });

  }

  /**
   * Apply one authoritative game state to the scene and the panel.
   *
   * Shared by the live GameUpdated push and by the post-reconnect refetch on purpose: a resync
   * then goes down exactly the same path as a normal update, instead of a second almost-identical
   * one that drifts out of step the first time either is touched.
   */
  private applyGameUpdate(data: any) {
    if (this.mgGame) this.mgGame.updateGame(data);
    // SignalR fires outside Angular's zone — run inside so the overlay renders.
    this.zone.run(() => {
      const status = String(data.gameStatus);
      // Pop the overlay when the game FIRST ends; clear it if we leave ENDED (e.g. undo).
      if (status === 'ENDED' && this.lastStatus !== 'ENDED') {
        this.endMessage = data.attributes?.result || 'Game over';
      } else if (status !== 'ENDED') {
        this.endMessage = '';
      }
      this.lastStatus = status;
      this.computeHud(data);
      this.updateServerPanel(data);   // generic server-driven panel (any game)
    });
  }

  // ---- panel placement (this viewer's preference, never the server's) ----
  //
  // 'default' = wherever each game asked for; 'screen' = force the view edge; 'world' = detach the
  // panel and leave it standing in the scene, pinned where the player was looking. Stored per
  // viewer in localStorage: it is a convenience, not game state, so it must not touch GameData.
  private static readonly PLACEMENT_KEY = 'mgx.panelPlacement';
  panelPlacement: 'default' | 'screen' | 'world' = 'default';

  private loadPanelPlacement() {
    try {
      const v = localStorage.getItem(GamePlayComponent.PLACEMENT_KEY);
      if (v === 'screen' || v === 'world' || v === 'default') this.panelPlacement = v;
    } catch { /* private window / storage blocked — the default is fine */ }
  }

  get panelPlacementLabel(): string {
    return this.panelPlacement === 'world' ? 'Panel: in world'
         : this.panelPlacement === 'screen' ? 'Panel: on screen'
         : 'Panel: auto';
  }
  get panelPlacementIcon(): string {
    return this.panelPlacement === 'world' ? 'pi pi-map-marker'
         : this.panelPlacement === 'screen' ? 'pi pi-window-maximize'
         : 'pi pi-th-large';
  }
  get panelPlacementHint(): string {
    return this.panelPlacement === 'world'
      ? 'Panel stands in the scene where you pinned it — the camera orbits around it'
      : this.panelPlacement === 'screen'
        ? 'Panel is pinned to the edge of your view'
        : 'Each game decides where its panels sit';
  }

  cyclePanelPlacement() {
    this.panelPlacement = this.panelPlacement === 'default' ? 'screen'
                        : this.panelPlacement === 'screen' ? 'world'
                        : 'default';
    try { localStorage.setItem(GamePlayComponent.PLACEMENT_KEY, this.panelPlacement); } catch { /* ignore */ }
    this.applyPanelPlacement();
    // Re-render the panel from the state we already hold; no server round-trip for a local choice.
    this.updateServerPanel(this.mgGame?.gameData);
  }

  private applyPanelPlacement() {
    this.panel3d?.setAnchorOverride(this.panelPlacement === 'default' ? null : this.panelPlacement);
  }

  // Top-centre HUD. The TEXT is decided by the server (attribute "hud"); the client only shows it.
  hud = '';
  private computeHud(data: any) { this.hud = String(data?.attributes?.['hud'] || ''); }

  backToList() {
    this.router.navigate([RouteNames.GamesList]);
  }

  undo() {
    this.dalService.undoGame(this.gameId!).subscribe();
  }

  // Reset the same game (Setup + Start) and keep playing. The fresh board arrives via
  // the GameUpdated broadcast, so the scene re-renders itself.
  playAgain() {
    this.endMessage = '';
    this.lastStatus = '';
    this.dalService.setupGame(this.gameId!, this.generalService.User!.id).subscribe(() => {
      this.dalService.startGame(this.gameId!).subscribe();
    });
  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection?.off('GameUpdated');
    this.signalRService.hubConnection?.off('GameDeleted');
    this.signalRService.unwatchGame(this.gameId);
    this.voice.leave();   // drop out of the voice call when leaving the game view
    this.disposePanel3d();   // unregister the in-scene panel's clickables before the scene goes
    this.mgThree?.dispose();
  }

  ngAfterViewInit(): void {

    this.dalService.getGameById(this.gameId!).subscribe(game => {
      if (!game) {
        this.router.navigate([RouteNames.GamesList]);
        return;
      }

      this.mgGame = new MgGame();
      this.mgGame.gameData = game;

      // A PANEL item is a uikit panel carried by the item tree, positioned by whatever holder it
      // sits in. Wired HERE, not in updatePanel3d: loadGame() below builds the item tree, so the
      // builder has to exist before the first PANEL item is created. The panel object itself is
      // still created lazily — the first call brings it up.
      this.mgGame.makeUiPanel = (nodes, worldWidth, seatId, interactive) => {
        if (!this.panel3d) this.updatePanel3d(this.mgGame?.gameData, []);
        return this.panel3d?.buildDetached(nodes, worldWidth, seatId, interactive) ?? null;
      };
      // Models arrive asynchronously, AFTER the panel has already been built from the server tree.
      // An "animpick" lists the clips found on the loaded mesh, so it has to be rebuilt once those
      // exist — otherwise it reads "model still loading" forever (which is exactly what happened
      // after a page refresh).
      this.mgGame.onMeshesReady = () => this.updateServerPanel(this.mgGame?.gameData);

      // If opening an already-finished game (e.g. to analyse it), show the result once.
      this.lastStatus = String(game.gameStatus);
      this.endMessage = this.lastStatus === 'ENDED'
        ? ((game.attributes?.result) || 'Game over')
        : '';
      this.computeHud(game);

      this.mgThree=new MgThree();
      this.mgThree.initThree(this.rendererContainer.nativeElement,()=>{
        this.mgGame.loadGame(this.mgThree,this.generalService.User!);
        this.setupServerPanel();   // generic server-driven panel: renders PlayerData.Screen for any game
      });
    });
  }

  onVrClick() {
    this.mgThree.startVr();
  }


  // ==================== Generic server-driven panel ====================
  // The client is DUMB. The server sends this seat's ENTIRE panel as PlayerData.Screen (a UiNode
  // tree) and this ONE renderer draws whatever it's given — for EVERY game. No game logic lives
  // here: not the texts, not the buttons, not the layout, not the rules. Swap this framework out
  // tomorrow and only mg.panel3d.ts is rewritten; the games never change.
  private panelSeatId = '';
  private panel3d?: MgPanel3d;

  // The control panel is drawn IN THE SCENE (see mg.panel3d.ts), so there is no HTML to build here
  // any more — this just kicks off the first panel render.
  //
  // REMOVED: this used to inject its own toolbar ("← Games" / "VR") as raw HTML at
  // top:10px/left:10px with z-index 30. Once the template grew a single top-left toolbar, the two
  // sat on the exact same coordinates and the injected one drew OVER the real buttons — the
  // overlapping labels and the "Panel:" button with no chrome. It existed only because the old
  // bottom bar was hidden for full-screen-panel games and VR had to stay reachable; the single bar
  // is never hidden, so both buttons now live there with everything else.
  private setupServerPanel() {
    this.updateServerPanel(this.mgGame?.gameData);
  }


  // Build/refresh the in-scene panel from the seat's Screen tree. Placement defaults to 'hud'
  // (parented to the camera, sized from its frustum) so EVERY game gets a usable panel with no
  // per-game setup; a game that wants the panel standing on the table sets panel3dAnchor /
  // panel3dRot / panel3dWidth and gets 'world' placement instead. Positioning is a SERVER
  // decision either way — the client only obeys.
  private updatePanel3d(g: any, sources: PanelSource[]): boolean {
    if (!this.mgThree) return false;
    if (!this.panel3d) {
      this.panel3d = new MgPanel3d(
        this.mgThree,
        (action: string, args: any) => {
          if (this.panelSeatId && action) this.signalRService.executeActionArgs(this.gameId!, this.panelSeatId, action, args);
        });
      // The panel parents ITSELF (the camera on screen, a controller in VR), so there is no
      // scene.add() here on purpose.
      //
      // It does have to be declared as an OCCLUDER, though: the panel is solid geometry sitting
      // between the eye and the board, and the InteractionManager (which owns board clicks) does
      // not otherwise know it exists — so a click on a button also selected the piece behind it.
      // BOTH panel roots occlude: screen panels hang off the camera, world panels off the scene,
      // and a click must not fall through either onto the board behind it.
      this.mgThree.uiBlockers = [this.panel3d.group, this.panel3d.worldGroup];
      // ...and hand the VR ray the widgets themselves, which it cannot discover by traversal.
      this.mgThree.uiHitTargets = () => this.panel3d?.hitTargets() ?? [];
      // uikit computes layout and flushes transforms once per frame, so it needs a slot in the
      // render loop. mg.three isolates each frame hook so a throw can never skip the render.
      this.panel3d.frameHook = (deltaMs: number) => this.panel3d?.tick(deltaMs);
      // animpick reads the animation clips off the already-loaded model.
      this.panel3d.items = () => (this.mgGame as any)?.allItems || {};
      // 'item3d' nodes: the panel positions geometry, MgGame knows how to build it from an asset.
      this.panel3d.makeItem = (item: any) => this.mgGame?.buildPanelItem(item) ?? null;
      this.mgThree.addFrameHook?.(this.panel3d.frameHook);
      // A purely local widget change (e.g. ticking one box of a "checks" group) has no server
      // round-trip, so the panel asks us to re-render it from the state we already hold.
      this.panel3d.onNeedRebuild = () => this.updateServerPanel(this.mgGame?.gameData);
      // Desktop <-> VR: on the table, or carried on the left controller.
      this.mgThree.onXrSessionChange = (presenting: boolean) => {
        const ctrl = presenting ? (this.mgThree.leftController?.() || null) : null;
        this.panel3d?.attachTo(ctrl);
        // Items too: a "hand"-anchored holder (Durak's hand) moves between the left controller and
        // the camera with the session.
        this.mgGame?.reattachHandAnchors();
      };
    }
    // Placement is entirely the client's business: panels dock to the edges of the view on screen,
    // stand in the scene when world-anchored, and ride the player's hand in VR. The server only
    // names a default; the viewer's own preference wins.
    this.applyPanelPlacement();
    return this.panel3d.update(sources, this.panelSeatId);
  }

  private disposePanel3d() {
    if (!this.panel3d) return;
    if (this.mgThree) {
      this.mgThree.uiBlockers = [];                  // stop occluding once the panel is gone
      this.mgThree.uiHitTargets = undefined;
    }
    this.panel3d.dispose();
    this.panel3d = undefined;
    if (this.mgThree) this.mgThree.onXrSessionChange = undefined;
  }

  private updateServerPanel(g: any) {
    if (!g) return;
    // Hand EVERY seat's Screen tree to the in-scene renderer, tagged with whose it is. My own seat
    // is interactive; the others are not, and the renderer keeps only the panels they published to
    // the table (world-anchored + public) — which is how you see another player holding cards.
    // There is no HTML path any more and deliberately no fallback: one renderer means a panel bug
    // shows up as a panel bug instead of silently reverting to a different UI.
    const me = this.generalService.User?.id;
    const seats = (g.players as PlayerData[]) || [];
    const mine = seats.find(p => p.user?.id === me && p.screen);
    // Fall back to my own seat: a panel carried by an ITEM dispatches through here too, and a game
    // may hand out no PlayerData.Screen at all (the holder demo does not). Without this the action
    // went out with an empty playerId and the server rightly refused it.
    this.panelSeatId = mine?.id || this.mgGame?.playerData?.id || '';

    const sources = seats
      .filter(p => p.screen && p.screen.length)
      .map(p => ({ seatId: p.id, screen: p.screen!, interactive: !!me && p.user?.id === me }));
    this.updatePanel3d(g, sources);
  }


}
