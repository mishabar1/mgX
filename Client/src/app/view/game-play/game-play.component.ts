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
import * as THREE from 'three';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader.js';
// import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
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
  get isResistance(): boolean { return String(this.mgGame?.gameData?.gameType) === 'RESISTANCE'; }

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
        this.updateDmSelected(data);   // refresh the console's contextual section
        this.updateRollUi(data);       // dice-roll prompt / result toast
        this.updateResistanceConsole(data);   // The Resistance per-player panel
      });
    });

  }

  // Captured/score readout for chess & checkers (top-centre HUD).
  hud = '';
  private computeHud(data: any) {
    const type = String(data?.gameType);
    if (type !== 'CHESS' && type !== 'CHECKERS') { this.hud = ''; return; }

    const byColor: { [c: string]: any[] } = {};
    const walk = (it: any) => {
      if (!it) return;
      const a = it.attributes || {};
      if (a['piece'] && a['color']) (byColor[a['color']] = byColor[a['color']] || []).push(it);
      (it.items || []).forEach(walk);
    };
    walk(data?.table);

    if (type === 'CHESS') {
      const val: any = { pawn: 1, knight: 3, bishop: 3, rook: 5, queen: 9, king: 0 };
      const mat = (c: string) => (byColor[c] || []).reduce((s, it) => s + (val[it.attributes.piece] || 0), 0);
      const w = mat('white'), b = mat('black'), d = w - b;
      const adv = d > 0 ? `White +${d}` : d < 0 ? `Black +${-d}` : 'even';
      this.hud = `White ${w}  ·  Black ${b}   (${adv})`;
    } else { // CHECKERS
      const bk = (byColor['black'] || []).length, rd = (byColor['red'] || []).length;
      this.hud = `Black ${bk}  ·  Red ${rd}`;
    }
  }

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
      this.lastRollNonce = game.attributes?.['rollResult'] || '';   // don't replay last roll on load

      // If opening an already-finished game (e.g. to analyse it), show the result once.
      this.lastStatus = String(game.gameStatus);
      this.endMessage = this.lastStatus === 'ENDED'
        ? ((game.attributes?.result) || 'Game over')
        : '';
      this.computeHud(game);

      this.mgThree=new MgThree();
      this.mgThree.initThree(this.rendererContainer.nativeElement,()=>{
        this.mgGame.loadGame(this.mgThree,this.generalService.User!);
        this.setupDmConsole();   // DM-only HTML control panel, mounted into the 3D scene
        this.setupResistanceConsole();   // The Resistance per-player console (no-op for other games)
        this.setupDemoConsole();   // Demo (dev reference) control panel (no-op for other games)
      });
    });
  }

  onVrClick() {
    this.mgThree.startVr();
  }

  // ---- DM console (CSS3D in-scene HTML panel) ------------------------------
  private dmPanelObj: any = null;

  private setupDmConsole() {
    const g: any = this.mgGame?.gameData;
    if (!g || String(g.gameType) !== 'DND') return;

    const me = this.generalService.User?.id;
    const dmSeat = (g.players || []).find((p: any) => p.attributes?.['type'] === 'dm' && p.user?.id === me);
    if (!dmSeat) return;                       // only the DM gets the console
    const dmSeatId = dmSeat.id;

    const scenes = this.parseCatalog(g.attributes?.['dndScenes']);
    const monsters = this.parseCatalog(g.attributes?.['dndMonsters']);
    const sounds = (g.attributes?.['dndSounds'] || '').split(';').filter(Boolean).map((p: string) => {
      const a = p.split('|'); return {label: a[0], url: a[1], loop: a[2] === '1'};
    });

    // A picture tile: scenes use their map PNG directly; monsters/heroes get a data-thumb the
    // model is rendered into after mount. Clicking a tile performs its action.
    const sceneTile = (s: any) =>
      `<div class="tile" data-act="LoadScene" data-url="${s.url}"><img src="/assets/games/${s.url}"><span>${s.label}</span></div>`;
    const monsterTile = (m: any) =>
      `<div class="tile" data-act="AddMonster" data-url="${m.url}"><img data-thumb="${m.url}"><span>${m.label}</span></div>`;
    const soundBtn = (s: any) =>
      `<button class="dmbtn" data-act="PlaySound" data-url="${s.url}" data-loop="${s.loop ? '1' : '0'}">${s.loop ? '🎵' : '🔊'} ${s.label}</button>`;

    const el = document.createElement('div');
    el.style.pointerEvents = 'auto';   // a docked HUD panel on the screen's right edge
    el.innerHTML = `
      <style>
        .dmc{width:340px;max-height:92vh;overflow-y:auto;font:600 16px system-ui,sans-serif;color:#e8edf5;
             background:linear-gradient(180deg,rgba(19,30,51,.97),rgba(10,17,32,.97));border:1px solid #2a3a55;
             border-radius:18px;padding:16px 18px;box-shadow:0 12px 48px rgba(0,0,0,.55);}
        .dmc h3{margin:0 0 12px;font-size:20px;}
        .dmc .row{margin-bottom:14px;}
        .dmc .lbl{font-size:12px;letter-spacing:.09em;color:#8aa0c0;text-transform:uppercase;margin-bottom:6px;}
        .dmc .pick{display:flex;gap:8px;flex-wrap:wrap;}
        .dmc .tile{width:78px;cursor:pointer;background:#0e1626;border:2px solid #2a3a55;border-radius:12px;
             padding:5px;text-align:center;transition:border-color .12s,transform .12s;}
        .dmc .tile:hover{border-color:#4a86e8;transform:translateY(-2px);}
        .dmc .tile img{width:66px;height:66px;object-fit:contain;border-radius:8px;display:block;background:#0a0f1a;}
        .dmc .tile span{display:block;font-size:12px;margin-top:4px;color:#cdd8ea;}
        .dmc .dmbtn{font:600 16px system-ui;color:#fff;background:#25406b;border:0;border-radius:10px;
             padding:8px 16px;margin:2px 6px 2px 0;cursor:pointer;}
        .dmc .dmbtn:hover{background:#31538c;}
        .dmc .dmbtn.on{background:#c99a00;color:#151515;}
      </style>
      <div class="dmc">
        <h3>🎲 DM Console</h3>
        <div class="row" id="dmSelected"></div>
        <div class="row"><div class="lbl">Scene</div><div class="pick">${scenes.map(sceneTile).join('')}</div></div>
        <div class="row"><div class="lbl">Add monster</div><div class="pick">${monsters.map(monsterTile).join('')}</div></div>
        <div class="row"><div class="lbl">Sound</div><div class="pick">${sounds.map(soundBtn).join('')}<button class="dmbtn" data-act="StopSound">⏹ Stop</button></div></div>
        <div class="row"><div class="lbl">All characters</div><div class="pick">
          <button class="dmbtn" data-act="ShowAllLabels">👁 Show labels</button>
          <button class="dmbtn" data-act="HideAllLabels">🚫 Hide labels</button>
          <button class="dmbtn" data-act="ClearAllRolls">🎲 Clear all dice</button>
        </div></div>
      </div>`;

    el.addEventListener('click', (ev: any) => {
      const t = ev.target.closest('[data-act]');
      if (!t || t.tagName === 'SELECT' || t.tagName === 'INPUT') return;   // selects/checkboxes fire via 'change'
      const act = t.getAttribute('data-act');
      if (!act) return;
      if (act === 'RemoveSelected' && !window.confirm('Remove this piece from the board?')) return;
      const args: any = {};
      if (act === 'LoadScene') args.sceneUrl = t.getAttribute('data-url');
      if (act === 'AddMonster') args.monsterUrl = t.getAttribute('data-url');
      if (act === 'PlaySound') { args.soundUrl = t.getAttribute('data-url'); args.loop = t.getAttribute('data-loop'); }
      if (act === 'SetHp') args.delta = t.getAttribute('data-delta');
      if (act === 'RotateSelected') args.delta = t.getAttribute('data-delta');
      if (act === 'RemoveRoll') args.idx = t.getAttribute('data-idx');
      if (act === 'AskRoll') args.seat = t.getAttribute('data-seat');
      if (act === 'SetDie') {
        args.sides = t.getAttribute('data-sides');
        el.querySelectorAll('.dmdice .dmbtn').forEach((b: any) => b.classList.remove('on'));
        t.classList.add('on');
      }
      this.signalRService.executeActionArgs(this.gameId!, dmSeatId, act, args);
    });

    // Dropdowns + the label checkbox fire 'change', not 'click'.
    el.addEventListener('change', (ev: any) => {
      const inp = ev.target.closest('input[data-act]');
      if (inp && inp.getAttribute('data-act') === 'ToggleLabel') {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'ToggleLabel', {});
        return;
      }
      const s = ev.target.closest('select[data-act]');
      if (!s) return;
      const act = s.getAttribute('data-act');
      if (act === 'SetAnim') {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'SetAnim', { idx: s.value });
      } else if (act === 'AskRoll' && s.value) {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'AskRoll', { seat: s.getAttribute('data-seat'), sides: s.value });
        s.value = '';   // reset so the DM can ask again
      } else if (act === 'RollSelected' && s.value) {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'RollSelected', { sides: s.value });
        s.value = '';
      }
    });

    // Dock into the screen-anchored "onscreen" holder — stays fixed on the right as the camera moves.
    this.mgThree.onscreenHolder?.appendChild(el);
    this.dmConsoleEl = el;
    this.dmConsoleSeat = dmSeatId;
    this.updateDmSelected(g);   // fill the contextual section for any current selection

    // Render the model thumbnails (monsters + heroes) into their tiles, one by one.
    setTimeout(async () => {
      const imgs = Array.from(el.querySelectorAll('img[data-thumb]')) as HTMLImageElement[];
      for (const img of imgs) {
        const u = img.getAttribute('data-thumb'); if (!u) continue;
        const data = await this.mgThree.renderModelThumbnail(u);
        if (data) img.src = data;
      }
    }, 0);
  }

  // ==================== The Resistance console ====================
  private resConsoleEl?: HTMLElement;
  private resSeatId = '';
  private lastResKey = '';

  // Team size per mission (1..5) by player count — must match the server table.
  private static RES_TEAM: { [n: number]: number[] } = {
    5: [2, 3, 2, 3, 3], 6: [2, 3, 4, 3, 4], 7: [2, 3, 3, 4, 4],
    8: [3, 4, 4, 5, 5], 9: [3, 4, 4, 5, 5], 10: [3, 4, 4, 5, 5],
  };
  private resTeamSize(n: number, mission: number): number {
    const row = GamePlayComponent.RES_TEAM[Math.min(10, Math.max(5, n))];
    return row[Math.min(5, Math.max(1, mission)) - 1];
  }
  private resImg(file: string) { return `/assets/games/resistance/${file}`; }

  private setupResistanceConsole() {
    const g: any = this.mgGame?.gameData;
    if (!g || String(g.gameType) !== 'RESISTANCE') return;

    const el = document.createElement('div');
    el.style.pointerEvents = 'auto';
    el.innerHTML = `
      <style>
        .resc{width:min(560px,100%);margin:0 auto 24px;box-sizing:border-box;font:600 16px system-ui,sans-serif;color:#e8edf5;
             background:linear-gradient(180deg,rgba(22,17,14,.98),rgba(12,9,7,.98));border:1px solid #5a4632;
             border-radius:18px;padding:16px 18px;box-shadow:0 12px 48px rgba(0,0,0,.6);}
        .resc .topbar{display:flex;justify-content:flex-start;margin-bottom:10px;}
        .resc .topbar .btn{background:#3a2c1e;padding:7px 14px;font-size:14px;margin:0;}
        .resc h3{margin:0 0 4px;font-size:22px;letter-spacing:.04em;}
        .resc .phase{color:#d9b98a;font-size:13px;letter-spacing:.08em;text-transform:uppercase;margin-bottom:12px;}
        .resc .card{border-radius:10px;border:1px solid #5a4632;overflow:hidden;background:#0a0705;}
        .resc .rolebox{display:flex;gap:12px;align-items:center;margin-bottom:12px;}
        .resc .rolebox img{width:82px;height:auto;border-radius:8px;display:block;}
        .resc .rname{font-size:18px;font-weight:800;}
        .resc .res{color:#5fd08a;} .resc .spy{color:#ff6b6b;}
        .resc .mates{font-size:13px;color:#ffb0b0;margin-top:4px;}
        .resc .track{display:flex;gap:6px;margin:10px 0;}
        .resc .pill{flex:1;text-align:center;padding:7px 0;border-radius:8px;background:#241a12;border:1px solid #5a4632;font-size:13px;}
        .resc .pill.cur{outline:2px solid #d9b98a;}
        .resc .pill.s{background:#123a20;border-color:#2f7a45;} .resc .pill.f{background:#3a1414;border-color:#7a2f2f;}
        .resc .vt{margin:8px 0;font-size:13px;color:#cbb493;}
        .resc .dot{display:inline-block;width:11px;height:11px;border-radius:50%;margin-right:4px;background:#3a2c1e;border:1px solid #5a4632;}
        .resc .dot.on{background:#ff6b6b;border-color:#ff6b6b;}
        .resc .lead{font-size:14px;margin:6px 0 12px;color:#e8dcc6;}
        .resc .ctrl{margin:8px 0;}
        .resc .btn{font:700 15px system-ui;color:#fff;background:#6a4a25;border:0;border-radius:10px;padding:10px 16px;margin:4px 8px 4px 0;cursor:pointer;}
        .resc .btn:hover{background:#8a6431;}
        .resc .btn.ok{background:#2f7a45;} .resc .btn.ok:hover{background:#3a9455;}
        .resc .btn.no{background:#7a2f2f;} .resc .btn.no:hover{background:#984040;}
        .resc .btn:disabled{opacity:.4;cursor:default;}
        .resc .pl{display:flex;align-items:center;gap:6px;padding:4px 0;font-size:15px;}
        .resc .pl input{width:17px;height:17px;}
        .resc .team{color:#ffe0a8;font-weight:700;}
        .resc .wait{color:#b7a488;font-size:14px;font-style:italic;}
        .resc .banner{font-size:20px;font-weight:800;text-align:center;padding:14px;border-radius:12px;margin:8px 0;}
        .resc .log{margin-top:12px;border-top:1px solid #5a4632;padding-top:8px;max-height:180px;overflow-y:auto;
             font:500 12px ui-monospace,monospace;color:#b7a488;white-space:pre-wrap;}
        .resc .voteimg{height:74px;border-radius:8px;cursor:pointer;border:2px solid transparent;}
        .resc .voteimg:hover{border-color:#d9b98a;}
      </style>
      <div class="resc">
        <div class="topbar"><button class="btn" data-act="Leave">← Games</button></div>
        <div id="resBody"></div>
      </div>`;

    el.addEventListener('click', (ev: any) => {
      const t = ev.target.closest('[data-act]');
      if (!t) return;
      const act = t.getAttribute('data-act');
      if (act === 'Leave') { this.zone.run(() => this.router.navigate([RouteNames.GamesList])); return; }
      if (!this.resSeatId) return;   // spectators can't act
      const args: any = {};
      if (act === 'ProposeTeam') {
        const boxes = Array.from(el.querySelectorAll('input[data-seat]:checked')) as HTMLInputElement[];
        const need = Number(t.getAttribute('data-need'));
        if (boxes.length !== need) { window.alert(`Pick exactly ${need} players.`); return; }
        args.team = boxes.map(b => b.getAttribute('data-seat')).join(',');
      }
      if (act === 'Vote') args.vote = t.getAttribute('data-vote');
      if (act === 'Mission') args.card = t.getAttribute('data-card');
      this.signalRService.executeActionArgs(this.gameId!, this.resSeatId, act, args);
    });

    // Phone-friendly: this game is pure cards/buttons, so the 2D panel IS the game. Mount it as a
    // full-screen, scrollable, centered overlay that covers the 3D scene (rather than the small
    // right-edge HUD used by the board games).
    const wrap = document.createElement('div');
    wrap.style.cssText = 'position:absolute;inset:0;z-index:30;overflow-y:auto;pointer-events:auto;'
      + 'display:flex;justify-content:center;align-items:flex-start;box-sizing:border-box;padding:10px;'
      + 'background:radial-gradient(circle at 50% -10%,#2a2018,#0b0806);';
    wrap.appendChild(el);
    this.rendererContainer.nativeElement.appendChild(wrap);
    this.resConsoleEl = el;
    this.updateResistanceConsole(g);
  }

  private updateResistanceConsole(g: any) {
    if (!this.resConsoleEl || !g || String(g.gameType) !== 'RESISTANCE') return;
    const body = this.resConsoleEl.querySelector('#resBody') as HTMLElement | null;
    if (!body) return;

    const me = this.generalService.User?.id;
    const occ = (g.players || []).filter((p: any) => p.type !== 'EMPTY_SEAT');
    const mySeat = occ.find((p: any) => p.user?.id === me);
    this.resSeatId = mySeat?.id || '';
    const a = g.attributes || {};

    const phase = a['phase'] || 'reveal';
    const mnum = Number(a['mnum'] || 1);
    const leader = a['leader'] || '';
    const voteTrack = Number(a['voteTrack'] || 0);
    const results = (a['results'] || '').split(',').filter(Boolean);
    const team = (a['team'] || '').split(',').filter(Boolean);
    const n = occ.length;
    const over = !!a['over'];

    // rebuild only when something relevant changed (so leader's checkbox picks survive)
    const votes = occ.filter((p: any) => a['vote:' + p.id]).length;
    const mcards = team.filter((id: string) => a['mcard:' + id]).length;
    const key = [phase, mnum, leader, voteTrack, results.length, team.join('|'), votes, mcards,
                 a['ack:' + this.resSeatId] || '', a['vote:' + this.resSeatId] || '',
                 a['mcard:' + this.resSeatId] || '', over ? a['result'] : '', (a['log'] || '').length].join('#');
    if (key === this.lastResKey) return;
    this.lastResKey = key;

    const nameOf = (id: string) => { const p = occ.find((x: any) => x.id === id); return p ? this.pname(p) : '?'; };
    const myRole = this.resSeatId ? (a['role:' + this.resSeatId] || 'resistance') : '';
    const iAmSpy = myRole === 'spy';
    const need = this.resTeamSize(n, mnum);

    // --- role box ---
    let roleHtml = '';
    if (this.resSeatId) {
      const card = a['card:' + this.resSeatId];
      const spymates = iAmSpy
        ? occ.filter((p: any) => p.id !== this.resSeatId && a['role:' + p.id] === 'spy').map((p: any) => this.pname(p))
        : [];
      roleHtml = `
        <div class="rolebox">
          ${card ? `<img class="card" src="${this.resImg(card)}">` : ''}
          <div>
            <div class="rname ${iAmSpy ? 'spy' : 'res'}">You are ${iAmSpy ? 'a SPY' : 'RESISTANCE'}</div>
            ${iAmSpy ? `<div class="mates">Fellow spies: ${spymates.length ? spymates.join(', ') : '— (you work alone)'}</div>`
                     : `<div class="mates" style="color:#8fbfa0;">Complete 3 missions to win.</div>`}
          </div>
        </div>`;
    } else {
      roleHtml = `<div class="wait" style="margin-bottom:10px;">Spectating — roles hidden.</div>`;
    }

    // --- mission track ---
    const pills = [];
    for (let m = 1; m <= 5; m++) {
      const sz = this.resTeamSize(n, m);
      const two = (m === 4 && n >= 7) ? '‼' : '';
      let cls = 'pill';
      if (m <= results.length) cls += results[m - 1] === 'S' ? ' s' : ' f';
      else if (m === mnum && !over) cls += ' cur';
      const mark = m <= results.length ? (results[m - 1] === 'S' ? '✔' : '✖') : `${sz}${two}`;
      pills.push(`<div class="${cls}">${mark}</div>`);
    }
    const trackHtml = `<div class="track">${pills.join('')}</div>`;

    // --- vote track ---
    const dots = [0, 1, 2, 3, 4].map(i => `<span class="dot ${i < voteTrack ? 'on' : ''}"></span>`).join('');
    const vtHtml = `<div class="vt">Rejected teams this round: ${dots} ${voteTrack}/5</div>`;

    const leadHtml = `<div class="lead">👑 Leader: <b>${nameOf(leader)}</b>${leader === this.resSeatId ? ' (you)' : ''}</div>`;

    // --- phase controls ---
    let ctrl = '';
    if (over) {
      const spiesWon = a['winnerRole'] === 'spy';
      ctrl = `<div class="banner ${spiesWon ? 'spy' : 'res'}" style="background:${spiesWon ? '#3a1414' : '#123a20'};">
                ${a['result'] || 'Game over'}</div>`;
    } else if (phase === 'reveal') {
      if (!this.resSeatId) ctrl = `<div class="wait">Waiting for players to review their roles…</div>`;
      else if (a['ack:' + this.resSeatId] === '1') {
        const ready = occ.filter((p: any) => a['ack:' + p.id] === '1').length;
        ctrl = `<div class="wait">Waiting for players… (${ready}/${n} ready)</div>`;
      } else ctrl = `<div class="ctrl"><button class="btn ok" data-act="Ready">I've seen my role — Ready</button></div>`;
    } else if (phase === 'team') {
      if (leader === this.resSeatId) {
        const rows = occ.map((p: any) =>
          `<label class="pl"><input type="checkbox" data-seat="${p.id}"> ${this.pname(p)}${p.id === this.resSeatId ? ' (you)' : ''}</label>`).join('');
        ctrl = `<div class="ctrl"><div style="margin-bottom:6px;">Pick <b>${need}</b> players for mission ${mnum}:</div>
                  ${rows}
                  <button class="btn ok" data-act="ProposeTeam" data-need="${need}" style="margin-top:8px;">Propose team</button></div>`;
      } else {
        ctrl = `<div class="wait">Waiting for <b>${nameOf(leader)}</b> to propose a team of ${need}…</div>`;
      }
    } else if (phase === 'vote') {
      const teamNames = team.map(nameOf).join(', ');
      let box = `<div class="ctrl">Proposed team: <span class="team">${teamNames}</span></div>`;
      if (this.resSeatId && !a['vote:' + this.resSeatId]) {
        box += `<div class="ctrl">
                  <img class="voteimg" data-act="Vote" data-vote="approve" src="${this.resImg('support-en.jpg')}" title="Approve">
                  <img class="voteimg" data-act="Vote" data-vote="reject" src="${this.resImg('reject-en.jpg')}" title="Reject">
                </div>`;
      } else if (this.resSeatId) {
        box += `<div class="wait">You voted <b>${a['vote:' + this.resSeatId]}</b>. Waiting… (${votes}/${n} voted)</div>`;
      } else {
        box += `<div class="wait">Voting… (${votes}/${n})</div>`;
      }
      ctrl = box;
    } else if (phase === 'mission') {
      const teamNames = team.map(nameOf).join(', ');
      let box = `<div class="ctrl">On mission: <span class="team">${teamNames}</span></div>`;
      const onTeam = team.indexOf(this.resSeatId) >= 0;
      if (onTeam && !a['mcard:' + this.resSeatId]) {
        box += `<div class="ctrl">
                  <img class="voteimg" data-act="Mission" data-card="success" src="${this.resImg('succeed-en.jpg')}" title="Success">
                  ${iAmSpy ? `<img class="voteimg" data-act="Mission" data-card="fail" src="${this.resImg('fail-en.jpg')}" title="Sabotage">` : ''}
                </div>`;
        if (!iAmSpy) box += `<div class="wait">Resistance must support the mission.</div>`;
      } else if (onTeam) {
        box += `<div class="wait">Card submitted. Waiting… (${mcards}/${team.length})</div>`;
      } else {
        box += `<div class="wait">Mission underway… (${mcards}/${team.length} cards in)</div>`;
      }
      ctrl = box;
    }

    // --- log ---
    const log = a['log'] || '';
    const logHtml = log ? `<div class="log">${this.esc(log)}</div>` : '';

    body.innerHTML = `
      <h3>THE RESISTANCE</h3>
      <div class="phase">Mission ${mnum} · ${this.resPhaseLabel(phase)}</div>
      ${roleHtml}
      ${trackHtml}
      ${vtHtml}
      ${leadHtml}
      ${ctrl}
      ${logHtml}`;

    // keep the log scrolled to the newest line
    const logEl = body.querySelector('.log') as HTMLElement | null;
    if (logEl) logEl.scrollTop = logEl.scrollHeight;
  }

  private resPhaseLabel(p: string) {
    return ({ reveal: 'Review roles', team: 'Proposing team', vote: 'Vote on team', mission: 'On mission', over: 'Game over' } as any)[p] || p;
  }
  private esc(s: string) { return s.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' } as any)[c]); }

  // ==================== Demo (dev reference) control panel ====================
  // Shows how to build an on-screen HTML control panel and wire its buttons to server
  // [GameAction] methods via executeActionArgs(gameId, seatId, actionId, args). The server reads
  // the args with its Arg(data,"key") helper — no clicked 3D item is involved. Mirror this pattern
  // (and setupDmConsole / setupResistanceConsole) to give any game its own panel.
  private setupDemoConsole() {
    const g: any = this.mgGame?.gameData;
    if (!g || String(g.gameType) !== 'DEMO') return;

    // Which seat are we? Panel actions are dispatched "as" this seat. Fall back to the first
    // occupied seat so a solo tester always has one.
    const me = this.generalService.User?.id;
    const occ = (g.players || []).filter((p: any) => p.type !== 'EMPTY_SEAT');
    const seat = occ.find((p: any) => p.user?.id === me) || occ[0];
    if (!seat) return;
    const seatId = seat.id;

    const el = document.createElement('div');
    el.style.pointerEvents = 'auto';
    el.innerHTML = `
      <style>
        .democ{width:280px;font:600 15px system-ui,sans-serif;color:#e8edf5;
             background:linear-gradient(180deg,rgba(19,30,51,.97),rgba(10,17,32,.97));
             border:1px solid #2a3a55;border-radius:16px;padding:14px 16px;box-shadow:0 12px 48px rgba(0,0,0,.55);}
        .democ h3{margin:0 0 10px;font-size:18px;}
        .democ .lbl{font-size:12px;letter-spacing:.08em;color:#8aa0c0;text-transform:uppercase;margin:10px 0 4px;}
        .democ button{font:600 15px system-ui;color:#fff;background:#25406b;border:0;border-radius:9px;
             padding:8px 14px;margin:2px 6px 2px 0;cursor:pointer;}
        .democ button:hover{background:#31538c;}
        .democ input,.democ select{width:100%;box-sizing:border-box;padding:7px;border-radius:8px;
             border:1px solid #2a3a55;background:#0e1626;color:#e8edf5;margin-bottom:6px;}
      </style>
      <div class="democ">
        <h3>🧪 Demo panel</h3>
        <div class="lbl">Spawn a disc on the board</div>
        <select id="demoColor">
          <option value="">random colour</option>
          <option value="0xE03131">red</option>
          <option value="0x22C55E">green</option>
          <option value="0x2563EB">blue</option>
          <option value="0xF59E0B">amber</option>
          <option value="0x9333EA">purple</option>
        </select>
        <button data-act="PanelSpawn">➕ Spawn disc</button>
        <button data-act="PanelClear">🗑 Clear spawned</button>
        <div class="lbl">Drop a floating label</div>
        <input id="demoText" placeholder="type text…">
        <button data-act="PanelSay">💬 Add label</button>
        <div class="lbl">Music (looping sound)</div>
        <button data-act="PlayMusic">🎵 Play</button>
        <button data-act="StopMusic">⏹ Stop</button>
        <div class="lbl">Turn / end</div>
        <button data-act="EndTurn">⏭ End turn</button>
        <button data-act="EndDemo">🏁 End game</button>
      </div>`;

    el.addEventListener('click', (ev: any) => {
      const t = ev.target.closest('button[data-act]');
      if (!t) return;
      const act = t.getAttribute('data-act');
      const args: any = {};
      if (act === 'PanelSpawn') args.color = (el.querySelector('#demoColor') as HTMLSelectElement).value;
      if (act === 'PanelSay') args.text = (el.querySelector('#demoText') as HTMLInputElement).value;
      // THE dispatch: run a server [GameAction] by name, passing a key/value bag.
      this.signalRService.executeActionArgs(this.gameId!, seatId, act, args);
    });

    this.mgThree.onscreenHolder?.appendChild(el);   // dock into the screen-anchored HUD layer
  }

  private parseCatalog(s: string): {label: string, url: string}[] {
    return (s || '').split(';').filter(Boolean).map(pair => {
      const i = pair.indexOf('|');
      return {label: pair.slice(0, i), url: pair.slice(i + 1)};
    });
  }
  private pname(p: any) { return p.user?.name || p.name || (p.type === 'AI' ? 'AI' : 'open'); }

  // ---- contextual "selected item" section of the DM console ----------------
  private dmConsoleEl?: HTMLElement;
  private dmConsoleSeat = '';

  private findSelectedItem(item: any): any {
    if (!item) return null;
    if (item.attributes?.['selected'] === '1') return item;
    for (const c of (item.items || [])) { const r = this.findSelectedItem(c); if (r) return r; }
    return null;
  }

  private lastSelKey = '';

  // Refresh the console's contextual block for the selected piece. Guarded by a key so unrelated
  // game updates don't rebuild it (which would snap an open dropdown shut).
  private updateDmSelected(g: any) {
    if (!this.dmConsoleEl) return;
    const box = this.dmConsoleEl.querySelector('#dmSelected') as HTMLElement | null;
    if (!box) return;
    const sel = this.findSelectedItem(g?.table);
    const key = sel ? `${sel.id}:${sel.animationIdx}:${sel.attributes?.['hp']}:${sel.attributes?.['rolls']}:${sel.attributes?.['hidelabel']}` : '';
    if (key === this.lastSelKey) return;   // nothing relevant changed
    this.lastSelKey = key;
    if (!sel) { box.innerHTML = ''; return; }

    const isHero = sel.attributes?.['char'] === '1';
    const owner = sel.attributes?.['owner'];
    const ownerP = (g.players || []).find((p: any) => p.id === owner);
    const cls = ownerP?.attributes?.['hero'];
    const name = isHero ? (cls ? `${cls} · ${this.pname(ownerP)}` : this.pname(ownerP)) : 'Monster';

    const dd = 'font:600 15px system-ui;padding:8px;border-radius:10px;border:1px solid #2a3a55;background:#0e1626;color:#e8edf5;margin:2px 6px 2px 0;';

    // Roll dropdown. Heroes → ask that hero's player (AskRoll); monsters → the DM rolls (RollSelected).
    const dieOpts = ['<option value="">🎲 Roll a die…</option>']
      .concat([4, 6, 8, 10, 12, 20, 100].map(s => `<option value="${s}">d${s}</option>`)).join('');
    const rollDD = (isHero && owner)
      ? `<select data-act="AskRoll" data-seat="${owner}" style="${dd}">${dieOpts}</select>`
      : `<select data-act="RollSelected" style="${dd}">${dieOpts}</select>`;

    // Accumulated rolls for this character — each removable by the DM.
    const rolls = (sel.attributes?.['rolls'] || '').split(';').filter((x: string) => x);
    const rollsRow = rolls.length ? `
      <div style="margin:6px 0 2px;">
        <div class="lbl">Rolls</div>
        ${rolls.map((r: string, i: number) => { const pr = r.split(':'); return `
          <span style="display:inline-flex;align-items:center;gap:5px;background:#0e1626;border:1px solid #2a3a55;border-radius:9px;padding:5px 9px;margin:0 6px 6px 0;">
            <b style="color:#ffd166;font-size:17px;">${pr[1]}</b><span style="color:#8aa0c0;font-size:12px;">${pr[0]}</span>
            <span data-act="RemoveRoll" data-idx="${i}" style="cursor:pointer;color:#ff6b6b;font-weight:800;margin-left:2px;">✖</span>
          </span>`; }).join('')}
      </div>` : '';

    // Animation: a dropdown of the model's actual clips.
    const clips: any[] = (this.mgGame as any)?.allItems?.[sel.id]?.mesh?.userData?.['clips'] || [];
    const curIdx = sel.animationIdx ?? -1;
    const animOpts = ['<option value="-1">🎬 none</option>']
      .concat(clips.map((c: any, i: number) => `<option value="${i}" ${i === curIdx ? 'selected' : ''}>${c.name || ('Clip ' + i)}</option>`)).join('');

    const hp = sel.attributes?.['hp'];
    const maxhp = sel.attributes?.['maxhp'];
    const hpRow = hp != null ? `
      <div style="display:flex;align-items:center;gap:6px;margin:8px 0 4px;">
        <span style="color:#8aa0c0;font-size:13px;letter-spacing:.06em;margin-right:2px;">HP</span>
        <button class="dmbtn" data-act="SetHp" data-delta="-5" style="padding:4px 10px;">−5</button>
        <button class="dmbtn" data-act="SetHp" data-delta="-1" style="padding:4px 12px;">−</button>
        <span style="font-weight:800;font-size:20px;color:#ff6b6b;min-width:60px;text-align:center;">${hp}${maxhp ? ('<span style="color:#8aa0c0;font-size:14px;font-weight:600;">/' + maxhp + '</span>') : ''}</span>
        <button class="dmbtn" data-act="SetHp" data-delta="1" style="padding:4px 12px;">+</button>
        <button class="dmbtn" data-act="SetHp" data-delta="5" style="padding:4px 10px;">+5</button>
      </div>` : '';

    // Facing: nudge the figure's heading in 10° steps.
    const facingRow = `
      <div style="display:flex;align-items:center;gap:6px;margin:4px 0;">
        <span style="color:#8aa0c0;font-size:13px;letter-spacing:.06em;margin-right:2px;">Facing</span>
        <button class="dmbtn" data-act="RotateSelected" data-delta="10" style="padding:4px 12px;">⟲ −10°</button>
        <button class="dmbtn" data-act="RotateSelected" data-delta="-10" style="padding:4px 12px;">⟳ +10°</button>
      </div>`;

    box.innerHTML = `
      <div class="lbl">Selected — ${name}</div>
      ${hpRow}
      ${facingRow}
      ${rollsRow}
      <div class="pick" style="align-items:center;gap:8px;">
        ${rollDD}
        ${clips.length ? `<select data-act="SetAnim" style="${dd}">${animOpts}</select>` : ''}
        <label style="display:inline-flex;align-items:center;gap:5px;color:#cdd8ea;font-size:14px;cursor:pointer;">
          <input type="checkbox" data-act="ToggleLabel" ${sel.attributes?.['hidelabel'] === '1' ? '' : 'checked'}> label
        </label>
        <button class="dmbtn" data-act="ClearSelected">✖ Unselect</button>
        <button class="dmbtn" data-act="RemoveSelected">🗑 Remove</button>
      </div>`;
  }

  // ---- player dice-roll prompt + result toast (onscreen) -------------------
  private rollEl?: HTMLElement;
  private rolling = false;
  private myRollFinal: number | null = null;
  private lastRollNonce = '';

  private mySeatId(): string { return (this.mgGame as any)?.playerData?.id || ''; }

  private updateRollUi(g: any) {
    const host = this.rendererContainer?.nativeElement;
    if (!host || !g) return;
    const seat = this.mySeatId();

    // Pending roll prompt for ME?
    const pending = seat ? g.attributes?.['roll:' + seat] : null;
    if (pending && !this.rollEl && !this.rolling) this.showRollPrompt(parseInt(pending, 10) || 20);
    else if (!pending && this.rollEl && !this.rolling) this.hideRollPrompt();

    // A roll result (for everyone). Nonce-prefixed so repeats still fire. Announce + settle mine.
    const rr = g.attributes?.['rollResult'];
    if (rr && rr !== this.lastRollNonce) {
      this.lastRollNonce = rr;
      const parts = String(rr).split('|');   // nonce|seat|who|sides|result
      const rseat = parts[1], who = parts[2], sides = parts[3], result = parts[4];
      this.showRollToast(`${who} rolled d${sides} → ${result}`);
      if (rseat === seat) this.myRollFinal = parseInt(result, 10);
    }
  }

  private showRollPrompt(sides: number) {
    const host = this.rendererContainer.nativeElement as HTMLElement;
    const el = document.createElement('div');
    el.style.cssText = 'position:absolute;left:50%;bottom:9%;transform:translateX(-50%);z-index:40;pointer-events:auto;'
      + 'font:600 20px system-ui,sans-serif;color:#e8edf5;text-align:center;'
      + 'background:linear-gradient(180deg,rgba(19,30,51,.97),rgba(10,17,32,.97));border:1px solid #2a3a55;'
      + 'border-radius:18px;padding:18px 28px;box-shadow:0 12px 48px rgba(0,0,0,.6);';
    el.innerHTML = `
      <div style="font-size:14px;color:#8aa0c0;letter-spacing:.06em;margin-bottom:8px;">THE DM ASKS YOU TO ROLL</div>
      <div class="rollnum" style="font-size:64px;font-weight:800;line-height:1;margin-bottom:14px;color:#ffd166;">d${sides}</div>
      <button class="rollbtn" style="font:700 20px system-ui;color:#151515;background:#ffd166;border:0;border-radius:12px;padding:12px 32px;cursor:pointer;">🎲 Roll d${sides}</button>`;
    el.querySelector('.rollbtn')!.addEventListener('click', () => this.startRoll(sides));
    host.appendChild(el);
    this.rollEl = el;
  }

  private startRoll(sides: number) {
    if (!this.rollEl || this.rolling) return;
    this.rolling = true;
    const numEl = this.rollEl.querySelector('.rollnum') as HTMLElement;
    const btn = this.rollEl.querySelector('.rollbtn') as HTMLElement;
    if (btn) btn.style.display = 'none';
    const spin = setInterval(() => { numEl.textContent = String(1 + Math.floor(Math.random() * sides)); }, 70);
    const start = Date.now();
    this.signalRService.executeActionArgs(this.gameId!, this.mySeatId(), 'RollDice', {});
    const settle = setInterval(() => {
      const done = Date.now() - start >= 900 && this.myRollFinal != null;
      if (done || Date.now() - start > 4000) {
        clearInterval(spin); clearInterval(settle);
        if (this.myRollFinal != null) numEl.textContent = String(this.myRollFinal);
        setTimeout(() => { this.hideRollPrompt(); this.rolling = false; this.myRollFinal = null; }, 1500);
      }
    }, 80);
  }

  private hideRollPrompt() { this.rollEl?.remove(); this.rollEl = undefined; }

  private showRollToast(text: string) {
    const host = this.rendererContainer?.nativeElement as HTMLElement;
    if (!host) return;
    const t = document.createElement('div');
    t.textContent = '🎲 ' + text;
    t.style.cssText = 'position:absolute;left:50%;top:18px;transform:translateX(-50%);z-index:45;pointer-events:none;'
      + 'font:700 22px system-ui,sans-serif;color:#151515;background:#ffd166;border-radius:12px;padding:10px 24px;'
      + 'box-shadow:0 8px 30px rgba(0,0,0,.45);opacity:0;transition:opacity .2s;';
    host.appendChild(t);
    requestAnimationFrame(() => { t.style.opacity = '1'; });
    setTimeout(() => { t.style.opacity = '0'; setTimeout(() => t.remove(), 300); }, 3200);
  }

}
