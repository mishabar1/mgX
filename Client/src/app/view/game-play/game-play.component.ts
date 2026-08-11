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
      </div>`;

    el.addEventListener('click', (ev: any) => {
      const t = ev.target.closest('[data-act]');
      if (!t || t.tagName === 'SELECT') return;   // selects fire via 'change', not click
      const act = t.getAttribute('data-act');
      if (!act) return;
      const args: any = {};
      if (act === 'LoadScene') args.sceneUrl = t.getAttribute('data-url');
      if (act === 'AddMonster') args.monsterUrl = t.getAttribute('data-url');
      if (act === 'PlaySound') { args.soundUrl = t.getAttribute('data-url'); args.loop = t.getAttribute('data-loop'); }
      if (act === 'AskRoll') args.seat = t.getAttribute('data-seat');
      if (act === 'SetDie') {
        args.sides = t.getAttribute('data-sides');
        el.querySelectorAll('.dmdice .dmbtn').forEach((b: any) => b.classList.remove('on'));
        t.classList.add('on');
      }
      this.signalRService.executeActionArgs(this.gameId!, dmSeatId, act, args);
    });

    // Dropdowns (animation picker, ask-roll die picker) fire 'change', not 'click'.
    el.addEventListener('change', (ev: any) => {
      const s = ev.target.closest('select[data-act]');
      if (!s) return;
      const act = s.getAttribute('data-act');
      if (act === 'SetAnim') {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'SetAnim', { idx: s.value });
      } else if (act === 'AskRoll' && s.value) {
        this.signalRService.executeActionArgs(this.gameId!, dmSeatId, 'AskRoll', { seat: s.getAttribute('data-seat'), sides: s.value });
        s.value = '';   // reset so the DM can ask again
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
    const key = sel ? `${sel.id}:${sel.animationIdx}` : '';
    if (key === this.lastSelKey) return;   // nothing relevant changed
    this.lastSelKey = key;
    if (!sel) { box.innerHTML = ''; return; }

    const isHero = sel.attributes?.['char'] === '1';
    const owner = sel.attributes?.['owner'];
    const ownerP = (g.players || []).find((p: any) => p.id === owner);
    const name = isHero ? (ownerP?.attributes?.['hero'] || this.pname(ownerP)) : 'Monster';

    const dd = 'font:600 15px system-ui;padding:8px;border-radius:10px;border:1px solid #2a3a55;background:#0e1626;color:#e8edf5;margin:2px 6px 2px 0;';

    // Ask-to-roll: a die dropdown (asks THIS hero's player to roll the chosen die).
    const dieOpts = ['<option value="">🎲 Ask to roll…</option>']
      .concat([4, 6, 8, 10, 12, 20, 100].map(s => `<option value="${s}">d${s}</option>`)).join('');

    // Animation: a dropdown of the model's actual clips.
    const clips: any[] = (this.mgGame as any)?.allItems?.[sel.id]?.mesh?.userData?.['clips'] || [];
    const curIdx = sel.animationIdx ?? -1;
    const animOpts = ['<option value="-1">🎬 none</option>']
      .concat(clips.map((c: any, i: number) => `<option value="${i}" ${i === curIdx ? 'selected' : ''}>${c.name || ('Clip ' + i)}</option>`)).join('');

    box.innerHTML = `
      <div class="lbl">Selected — ${name}</div>
      <div class="pick" style="align-items:center;gap:8px;">
        ${isHero && owner ? `<select data-act="AskRoll" data-seat="${owner}" style="${dd}">${dieOpts}</select>` : ''}
        ${clips.length ? `<select data-act="SetAnim" style="${dd}">${animOpts}</select>` : ''}
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
