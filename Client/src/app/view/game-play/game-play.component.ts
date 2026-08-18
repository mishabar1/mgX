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
import {MgPanel3d} from '../../bl/mg.panel3d';
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

  // The Resistance is a pure 2D card/vote game — no 3D scene, so hide 3D-only controls (VR).
  // A full-screen server-driven panel (e.g. The Resistance) owns the whole view — hide the 3D
  // play chrome (VR button, bottom bar). Driven by the server's panelMode, not by game type.
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

    this.signalRService.hubConnection.off('GameDeleted');
    this.signalRService.hubConnection.on('GameDeleted', data => {
      console.log('GameDeleted', data);
      // The game we're playing was deleted → return to the games list.
      if (String(data) === String(this.gameId)) {
        this.zone.run(() => this.router.navigate([RouteNames.GamesList]));
      }
    });

    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      if (String(data?.id) !== String(this.gameId)) return;   // broadcast is Clients.All — ignore other games
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
    });

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
    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.off('GameDeleted');
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
  private panelEl?: HTMLElement;
  private panelWrap?: HTMLElement;
  private panelSeatId = '';
  private panel3d?: MgPanel3d;

  // The control panel is drawn IN THE SCENE (see mg.panel3d.ts). The only HTML left here is the
  // app's own chrome — a "back to games" button — which is navigation, not game content, and so
  // has no business being geometry. Everything the SERVER sends for a seat goes to the 3D panel.
  private setupServerPanel() {
    const el = document.createElement('div');
    el.style.pointerEvents = 'auto';
    // Always-on chrome. This deliberately does NOT live in the template's bottom bar, which is
    // hidden for a full-screen panel (see panelFull) — VR has to stay reachable in every game.
    el.innerHTML = `<style>
        .sp-chrome{position:absolute;top:10px;left:10px;z-index:30;display:flex;gap:8px;}
        .sp-chrome button{font:600 15px system-ui,sans-serif;color:#e8edf5;background:rgba(12,9,7,.92);
          border:1px solid #5a4632;border-radius:10px;padding:8px 14px;cursor:pointer;}
        .sp-chrome button:hover{background:rgba(32,24,18,.96);}
        .sp-chrome button.vr{border-color:#2b6cb0;}
      </style><div class="sp-chrome">
        <button data-leave="1">← Games</button>
        <button data-vr="1" class="vr" title="Enter VR">VR</button>
      </div>`;

    el.addEventListener('click', (ev: any) => {
      if (ev.target.closest('[data-leave]')) { this.zone.run(() => this.router.navigate([RouteNames.GamesList])); return; }
      if (ev.target.closest('[data-vr]')) this.zone.run(() => this.onVrClick());
    });

    this.panelWrap = document.createElement('div');
    this.panelWrap.appendChild(el);
    this.rendererContainer.nativeElement.appendChild(this.panelWrap);
    this.panelEl = el;
    this.updateServerPanel(this.mgGame?.gameData);
  }


  // Build/refresh the in-scene panel from the seat's Screen tree. Placement defaults to 'hud'
  // (parented to the camera, sized from its frustum) so EVERY game gets a usable panel with no
  // per-game setup; a game that wants the panel standing on the table sets panel3dAnchor /
  // panel3dRot / panel3dWidth and gets 'world' placement instead. Positioning is a SERVER
  // decision either way — the client only obeys.
  private updatePanel3d(g: any, screen: any): boolean {
    if (!this.mgThree) return false;
    if (!this.panel3d) {
      this.panel3d = new MgPanel3d(
        this.mgThree,
        (action: string, args: any) => {
          if (this.panelSeatId && action) this.signalRService.executeActionArgs(this.gameId!, this.panelSeatId, action, args);
        });
      // The panel parents ITSELF (camera for 'hud', scene for 'world', a controller in VR), so
      // there is no scene.add() here on purpose.
      // uikit computes layout and flushes transforms once per frame, so it needs a slot in the
      // render loop. mg.three isolates each frame hook so a throw can never skip the render.
      this.panel3d.frameHook = (deltaMs: number) => this.panel3d?.tick(deltaMs);
      // animpick reads the animation clips off the already-loaded model.
      this.panel3d.items = () => (this.mgGame as any)?.allItems || {};
      this.mgThree.addFrameHook?.(this.panel3d.frameHook);
      // A purely local widget change (e.g. ticking one box of a "checks" group) has no server
      // round-trip, so the panel asks us to re-render it from the state we already hold.
      this.panel3d.onNeedRebuild = () => this.updateServerPanel(this.mgGame?.gameData);
      // Desktop <-> VR: on the table, or carried on the left controller.
      this.mgThree.onXrSessionChange = (presenting: boolean) => {
        const ctrl = presenting ? (this.mgThree.controllers?.[0] || null) : null;
        this.panel3d?.attachTo(ctrl);
      };
    }
    // Placement is entirely the client's business now: panels dock to the edges of the view on
    // screen, and to the player's hand in VR. The server only names the edge, per panel.
    return this.panel3d.update(screen, this.panelSeatId);
  }

  private disposePanel3d() {
    if (!this.panel3d) return;
    this.panel3d.dispose();
    this.panel3d = undefined;
    if (this.mgThree) this.mgThree.onXrSessionChange = undefined;
  }

  private updateServerPanel(g: any) {
    if (!g) return;
    // Find THIS user's seat and hand its Screen tree to the in-scene renderer. There is no HTML
    // path any more and deliberately no fallback: one renderer means a panel bug shows up as a
    // panel bug instead of silently reverting to a different UI.
    const me = this.generalService.User?.id;
    const mine = (g.players || []).find((p: any) => p.user?.id === me && p.screen);
    this.panelSeatId = mine?.id || '';
    this.updatePanel3d(g, mine?.screen || null);
  }


}
