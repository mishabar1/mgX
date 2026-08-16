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
import {MgThree, GAMES_BASE} from '../../bl/mg.three';
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
  // tomorrow and only renderNode() is rewritten; the games never change.
  private panelEl?: HTMLElement;
  private panelWrap?: HTMLElement;
  private panelSeatId = '';
  private lastPanelKey = '';

  private setupServerPanel() {
    // One-time: build the panel shell + styles + a single delegated click handler.
    const el = document.createElement('div');
    el.style.pointerEvents = 'auto';
    el.innerHTML = `<style>${this.panelStyles()}</style>`
      + `<div class="sp"><div class="sp-topbar"><button class="sp-btn ghost" data-leave="1">← Games</button></div><div id="spBody"></div></div>`;

    // Clicks on buttons (SELECT/checkbox fire 'change' below instead).
    el.addEventListener('click', (ev: any) => {
      if (ev.target.closest('[data-leave]')) { this.zone.run(() => this.router.navigate([RouteNames.GamesList])); return; }
      const t = ev.target.closest('button[data-act]');
      if (!t || !this.panelSeatId) return;
      const confirm = t.getAttribute('data-confirm');
      if (confirm && !window.confirm(confirm)) return;
      const act = t.getAttribute('data-act');
      let args: any = {};
      try { args = JSON.parse(t.getAttribute('data-args') || '{}'); } catch {}
      // "checks" submit: gather the checked values in this group into the named arg key.
      const argKey = t.getAttribute('data-argkey');
      if (argKey) {
        const group = t.closest('.sp-checks');
        const vals = group ? Array.from(group.querySelectorAll('input[type=checkbox]:checked')).map((c: any) => c.getAttribute('data-val')) : [];
        const need = Number(t.getAttribute('data-need') || 0);
        if (need > 0 && vals.length !== need) { window.alert(`Pick exactly ${need}.`); return; }
        args[argKey] = vals.join(',');
      }
      // "gather": read named input/select fields into args (keyed by their id).
      const gather = t.getAttribute('data-gather');
      if (gather) gather.split(',').filter(Boolean).forEach((id: string) => {
        const f = this.panelEl!.querySelector(`[data-id="${id}"]`) as any;
        if (f) args[id] = f.value;
      });
      this.signalRService.executeActionArgs(this.gameId!, this.panelSeatId, act, args);
    });

    // Change on an on-change select / checkbox → dispatch immediately with its value.
    el.addEventListener('change', (ev: any) => {
      const t = ev.target.closest('[data-act][data-onchange]');
      if (!t || !this.panelSeatId) return;
      if (t.tagName === 'SELECT' && !t.value) return;   // ignore the empty placeholder
      const act = t.getAttribute('data-act');
      let args: any = {};
      try { args = JSON.parse(t.getAttribute('data-args') || '{}'); } catch {}
      const argKey = t.getAttribute('data-argkey');
      if (argKey) args[argKey] = t.type === 'checkbox' ? (t.checked ? '1' : '0') : t.value;
      this.signalRService.executeActionArgs(this.gameId!, this.panelSeatId, act, args);
    });

    // A wrapper we re-style per update (full-screen vs docked) based on the server's panelMode.
    this.panelWrap = document.createElement('div');
    this.panelWrap.appendChild(el);
    this.rendererContainer.nativeElement.appendChild(this.panelWrap);
    this.panelEl = el;
    this.updateServerPanel(this.mgGame?.gameData);
  }

  private updateServerPanel(g: any) {
    if (!this.panelEl || !this.panelWrap || !g) return;
    const me = this.generalService.User?.id;
    const mine = (g.players || []).find((p: any) => p.user?.id === me && p.screen);
    this.panelSeatId = mine?.id || '';
    const screen = mine?.screen || null;
    const mode = g.attributes?.['panelMode'] || 'side';

    const wrap = this.panelWrap;
    if (!screen) { wrap.style.display = 'none'; this.lastPanelKey = ''; return; }   // no panel for this game/seat
    wrap.style.cssText = mode === 'full'
      ? 'position:absolute;inset:0;z-index:30;overflow-y:auto;pointer-events:auto;display:flex;justify-content:center;align-items:flex-start;box-sizing:border-box;padding:10px;background:radial-gradient(circle at 50% -10%,#2a2018,#0b0806);'
      : 'position:absolute;top:0;right:0;height:100%;z-index:30;overflow-y:auto;pointer-events:auto;display:flex;align-items:flex-start;padding:14px;';

    // Rebuild only when the screen actually changed (so in-progress checkbox picks survive).
    const key = mode + '#' + JSON.stringify(screen);
    if (key === this.lastPanelKey) return;
    this.lastPanelKey = key;
    const body = this.panelEl.querySelector('#spBody') as HTMLElement;
    body.innerHTML = (screen as any[]).map(n => this.renderNode(n)).join('');

    // "model" nodes: the client renders the 3D model into a thumbnail image (a generic client
    // capability — the SERVER only says "show this model as a picture").
    setTimeout(async () => {
      for (const img of Array.from(body.querySelectorAll('img[data-model]')) as HTMLImageElement[]) {
        const u = img.getAttribute('data-model'); if (!u) continue;
        const data = await this.mgThree.renderModelThumbnail(u);
        if (data) img.src = data;
      }
    }, 0);

    this.fillPendingAnimpicks(body);
  }

  // An animpick <select> can render BEFORE its item's model (and animation clips) finished
  // loading — it would then show only "none" forever, because the panel only rebuilds when the
  // server screen changes. Poll briefly and refill any empty pickers once the clips arrive.
  private fillPendingAnimpicks(body: HTMLElement, tries = 0) {
    const sels = Array.from(body.querySelectorAll('select[data-animpick]')) as HTMLSelectElement[];
    const pending = sels.filter(s => s.options.length <= 1);
    if (!pending.length || tries > 50) return;
    for (const s of pending) {
      const id = s.getAttribute('data-animpick') || '';
      const it: any = (this.mgGame as any)?.allItems?.[id];
      const clips = it?.mesh?.userData?.['clips'] || [];
      if (!clips.length) continue;
      const cur = it?.animationIdx ?? -1;
      const clean = (v: any) => String(v ?? '').replace(/[&<>]/g, '');
      s.innerHTML = ['<option value="-1">🎬 none</option>'].concat(
        clips.map((c: any, i: number) => `<option value="${i}" ${i === cur ? 'selected' : ''}>${clean(c.name || ('Clip ' + i))}</option>`)).join('');
    }
    if (pending.some(s => s.options.length <= 1)) setTimeout(() => this.fillPendingAnimpicks(body, tries + 1), 400);
  }

  // Render one server UiNode (+ children) to HTML. Unknown types fall back to text, so a new
  // game can never crash the client.
  private renderNode(nd: any): string {
    const esc = (s: any) => String(s ?? '').replace(/[&<>]/g, (c: string) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' } as any)[c]);
    const bg = nd.bg ? `background:#${nd.bg};` : '';
    const style = (nd.color ? `color:#${nd.color};` : '') + (nd.size ? `font-size:${nd.size}px;` : '') + bg;
    const kids = () => (nd.children || []).map((k: any) => this.renderNode(k)).join('');
    switch (nd.type) {
      case 'col':   return `<div class="sp-col ${nd.style || ''}" style="${nd.size ? `gap:${nd.size}px;` : ''}${bg}">${kids()}</div>`;
      case 'row':   return `<div class="sp-row ${nd.style || ''}" style="${nd.size ? `gap:${nd.size}px;` : ''}${bg}">${kids()}</div>`;
      case 'title': return `<h3>${esc(nd.text)}</h3>`;
      case 'note':  return `<div class="sp-note">${esc(nd.text)}</div>`;
      case 'banner':return `<div class="sp-banner ${nd.style || ''}">${esc(nd.text)}</div>`;
      case 'log':   return `<div class="sp-log">${esc(nd.text)}</div>`;
      case 'space': return `<div style="height:${nd.size || 8}px"></div>`;
      case 'image': {
        const img = `<img class="sp-img ${nd.style || ''}" src="${GAMES_BASE}${nd.url}"${nd.size ? ` style="height:${nd.size}px"` : ''}>`;
        // Generic "item over item": the server can layer marker images on top of a base image
        // (nd.overlays: url + x/y centre + width, all in % of the base) — e.g. Resistance stamps
        // the current mission + past results onto the map. Percent units keep markers glued to
        // the same map spot at any panel size.
        if (!nd.overlays?.length) return img;
        const marks = nd.overlays.map((o: any) =>
          `<img src="${GAMES_BASE}${o.url}" style="position:absolute;left:${o.x}%;top:${o.y}%;width:${o.w}%;transform:translate(-50%,-50%);pointer-events:none;">`).join('');
        return `<span style="position:relative;display:inline-block;line-height:0;">${img}${marks}</span>`;
      }
      case 'model': return `<img class="sp-img sp-model ${nd.style || ''}" data-model="${nd.url}"${nd.size ? ` style="height:${nd.size}px"` : ''}>`;
      case 'button': {
        const isModel = nd.url && /\.(gltf|glb|obj|stl)$/i.test(nd.url);  // model icon → client thumbnail
        const icon = nd.url ? (isModel ? `<img data-model="${nd.url}">` : `<img src="${GAMES_BASE}${nd.url}">`) : '';
        const extra = (nd.confirm ? ` data-confirm="${esc(nd.confirm)}"` : '')
                    + (nd.gather ? ` data-gather="${esc((nd.gather || []).join(','))}"` : '');
        return `<button class="sp-btn ${nd.style || ''}" style="${bg}" data-act="${esc(nd.action)}" data-args='${esc(JSON.stringify(nd.args || {}))}'${extra}>${icon}<span>${esc(nd.text)}</span></button>`;
      }
      case 'animpick': {
        // Model-inspection capability (not game logic): fill a dropdown from the loaded model's
        // animation clips and dispatch the server action (SetAnim) with the chosen index.
        const it = (this.mgGame as any)?.allItems?.[nd.id];
        const clips = it?.mesh?.userData?.['clips'] || [];
        const cur = it?.animationIdx ?? -1;
        const opts = ['<option value="-1">🎬 none</option>'].concat(
          clips.map((c: any, i: number) => `<option value="${i}" ${i === cur ? 'selected' : ''}>${esc(c.name || ('Clip ' + i))}</option>`)).join('');
        // data-animpick lets fillPendingAnimpicks refill this select once the model's clips load
        return `<select class="sp-input" data-animpick="${esc(nd.id)}" data-act="${esc(nd.action)}" data-onchange="1" data-argkey="${esc(nd.argKey || 'idx')}">${opts}</select>`;
      }
      case 'input':
        return `<input class="sp-input" data-id="${esc(nd.id)}" placeholder="${esc(nd.placeholder || '')}">`;
      case 'select': {
        const opts = (nd.options || []).map((o: any) =>
          `<option value="${esc(o.value)}" ${o.selected ? 'selected' : ''}>${esc(o.label)}</option>`).join('');
        const act = nd.action ? ` data-act="${esc(nd.action)}"` : '';
        const oc = nd.onChange ? ' data-onchange="1"' : '';
        const ak = nd.argKey ? ` data-argkey="${esc(nd.argKey)}"` : '';
        const ar = nd.args ? ` data-args='${esc(JSON.stringify(nd.args))}'` : '';
        return `<select class="sp-input" data-id="${esc(nd.id)}"${act}${oc}${ak}${ar}>${opts}</select>`;
      }
      case 'check':
        return `<label class="sp-check"><input type="checkbox" data-act="${esc(nd.action)}" data-onchange="1" data-argkey="${esc(nd.argKey)}" data-args='${esc(JSON.stringify(nd.args || {}))}' ${nd.checked ? 'checked' : ''}> ${esc(nd.text)}</label>`;
      case 'checks': {
        const opts = (nd.options || []).map((o: any) =>
          `<label class="sp-check"><input type="checkbox" data-val="${esc(o.value)}" ${o.checked ? 'checked' : ''}> ${esc(o.label)}</label>`).join('');
        return `<div class="sp-checks">${opts}<button class="sp-btn ok" data-act="${esc(nd.action)}" data-argkey="${esc(nd.argKey)}" data-need="${nd.need || 0}" data-args='${esc(JSON.stringify(nd.args || {}))}'>${esc(nd.text || 'Submit')}</button></div>`;
      }
      case 'text':
      default:      return `<div class="sp-text ${nd.style || ''}" style="${style}">${esc(nd.text)}</div>`;
    }
  }

  private panelStyles(): string {
    return `
      .sp{width:min(560px,100%);margin:0 auto 24px;box-sizing:border-box;font:600 16px system-ui,sans-serif;color:#e8edf5;
           background:linear-gradient(180deg,rgba(22,17,14,.98),rgba(12,9,7,.98));border:1px solid #5a4632;
           border-radius:18px;padding:16px 18px;box-shadow:0 12px 48px rgba(0,0,0,.6);}
      .sp h3{margin:0 0 6px;font-size:22px;letter-spacing:.04em;}
      .sp-topbar{display:flex;margin-bottom:10px;}
      .sp-col{display:flex;flex-direction:column;}
      .sp-row{display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin:8px 0;}
      .sp-text{margin:4px 0;} .sp-text.big{font-size:19px;font-weight:800;}
      .sp-note{color:#b7a488;font-size:14px;font-style:italic;margin:6px 0;}
      .sp-img{border-radius:10px;border:1px solid #5a4632;display:block;margin:6px 0;} .sp-img.full{width:100%;}
      .sp-log{margin-top:12px;border-top:1px solid #5a4632;padding-top:8px;max-height:180px;overflow-y:auto;
           font:500 12px ui-monospace,monospace;color:#b7a488;white-space:pre-wrap;}
      .sp-banner{font-size:20px;font-weight:800;text-align:center;padding:14px;border-radius:12px;margin:8px 0;}
      .sp-banner.res,.sp-banner.win{background:#123a20;color:#8fffb0;}
      .sp-banner.spy,.sp-banner.lose{background:#3a1414;color:#ff9b9b;}
      .sp-text.pill{flex:1;text-align:center;padding:8px 0;border-radius:8px;background:#241a12;border:1px solid #5a4632;font-size:14px;margin:0;}
      .sp-text.chip{padding:2px 10px;border-radius:999px;font-size:12.5px;margin:0;border:1px solid rgba(255,255,255,.14);white-space:nowrap;}
      .sp-col.hist{margin-top:12px;border-top:1px solid #5a4632;padding-top:8px;max-height:260px;overflow-y:auto;}
      .sp-col.hist .sp-row{margin:2px 0;}
      .sp-text.pill.cur{outline:2px solid #d9b98a;}
      .sp-text.pill.s{background:#123a20;border-color:#2f7a45;} .sp-text.pill.f{background:#3a1414;border-color:#7a2f2f;}
      .sp-text.teamrow{font-size:18px;font-weight:700;color:#ffe0a8;padding:8px 12px;background:#241a12;border:1px solid #5a4632;border-radius:9px;margin-bottom:6px;}
      .sp-btn{font:800 16px system-ui;color:#fff;background:#6a4a25;border:0;border-radius:10px;padding:10px 16px;margin:4px 8px 4px 0;cursor:pointer;display:inline-flex;align-items:center;gap:8px;}
      .sp-btn:hover{background:#8a6431;}
      .sp-btn.ghost{background:#3a2c1e;padding:7px 14px;font-size:14px;margin:0;}
      .sp-btn.big{width:100%;justify-content:center;font-size:17px;padding:14px;box-sizing:border-box;}
      .sp-btn.ok{background:#2f7a45;} .sp-btn.ok:hover{background:#3a9455;}
      .sp-btn.no{background:#7a2f2f;} .sp-btn.no:hover{background:#984040;}
      .sp-btn.votebtn{flex-direction:column;gap:6px;background:#241a12;border:2px solid #5a4632;padding:10px 14px;}
      .sp-btn.votebtn img{height:64px;border-radius:6px;}
      .sp-checks{display:flex;flex-direction:column;gap:2px;margin:6px 0;}
      .sp-check{display:flex;align-items:center;gap:8px;font-size:16px;padding:5px 0;cursor:pointer;}
      .sp-check input{width:18px;height:18px;}
      .sp-input{box-sizing:border-box;padding:8px 10px;border-radius:9px;border:1px solid #5a4632;background:#0e0b08;color:#e8edf5;font:600 15px system-ui;margin:4px 0;min-width:140px;}
      .sp-model{width:74px;height:74px;object-fit:contain;background:#0a0705;border-radius:8px;}
      .sp-btn.tile{flex-direction:column;gap:4px;width:86px;background:#241a12;border:1px solid #5a4632;font-size:12px;padding:6px;}
      .sp-btn.tile img{width:68px;height:68px;object-fit:contain;border-radius:6px;background:#0a0705;}
      .sp-chip{display:inline-flex;align-items:center;gap:6px;background:#241a12;border:1px solid #5a4632;border-radius:9px;padding:5px 9px;margin:0 6px 6px 0;}
      .sp-col.tile{border:1px solid #2a3a55;border-radius:10px;padding:8px;min-width:104px;gap:5px;}
    `;
  }


}
