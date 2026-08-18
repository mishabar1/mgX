import * as THREE from 'three';
import {
  Container, Text, Image as UiImage, Input as UiInput,
  reversePainterSortStable, setPreferredColorScheme,
} from '@pmndrs/uikit';
// Real controls, from uikit's own widget set (the VANILLA @pmndrs build, not the React one).
// NOTE there is deliberately no Select/Dropdown in this package: a list that expands over other
// content is unusable with a VR laser pointer, so a RadioGroup showing every option is the
// idiomatic 3D substitute — and it is what `select` maps onto below.
import { Checkbox, RadioGroup, RadioGroupItem } from '@pmndrs/uikit-default';
// Named imports ONLY — the package exports ~1600 icons and pulling the barrel in wholesale would
// drag every one of them into the bundle.
import {
  Dice6, Music, Volume2, Eye, Ban, Trash2, X, RotateCcw, RotateCw, Square, Play,
} from '@pmndrs/uikit-lucide';
// NOTE on scrolling: uikit's overflow:'scroll' needs a pointer system feeding it wheel and drag
// events (@pmndrs/pointer-events, forwardHtmlEvents). That was tried and REVERTED: its canvas
// listeners compete with the app's own InteractionManager on the same element and left every panel
// control dead. Re-attempt it deliberately — prove it delivers a click BEFORE removing the
// InteractionManager path below, and expect to have to arbitrate which system owns the canvas.
import { GAMES_BASE } from './mg.three';

// =====================================================================================
// THE control panel renderer.  There is no HTML panel any more: a seat's `PlayerData.Screen`
// (a UiNode tree the SERVER builds) is drawn as real geometry in the scene, so the panel looks
// and behaves the same on desktop, on mobile and inside a VR session — a DOM overlay is simply
// invisible to a headset, which is why the HTML renderer had to go.
//
// The client stays DUMB. This file knows how to DRAW a UiNode and how to REPORT an activation.
// It holds no rules, no game texts, no button lists and no game names; every game gets a panel
// for free by building the same tree it always did. Nothing here is game-specific.
//
// Library boundary: @pmndrs/uikit lives ONLY in this file. It runs a real flexbox engine (yoga),
// so this code DESCRIBES the panel and never measures it — no line counting, no estimated text
// widths, no explicit heights. Sizes below are in PIXELS, exactly the units the server already
// sends in `UiNode.Size`, and the whole panel is scaled once at the end.
//
// PLACEMENT: default is 'hud' — the panel is parented to the CAMERA and sized from the camera's
// own frustum, so it sits in view at a sensible size in every game with zero per-game setup
// (the closest like-for-like replacement for the old overlay). A game can opt into 'world'
// placement via the panel3dAnchor / panel3dRot / panel3dWidth attributes when we get to
// per-game positioning.
// =====================================================================================

export type UiNode = {
  type?: string; text?: string; color?: string; bg?: string; size?: number; style?: string;
  url?: string; action?: string; args?: { [k: string]: string }; argKey?: string; need?: number;
  options?: { label: string, value: string, checked?: boolean, selected?: boolean }[];
  children?: UiNode[]; id?: string; placeholder?: string; onChange?: boolean; checked?: boolean;
  confirm?: string; gather?: string[]; overlays?: { url: string, x: number, y: number, w: number }[];
};

/**
 * Authored width in px per dock. A side dock is a narrow column; a top/bottom dock is a wide
 * strip, and giving it the same 520px would wrap its rows of tiles into a tall block that then
 * has to be scaled down to fit — tiny and unreadable. Width is the only lever here, since yoga
 * derives the height from the content.
 */
const PANEL_PX_SIDE = 520;
const PANEL_PX_EDGE = 1100;
const widthFor = (dock: Dock) => (dock === 'top' || dock === 'bottom') ? PANEL_PX_EDGE : PANEL_PX_SIDE;

// `style` arrives as the same free-form keyword string the CSS classes used. Unknown keywords
// fall through to a neutral look, so the server can invent a style without breaking the client.
const FILL: { [k: string]: string } = {
  ok: '#15803d', no: '#b91c1c', primary: '#b45309', team: '#1d4ed8',
  win: '#15803d', lose: '#b91c1c', cur: '#b45309', ghost: '#1f2937',
};
const NEUTRAL = '#334155';
const has = (style: string | undefined, k: string) => (style || '').split(/[\s,]+/).includes(k);

// The MSDF atlas uikit ships (Inter, via @pmndrs/msdfonts) contains exactly 104 glyphs: all 95
// printable ASCII plus Ö Ü Ä § ä ö ü ß ° — verified by reading the font's own char table, not
// guessed. Anything else draws as a hollow box, so text is mapped onto that set here. This is why
// chasing individual characters kept failing: there is no glyph for ANY arrow, dash, bullet,
// dingbat or emoji, so they all have to be translated or dropped.
const GLYPHS: Set<string> = new Set([
  ...Array.from({ length: 0x7F - 0x20 }, (_, i) => String.fromCharCode(0x20 + i)),
  ...'ÖÜÄ§äöüß°',
]);

// Sensible ASCII stand-ins for the punctuation the games actually write.
const TRANSLIT: [RegExp, string][] = [
  [/[·•]/g, '-'],                     // · •
  [/[‐-―−]/g, '-'],              // hyphens, dashes, MINUS SIGN
  [/→/g, '->'], [/←/g, '<-'],
  [/[‘’‛]/g, "'"], [/[“”‟]/g, '"'],
  [/×/g, 'x'], [/÷/g, '/'],
  [/≥/g, '>='], [/≤/g, '<='], [/≠/g, '!='],
  [/…/g, '...'], [/–/g, '-'],
  [/☑/g, '[x]'], [/☐/g, '[ ]'], [/[✔✓]/g, 'v'],
  [/[ - ]/g, ' '],                    // exotic spaces -> a normal one
];

/**
 * Characters that are DECORATIVE when they have no glyph — symbols, arrows, dingbats, emoji and
 * their modifiers. These are dropped silently. Everything else that is missing becomes '?', so a
 * name in an unsupported script shows as ???? rather than vanishing without trace.
 */
const DECORATIVE = /[ -⯿\u{1F000}-\u{1FAFF}︀-️‍]/u;

/**
 * EVERY string that reaches a Text node must come through here — a missing glyph is a silent
 * cosmetic bug, not a crash, so a dropped call is easy to miss (it happened once already).
 */
function txt(s: string | undefined): string {
  let t = s ?? '';
  for (const [re, to] of TRANSLIT) t = t.replace(re, to);
  let out = '';
  for (const ch of t) {
    if (ch === '\n' || GLYPHS.has(ch)) out += ch;
    else if (DECORATIVE.test(ch)) continue;      // icon-ish and unrenderable -> drop
    else out += '?';                             // real text we cannot draw -> stay visible
  }
  return out.replace(/[ \t]{2,}/g, ' ').trim();
}

/**
 * The games decorate labels with emoji, and the MSDF atlas has no glyph for any of them (see
 * GLYPHS) — so they used to render as hollow boxes, and then as nothing once txt() started
 * dropping them. Real vector icons put the meaning back: an emoji at the START of a label is
 * replaced by the matching lucide icon, drawn as geometry beside the text.
 *
 * A label whose emoji is not mapped simply loses it, exactly as before — never a box.
 */
const ICONS: { [ch: string]: any } = {
  '🎲': Dice6, '🎵': Music, '🔊': Volume2, '👁': Eye, '🚫': Ban, '🗑': Trash2,
  '✖': X, '✕': X, '⟲': RotateCcw, '⟳': RotateCw, '⏹': Square, '▶': Play,
};

/** Split a leading icon off a label: the icon class (if recognised) and the remaining text. */
function splitIcon(label: string): { Icon?: any, rest: string } {
  const trimmed = (label || '').trim();
  for (const ch of Object.keys(ICONS)) {
    if (trimmed.startsWith(ch)) return { Icon: ICONS[ch], rest: trimmed.slice(ch.length).trim() };
  }
  return { rest: label };
}

function fill(style: string | undefined): string {
  for (const k of Object.keys(FILL)) if (has(style, k)) return FILL[k];
  return NEUTRAL;
}

/** 'rrggbb' | '#rrggbb' -> a colour uikit accepts. The server sends bare hex, as CSS did. */
function col(hex: string | undefined, fallback: string): string {
  if (!hex) return fallback;
  const h = String(hex).replace('#', '').trim();
  return /^[0-9a-fA-F]{3}$|^[0-9a-fA-F]{6}$/.test(h) ? '#' + h : fallback;
}

/** Server picture paths are relative to the games content root, exactly as the DOM panel had it. */
const gamesUrl = (u: string) => (/^(https?:|data:|\/)/i.test(u) ? u : GAMES_BASE + u);

/**
 * A choice list is drawn either as a DROPDOWN (collapsed to one row until opened) or as a plain
 * LIST of radio buttons. A list costs one click and is the friendlier target for a VR pointer, so
 * it wins for short option sets; past this many options it eats the panel, and the dropdown wins.
 * The server can force either with a `dropdown` or `list` style keyword.
 */
const DROPDOWN_FROM = 6;

/** Which edge of the player's view a panel is pinned to. Order here is the layout order. */
export type Dock = 'right' | 'left' | 'top' | 'bottom';
const DOCKS: Dock[] = ['right', 'left', 'top', 'bottom'];

/** Distance in front of the eye that docked panels hang at, in world units. */
const HUD_DIST = 2;
/** How wide a panel is when carried in VR, in metres. */
const HAND_WIDTH = 0.42;

interface Pane {
  dock: Dock;
  key: string;              // content fingerprint; unchanged key = keep this panel as it is
  clickables: any[];        // registered with the InteractionManager, released with the pane
  group: THREE.Group;       // positions the panel; a child of MgPanel3d.group
  root: Container;          // the uikit tree
  wPx: number;              // authored width in px (depends on the dock)
  estH: number;             // estimated content height in px, for fitting and stacking
  px: number;               // current pixelSize
}

function dockOf(style: string | undefined): Dock {
  const s = (style || '').toLowerCase();
  for (const d of DOCKS) if (s.split(/[\s,]+/).includes(d)) return d;
  return 'right';                       // unknown or absent -> the default edge
}

/**
 * Split a seat's Screen into panels.
 *
 * A `panel` node becomes one panel, pinned to the edge named in its Style. Anything at the top
 * level that is NOT a panel node is collected into one implicit panel docked right — which is
 * exactly what every game that never mentions panels already gets, so this is backward
 * compatible by construction.
 */
function splitPanels(screen: UiNode[]): { dock: Dock, nodes: UiNode[] }[] {
  const out: { dock: Dock, nodes: UiNode[] }[] = [];
  let loose: UiNode[] | null = null;
  for (const nd of screen) {
    if ((nd.type || '').toLowerCase() === 'panel') {
      out.push({ dock: dockOf(nd.style), nodes: nd.children || [] });
    } else {
      if (!loose) { loose = []; out.push({ dock: 'right', nodes: loose }); }
      loose.push(nd);
    }
  }
  return out;
}

/** Only push a new pixelSize when it actually changed — it invalidates uikit's layout. */
function setPixelSize(p: Pane, v: number) {
  if (!isFinite(v) || v <= 0 || Math.abs(v - p.px) < 1e-9) return;
  p.px = v;
  p.root.setProperties({ pixelSize: v });
}

export class MgPanel3d {

  /** Everything this seat's panels live under. Parented to the camera, or to a hand in VR. */
  readonly group = new THREE.Group();

  private panes: Pane[] = [];
  /** The pane being built, so its interactive objects are recorded against it. */
  private building?: Pane;
  private seatId = '';

  /** Local widget state that must survive a rebuild. The SERVER still decides what it means. */
  private picks: { [argKey: string]: string[] } = {};      // "checks" groups, mid-selection
  private fields: { [id: string]: string } = {};           // "input" values, for `gather`
  private open: { [id: string]: boolean } = {};            // which dropdowns are expanded

  /** Non-null while the panels ride a VR controller. */
  private attachedTo: THREE.Object3D | null = null;

  constructor(
    private mgThree: any,
    /** Report an activation to the server. Identical payload to what the DOM panel sent. */
    private dispatch: (action: string, args: { [k: string]: string }) => void,
  ) {
    this.group.name = 'PANEL3D';
    // uikit-default is themed light/dark; the panel is dark, so ask for the dark palette once.
    setPreferredColorScheme('dark');
    const r = mgThree?.renderer;
    if (r) {
      // uikit needs local clipping for nested containers, and the reverse painter sort so a
      // panel's background / border / text layer correctly instead of z-fighting.
      r.localClippingEnabled = true;
      if (typeof r.setTransparentSort === 'function') r.setTransparentSort(reversePainterSortStable);
    }

  }

  // ------------------------------------------------------------------ lifecycle

  /** @returns true if anything is on screen (false = nothing to show for this seat). */
  update(screen: UiNode[] | null, seatId: string): boolean {
    this.seatId = seatId;
    this.reparent();

    if (!screen || !screen.length) { this.clear(); this.group.visible = false; return false; }
    this.group.visible = true;

    const specs = splitPanels(screen);

    // Rebuild PER PANEL, not per screen. A click usually changes one panel (the DM's die roll does
    // not touch his scene buttons), and tearing down a panel that did not change is both wasted
    // work and a visible flicker. The local widget state is part of the fingerprint so a
    // half-finished "checks" selection still survives.
    // NOTE `open` is deliberately NOT in here: a dropdown opens by toggling its list in place, so
    // including it would rebuild the panel for something purely visual — the old flicker.
    const local = JSON.stringify(this.picks) + '#' + JSON.stringify(this.fields);
    const keyed = specs.map(sp => ({
      ...sp,
      key: seatId + '#' + sp.dock + '#' + JSON.stringify(sp.nodes) + '#' + local + '#' + this.clipSig(sp.nodes),
    }));

    // If the panel LIST changed shape, positional matching is meaningless — start over.
    const sameShape = keyed.length === this.panes.length
      && keyed.every((k, i) => k.dock === this.panes[i].dock);
    if (!sameShape) this.clear();

    const kept: Pane[] = [];
    for (let i = 0; i < keyed.length; i++) {
      const spec = keyed[i];
      const existing = sameShape ? this.panes[i] : undefined;
      if (existing && existing.key === spec.key) { kept.push(existing); continue; }
      if (existing) this.clearPane(existing);
      kept.push(this.buildPane(spec));
    }
    this.panes = kept;

    // Size and position NOW rather than waiting for the next frame's tick.
    this.safePlace();
    return this.panes.length > 0;
  }

  /**
   * An `animpick` lists the animation clips of an already-loaded MODEL, so its content does not
   * come from the server tree at all. That has to be part of the panel's fingerprint, or a panel
   * whose clips arrive later would never be rebuilt and would sit on "model still loading".
   */
  private clipSig(nodes: UiNode[]): string {
    let sig = '';
    const walk = (ns: UiNode[]) => {
      for (const n of ns) {
        if ((n.type || '').toLowerCase() === 'animpick') {
          const it: any = this.items?.()?.[n.id || ''];
          sig += '|' + (n.id || '') + ':' + ((it?.mesh?.userData?.['clips'] || []).length);
        }
        if (n.children?.length) walk(n.children);
      }
    };
    walk(nodes);
    return sig;
  }

  private buildPane(spec: { dock: Dock, nodes: UiNode[], key: string }): Pane {
    const paneGroup = new THREE.Group();
    paneGroup.name = 'PANEL3D:' + spec.dock;
    this.group.add(paneGroup);

    const wPx = widthFor(spec.dock);
    // No height: yoga derives it from the content.
    const root = new Container({
      width: wPx,
      flexDirection: 'column',
      alignItems: 'stretch',
      padding: 18,
      gap: 10,
      backgroundColor: '#0b0f14',
      opacity: 0.92,
      borderRadius: 18,
      borderWidth: 2,
      borderColor: '#1f2937',
    });
    paneGroup.add(root);

    const pane: Pane = {
      dock: spec.dock, key: spec.key, clickables: [],
      group: paneGroup, root, wPx, estH: this.estimate(spec.nodes), px: 0,
    };
    this.building = pane;
    try { for (const nd of spec.nodes) this.build(nd, root); }
    finally { this.building = undefined; }
    return pane;
  }

  /** uikit computes layout and flushes transforms once per frame; mg.three drives this. */
  tick(deltaMs: number) {
    if (!this.panes.length) return;
    // place() FIRST: it sets each panel's pixelSize, and root.update() below is what consumes it.
    // Laying out first and correcting after costs one visibly wrong frame — which is exactly the
    // flash this used to produce on every click, because a fresh root starts at uikit's default
    // pixelSize of 0.01 (a 520px panel = 5.2 world units, two units from the eye: enormous).
    this.safePlace();
    for (const p of this.panes) p.root.update(deltaMs);
  }

  private safePlace() {
    try { this.place(); } catch (e) {
      if (!this.loggedPlaceError) { this.loggedPlaceError = true; console.error('[panel3d] placement failed', e); }
    }
  }
  private loggedPlaceError = false;

  /** Carry the panels on a VR controller (or, with null, dock them to the camera again). */
  attachTo(target: THREE.Object3D | null) {
    if (target === this.attachedTo) return;
    this.attachedTo = target;
    this.reparent();
  }

  private reparent() {
    const parent = this.attachedTo ?? this.mgThree.camera;
    if (parent && this.group.parent !== parent) parent.add(this.group);
    // A camera is not normally in the scene graph, and children of an unparented camera are never
    // rendered. Adding it is idempotent and harmless.
    if (!this.attachedTo) this.mgThree.scene?.add(this.mgThree.camera);
  }

  /**
   * Lay the panels out. Runs per frame: cheap, and the camera can move or resize.
   *
   * NOTE uikit converts px to world units ITSELF through the inherited `pixelSize` property
   * (default 0.01, so a 520px panel is 5.2 units wide). World size is therefore set by choosing
   * pixelSize, NOT by scaling the group — scaling only fights uikit's own conversion. And since
   * every panel's authored width is known (see widthFor), no measuring is needed.
   * (uikit's `size` signal is not in pixels — it reads 1x1 — and its meshes are transformed in
   * the shader, so neither that signal nor a Box3 can measure a panel. Both were dead ends.)
   */
  private place() {
    if (this.attachedTo) { this.placeOnHand(); return; }
    this.group.position.set(0, 0, 0);
    this.group.rotation.set(0, 0, 0);
    this.group.scale.setScalar(1);

    const cam = this.mgThree.camera as THREE.PerspectiveCamera;
    if (!cam?.isPerspectiveCamera) return;
    const viewH = 2 * HUD_DIST * Math.tan((cam.fov * Math.PI / 180) / 2);   // world height at that depth
    const viewW = viewH * cam.aspect;
    const margin = viewW * 0.015;
    const gap = viewH * 0.02;

    for (const dock of DOCKS) {
      const group = this.panes.filter(p => p.dock === dock);
      if (!group.length) continue;
      const n = group.length;
      const vertical = dock === 'right' || dock === 'left';

      // Share the edge between the panels pinned to it: along the edge each gets a slice, across
      // it they all get the same allowance. Fit BOTH, so a tall panel shrinks instead of running
      // off the screen.
      const allowW = vertical ? viewW * 0.30 : (viewW * 0.94) / n;
      const allowH = vertical ? (viewH * 0.94) / n : (viewH * 0.42) / n;
      for (const p of group) {
        setPixelSize(p, Math.min(allowW / p.wPx, allowH / Math.max(1, p.estH)));
      }

      const size = (p: Pane) => ({ w: p.wPx * p.px, h: p.estH * p.px });
      const total = group.reduce((a, p) => a + (vertical ? size(p).h : size(p).w), 0) + gap * (n - 1);

      // Stack along the edge, centred on it: top-to-bottom on a side edge, left-to-right on a
      // top/bottom edge — always in the order the game listed the panels.
      let cursor = vertical ? total / 2 : -total / 2;
      for (const p of group) {
        const { w, h } = size(p);
        let x: number, y: number;
        if (vertical) {
          y = cursor - h / 2;
          cursor -= h + gap;
          x = dock === 'right' ? viewW / 2 - w / 2 - margin : -viewW / 2 + w / 2 + margin;
        } else {
          x = cursor + w / 2;
          cursor += w + gap;
          y = dock === 'top' ? viewH / 2 - h / 2 - margin : -viewH / 2 + h / 2 + margin;
        }
        p.group.position.set(x, y, -HUD_DIST);
        p.group.rotation.set(0, 0, 0);
      }
    }
  }

  /**
   * In VR the panels ride the hand instead of the view, so the DOCK is meaningless there — a
   * headset has no screen edges. They simply stack one under another, in list order, tilted up
   * like a handful of cards.
   */
  private placeOnHand() {
    this.group.position.set(0.12, 0.04, -0.22);
    this.group.rotation.set(-Math.PI / 5, -0.35, 0);
    this.group.scale.setScalar(1);

    const gap = 0.02;
    let cursor = 0;
    for (const p of this.panes) {
      setPixelSize(p, HAND_WIDTH / p.wPx);
      const h = p.estH * p.px;
      p.group.position.set(0, -cursor - h / 2, 0);
      p.group.rotation.set(0, 0, 0);
      cursor += h + gap;
    }
  }

  /**
   * A rough content height in px, used ONLY for fitting and stacking. yoga still does the real
   * layout; this never positions anything inside a panel, so being a little off is harmless.
   */
  private estimate(nodes: UiNode[]): number {
    const one = (nd: UiNode): number => {
      const t = (nd.type || 'text').toLowerCase();
      switch (t) {
        case 'panel': case 'col': return (nd.children || []).reduce((a, k) => a + one(k), 0) + (nd.size ?? 8);
        case 'row': return Math.max(24, ...(nd.children || []).map(one)) + (nd.size ?? 8);
        case 'space': return nd.size ?? 8;
        case 'image': case 'model': return (nd.size ?? 84) + 8;
        case 'title': return 42;
        case 'banner': return 62;
        case 'note': return 24;
        case 'log': return String(nd.text || '').split('\n').length * 21 + 20;
        case 'button': case 'check': return 48;
        case 'select': case 'animpick': {
          const n = this.choiceCount(nd);
          if (!n) return 24;                                    // nothing to offer yet
          if (!this.asDropdown(nd.style, n)) return n * 34 + 8;  // a plain list of radios
          return 46;   // a dropdown is always one row tall: its open list floats above, absolutely
        }
        case 'checks': return ((nd.options || []).length + 1) * 46;
        case 'input': return 46;
        default: return (nd.size ?? 21) * 1.5;
      }
    };
    const raw = nodes.reduce((a, n) => a + one(n) + 10, 0) + 36;   // + root gaps and padding
    return raw * 1.3;                                             // headroom: wrapped text grows
  }

  dispose() {
    this.clear();
    if (this.frameHook) { this.mgThree?.removeFrameHook?.(this.frameHook); this.frameHook = undefined; }
    this.group.removeFromParent();
  }

  /** Registered by the host so dispose() can unregister it again. */
  frameHook?: (deltaMs: number) => void;

  private clear() {
    for (const p of this.panes) this.clearPane(p);
    this.panes = [];
  }

  /** Release ONE panel: unregister its interactive objects, drop its uikit tree and its group. */
  private clearPane(p: Pane) {
    for (const b of p.clickables) {
      try {
        b.removeEventListener('click', b.userData['__onClick']);
        this.mgThree.interactionManager?.remove(b);
      } catch { /* already gone */ }
    }
    p.clickables = [];
    try { (p.root as any).dispose?.(); } catch { /* nothing to release */ }
    p.group.removeFromParent();
  }

  // ------------------------------------------------------------------ node -> geometry

  private build(nd: UiNode, parent: Container) {
    const t = (nd.type || 'text').toLowerCase();

    switch (t) {
      case 'col':
      case 'row': {
        const isRow = t === 'row';
        const box = new Container({
          flexDirection: isRow ? 'row' : 'column',
          flexWrap: isRow ? 'wrap' : 'no-wrap',
          alignItems: 'center',
          justifyContent: isRow ? 'flex-start' : 'flex-start',
          gap: nd.size ?? 8,                       // `size` is the GAP on a container, as in CSS
          ...(nd.bg ? { backgroundColor: col(nd.bg, '#111827'), borderRadius: 10, padding: 8 } : {}),
        });
        parent.add(box);
        for (const k of nd.children || []) this.build(k, box);
        return;
      }

      case 'title': {
        const { Icon, rest } = splitIcon(nd.text || '');
        const colour = col(nd.color, '#ffd166');
        if (!Icon) {
          parent.add(new Text({ text: txt(rest), fontSize: 30, fontWeight: 'bold', color: colour }));
          return;
        }
        const row = new Container({ flexDirection: 'row', alignItems: 'center', gap: 10 });
        parent.add(row);
        row.add(new Icon({ width: 28, height: 28, color: colour, flexShrink: 0 }));
        row.add(new Text({ text: txt(rest), fontSize: 30, fontWeight: 'bold', color: colour }));
        return;
      }

      case 'note':
        parent.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? 17, color: col(nd.color, '#94a3b8') }));
        return;

      case 'banner': {
        const box = new Container({
          backgroundColor: fill(nd.style), borderRadius: 12, padding: 12, marginY: 4,
          alignItems: 'center', justifyContent: 'center',
        });
        parent.add(box);
        box.add(new Text({ text: txt(nd.text), fontSize: 26, fontWeight: 'bold', color: '#ffffff' }));
        return;
      }

      case 'log': {
        const box = new Container({
          backgroundColor: '#030712', opacity: 0.9, borderRadius: 8, padding: 10,
          flexDirection: 'column', alignItems: 'flex-start',
        });
        parent.add(box);
        box.add(new Text({ text: txt(nd.text), fontSize: 15, color: '#9ca3af' }));
        return;
      }

      case 'space':
        parent.add(new Container({ height: nd.size ?? 8, flexShrink: 0 }));
        return;

      case 'image': {
        const h = nd.size ?? 84;
        // Overlays are positioned in PERCENT of the base picture, so they stay glued to the same
        // spot at any panel size — the base is the positioning context, exactly as in the DOM.
        const holder = nd.overlays?.length
          ? new Container({ height: h, flexShrink: 0, positionType: 'relative' })
          : null;
        const img = new UiImage({ height: h, borderRadius: 8, flexShrink: 0, src: gamesUrl(nd.url || '') });
        if (holder) { parent.add(holder); holder.add(img); } else { parent.add(img); }
        if (holder) {
          for (const o of nd.overlays || []) {
            holder.add(new UiImage({
              src: gamesUrl(o.url),
              positionType: 'absolute',
              positionLeft: `${o.x}%` as any,
              positionTop: `${o.y}%` as any,
              width: `${o.w}%` as any,
              transformTranslateX: '-50%' as any,
              transformTranslateY: '-50%' as any,
            }));
          }
        }
        return;
      }

      case 'model': {
        const h = nd.size ?? 84;
        const box = new UiImage({ height: h, width: h, borderRadius: 8, flexShrink: 0 });
        parent.add(box);
        this.thumbnail(nd.url, box);
        return;
      }

      case 'button':
        this.addButton(parent, nd.text || '', nd.style, nd.url, () => this.fire(nd, nd.args || {}));
        return;

      case 'check': {
        // A REAL checkbox. The server still receives the same '0'/'1' it always did.
        const send = () => this.fire(nd, { ...(nd.args || {}), [nd.argKey || 'v']: nd.checked ? '0' : '1' });
        parent.add(this.labelled(new Checkbox({ checked: !!nd.checked, onCheckedChange: send }), nd.text, send));
        return;
      }

      case 'select': {
        const akey = nd.argKey || 'v';
        const id = this.choiceId(nd);
        // An option with an EMPTY value is a "choose..." placeholder, not a choice — the DOM panel
        // explicitly ignored it, so it is used as the collapsed caption here instead of an option.
        const all = nd.options || [];
        const opts = all.filter(o => o.value !== '');
        const placeholder = all.find(o => o.value === '')?.label || '';
        const chosen = this.fields[id] ?? all.find(o => o.selected)?.value;

        const pick = (v: string) => {
          this.fields[id] = v;                       // remembered either way, so `gather` can read it
          if (nd.onChange) this.fire(nd, { ...(nd.args || {}), [akey]: v });
          else this.rebuildSoon();                   // no dispatch: just show the new choice
        };

        if (this.asDropdown(nd.style, opts.length)) {
          this.addDropdown(parent, id, placeholder, opts, chosen, pick);
          return;
        }

        const grp = new RadioGroup({
          value: chosen,
          onValueChange: (v?: string) => { if (v != null) pick(v); },
        });
        (grp as any).setProperties({ flexDirection: 'column', gap: 6, alignItems: 'flex-start' });
        parent.add(grp);
        for (const o of opts) {
          grp.add(this.labelled(new RadioGroupItem({ value: o.value }), o.label, () => pick(o.value)));
        }
        return;
      }

      case 'checks': {
        // Multi-select: real checkboxes, with the tally kept LOCALLY until the player submits.
        const akey = nd.argKey || 'v';
        const cur = this.picks[akey] ?? (nd.options || []).filter(o => o.checked).map(o => o.value);
        this.picks[akey] = cur;
        const box = new Container({ flexDirection: 'column', gap: 6, alignItems: 'flex-start' });
        parent.add(box);
        for (const o of nd.options || []) {
          const toggle = () => {
            const i = cur.indexOf(o.value);
            if (i >= 0) cur.splice(i, 1); else cur.push(o.value);
            this.rebuildSoon();   // the fingerprint includes `picks`, so only THIS panel rebuilds
          };
          box.add(this.labelled(
            new Checkbox({ checked: cur.includes(o.value), onCheckedChange: toggle }), o.label, toggle));
        }
        const need = nd.need ?? 0;
        const ready = need <= 0 || cur.length === need;
        this.addButton(box, (nd.text || 'Submit') + (need > 0 ? ` (${cur.length}/${need})` : ''),
          ready ? 'ok' : undefined, undefined, () => {
            if (!ready) { return; }
            this.fire(nd, { ...(nd.args || {}), [akey]: cur.join(',') });
            delete this.picks[akey];
          });
        return;
      }

      case 'animpick': {
        // Model-inspection capability, not game logic: offer the loaded model's animation clips
        // and dispatch the server's action with the chosen index.
        const it: any = this.items?.()?.[nd.id || ''];
        const clips: any[] = it?.mesh?.userData?.['clips'] || [];
        if (!clips.length) {
          parent.add(new Text({ text: '(model still loading)', fontSize: 15, color: '#64748b' }));
          return;
        }
        const akey = nd.argKey || 'idx';
        const id = this.choiceId(nd);
        const cur = String(it?.animationIdx ?? -1);
        const send = (v: string) => this.fire(nd, { ...(nd.args || {}), [akey]: v });
        const opts = [{ label: 'none', value: '-1' }]
          .concat(clips.map((c: any, i: number) => ({ label: c?.name || ('Clip ' + i), value: String(i) })));

        if (this.asDropdown(nd.style, opts.length)) {
          this.addDropdown(parent, id, 'animation', opts, cur, send);
          return;
        }
        const grp = new RadioGroup({
          value: cur,
          onValueChange: (v?: string) => { if (v != null) send(v); },
        });
        (grp as any).setProperties({ flexDirection: 'column', gap: 6, alignItems: 'flex-start' });
        parent.add(grp);
        for (const o of opts) {
          grp.add(this.labelled(new RadioGroupItem({ value: o.value }), o.label, () => send(o.value)));
        }
        return;
      }

      case 'input': {
        // uikit ships a real text field with a caret, so this keeps working on desktop/mobile.
        // The value is remembered locally so a `gather` button can collect it, exactly as the
        // DOM panel read it out of the <input>.
        const id = nd.id || '';
        const box = new Container({
          backgroundColor: '#111827', borderRadius: 8, borderWidth: 1, borderColor: '#334155',
          paddingX: 10, paddingY: 8,
        });
        parent.add(box);
        box.add(new UiInput({
          value: this.fields[id] ?? '',
          placeholder: nd.placeholder || '',
          fontSize: 19,
          color: '#e5e7eb',
          onValueChange: (v: string) => { this.fields[id] = v; },
        }));
        return;
      }

      case 'text':
      default: {
        // A chip/pill, or plain text. Unknown node types land here and show their text rather
        // than breaking, so the server can add a node type without breaking an old client.
        if (has(nd.style, 'pill') || has(nd.style, 'chip') || nd.bg) {
          const chip = new Container({
            backgroundColor: col(nd.bg, fill(nd.style)), borderRadius: 999,
            paddingX: 12, paddingY: 5, flexShrink: 0,
          });
          parent.add(chip);
          chip.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? 19, color: col(nd.color, '#ffffff') }));
          return;
        }
        parent.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? 21, color: col(nd.color, '#e5e7eb') }));
        return;
      }
    }
  }

  // ------------------------------------------------------------------ helpers

  /** A model shown as a picture — a generic client capability the server just asks for. */
  private thumbnail(url: string | undefined, box: any) {
    if (!url) return;
    this.mgThree.renderModelThumbnail?.(url)
      .then((dataUrl: string) => { if (dataUrl) { try { box.setProperties({ src: dataUrl }); } catch { } } })
      .catch(() => { });
  }

  /**
   * A DROPDOWN: one row showing the current choice, which expands into the option list when
   * clicked. Built from plain containers because uikit ships no Select/Dropdown of its own.
   *
   * The list expands INLINE (pushing what follows downward) rather than floating over the panel.
   * An absolutely-positioned overlay would look more like the web, but it would have to escape
   * the panel's own clipping and win a z-fight against it — and the panel is auto-height, so
   * growing it costs nothing and behaves identically under a VR pointer.
   *
   * `open` lives in local state (never on the server) and is part of the rebuild fingerprint, so
   * expanding rebuilds only this panel.
   */
  private addDropdown(parent: Container, id: string, placeholder: string,
                      options: { label: string, value: string }[],
                      current: string | undefined, choose: (v: string) => void) {
    // positionType 'relative' makes this box the anchor the open list positions itself against.
    const box = new Container({
      flexDirection: 'column', alignItems: 'stretch', flexShrink: 0, positionType: 'relative',
    });
    parent.add(box);

    const chosen = options.find(o => o.value === current);
    let isOpen = !!this.open[id];

    // The handler is wired through an indirection because uikit binds an `onX` property as a
    // listener when the properties are APPLIED — i.e. at construction. Assigning onClick later via
    // setProperties silently registers nothing, which is exactly how opening got broken once.
    // `toggle` cannot be defined yet (it needs the list built below), so the constructor gets a
    // stable wrapper and the real implementation is slotted in afterwards.
    let toggle = () => { };
    const head = new Container({
      flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
      backgroundColor: '#111827', borderWidth: 1, borderColor: isOpen ? '#3b82f6' : '#334155',
      borderRadius: 8, paddingX: 12, paddingY: 9,
    });
    box.add(head);
    const caption = new Text({
      text: txt(chosen ? chosen.label : (placeholder || 'Choose...')),
      fontSize: 19, color: chosen ? '#e5e7eb' : '#94a3b8',
    });
    head.add(caption);
    // 'v' / '^' rather than a caret glyph: the atlas has no arrows at all (see GLYPHS).
    const caret = new Text({ text: isOpen ? '^' : 'v', fontSize: 17, color: '#94a3b8' });
    head.add(caret);

    // The open list:
    //  * ABSOLUTE, so it floats over what follows instead of pushing it down — and takes no part
    //    in the panel's layout, which is why the panel neither grows nor rescales when it opens.
    //  * renderOrder + depthTest:false are what actually make it an OVERLAY. Everything in a panel
    //    is coplanar and transparent, so with depth testing on, the siblings drawn after it simply
    //    painted over the top and the options appeared interleaved with the controls behind them.
    //    Both properties are inherited by children, so the option text comes along.
    //  * pointerEventsOrder keeps it winning the hit-test against those same siblings.
    const list = new Container({
      positionType: 'absolute', positionTop: '100%', positionLeft: 0, width: '100%',
      display: isOpen ? 'flex' : 'none',
      // pointerEvents must follow the open state. pointerEventsOrder below makes this list beat
      // its siblings in the hit-test, and 'display: none' does NOT take an element out of
      // hit-testing — so a CLOSED list left at 'auto' silently swallowed every click in the whole
      // panel, buttons included.
      pointerEvents: isOpen ? 'auto' : 'none',
      zIndexOffset: 100, renderOrder: 1000, depthTest: false, pointerEventsOrder: 100,
      flexDirection: 'column', gap: 2, alignItems: 'stretch',
      backgroundColor: '#0b1220', borderRadius: 8, padding: 4,
      borderWidth: 1, borderColor: '#3b4a63',
      // opacity is INHERITED, and the panel root sets 0.92 — without overriding it here the
      // options list stays see-through and the controls behind it read straight through the menu.
      opacity: 1,
    });
    box.add(list);

    // Open/closed is pure LOCAL view state, so it is driven in place. Rebuilding the panel for it
    // was what made the whole panel flicker on every open.
    const setOpen = (v: boolean) => {
      isOpen = v;
      this.open[id] = v;                // remembered, so a later server-driven rebuild agrees
      list.setProperties({ display: v ? 'flex' : 'none', pointerEvents: v ? 'auto' : 'none' });
      head.setProperties({ borderColor: v ? '#3b82f6' : '#334155' });
      caret.setProperties({ text: v ? '^' : 'v' });
    };
    toggle = () => setOpen(!isOpen);
    this.makeInteractive(head, () => toggle());

    for (const o of options) {
      const selected = o.value === current;
      // Close and show the new choice IMMEDIATELY rather than waiting for a rebuild to do it.
      // Some choices never cause one: an animpick's clip index is client-side item state and is
      // not part of the panel's fingerprint, so relying on a rebuild left the menu hanging open.
      const activate = () => {
        setOpen(false);
        caption.setProperties({ text: txt(o.label), color: '#e5e7eb' });
        choose(o.value);
      };
      const row = new Container({
        paddingX: 12, paddingY: 8, borderRadius: 6,
        backgroundColor: selected ? '#1d4ed8' : '#0b0f14',
      });
      list.add(row);
      row.add(new Text({ text: txt(o.label), fontSize: 19, color: '#e5e7eb' }));
      this.makeInteractive(row, activate);
    }
  }

  /** Stable local-state key for a choice node (open/closed, remembered value). */
  private choiceId(nd: UiNode): string {
    return (nd.type || '').toLowerCase() === 'animpick'
      ? 'anim:' + (nd.id || '')
      : (nd.id || ('sel' + (nd.action || '')));
  }

  /** The options a choice node will actually offer, so height and content agree. */
  private choiceCount(nd: UiNode): number {
    if ((nd.type || '').toLowerCase() === 'animpick') {
      const it: any = this.items?.()?.[nd.id || ''];
      return ((it?.mesh?.userData?.['clips'] || []).length) + 1;      // + "none"
    }
    return (nd.options || []).filter(o => o.value !== '').length;
  }

  /** Dropdown or plain list? Server style wins; otherwise it is down to how many options there are. */
  private asDropdown(style: string | undefined, count: number): boolean {
    if (has(style, 'dropdown')) return true;
    if (has(style, 'list')) return false;
    return count >= DROPDOWN_FROM;
  }

  /**
   * A control plus its caption, side by side.
   *
   * The widget handles its own state off a 'click' event, so all this has to do is make it
   * REACHABLE by both input paths — see makeInteractive. `activate` is what the VR path calls,
   * since a controller ray cannot rely on the widget's own click handling.
   */
  private labelled(control: any, caption: string | undefined, activate?: () => void): Container {
    const row = new Container({ flexDirection: 'row', alignItems: 'center', gap: 10, flexShrink: 0 });
    row.add(control);
    if (caption) row.add(new Text({ text: txt(caption), fontSize: 19, color: '#e5e7eb' }));
    this.makeInteractive(control, activate, true);
    return row;
  }

  /**
   * Make one object clickable by BOTH input paths, with no change to either:
   *  * desktop — the InteractionManager raycasts registered objects and dispatches DOM-ish events
   *    ('click', 'mouseover', 'mouseout'); uikit Components override raycast(), so they intersect
   *    correctly and their own onClick/onCheckedChange fires from that dispatched 'click';
   *  * VR — mg.three's controller raycast walks up parents looking for exactly this userData shape
   *    (ItemData.clickActions with at least one key), so it finds the control for free. `uiArgs`
   *    marks it as a UI activation rather than a clicked board item.
   */
  private addButton(parent: Container, rawLabel: string, style: string | undefined,
                    iconUrl: string | undefined, onClick: () => void) {
    const big = has(style, 'big');
    const base = fill(style);
    const { Icon, rest: label } = splitIcon(rawLabel);

    const btn = new Container({
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'center',
      gap: 8,
      backgroundColor: base,
      borderRadius: 10,
      paddingX: big ? 20 : 14,
      paddingY: big ? 12 : 9,
      flexShrink: 0,
    });
    parent.add(btn);

    if (Icon) {
      const sz = big ? 26 : 20;
      btn.add(new Icon({ width: sz, height: sz, color: '#ffffff', flexShrink: 0 }));
    }
    if (iconUrl) {
      const isModel = /\.(gltf|glb|obj|stl)$/i.test(iconUrl);
      const icon = new UiImage({ height: big ? 30 : 24, width: big ? 30 : 24, flexShrink: 0 });
      btn.add(icon);
      if (isModel) this.thumbnail(iconUrl, icon);
      else icon.setProperties({ src: gamesUrl(iconUrl) });
    }
    if (label) btn.add(new Text({ text: txt(label), fontSize: big ? 26 : 21, fontWeight: 'bold', color: '#ffffff' }));

    // Hover and cursor are uikit's own now that it receives pointerover / pointerout.
    this.makeInteractive(btn, onClick);
  }

  /**
   * @param selfHandled true for a uikit-default WIDGET (Checkbox, RadioGroupItem, ...), which binds
   *   its own click handler at construction. Those only need to be REGISTERED so the dispatched
   *   'click' reaches them — adding our listener as well would run the action twice.
   */
  private makeInteractive(obj: any, activate?: () => void, selfHandled = false) {
    // VR: mg.three's controller raycast walks up parents looking for exactly this userData shape
    // (ItemData.clickActions with at least one key). `uiArgs` marks it as a UI activation rather
    // than a clicked board item.
    obj.userData['ItemData'] = { id: '', clickActions: { [this.seatId]: '__panel3d' }, attributes: {} };
    if (activate) obj.userData['uiArgs'] = { fire: activate };

    // DESKTOP: the app's InteractionManager. @pmndrs/pointer-events is also wired up (it is what
    // will make wheel/drag scrolling possible), but it is NOT yet proven to deliver clicks here —
    // and removing this registration on the assumption that it would left the entire panel dead.
    // Both systems dispatch 'click' on the object, so if pointer-events is later confirmed to
    // work, THIS is the line to drop — not the other way round.
    if (activate && !selfHandled) {
      const fire = () => activate();
      obj.userData['__onClick'] = fire;
      obj.addEventListener('click', fire);
    }
    this.mgThree.interactionManager?.add(obj);
    (this.building?.clickables ?? []).push(obj);
  }

  private fire(nd: UiNode, args: { [k: string]: string }) {
    if (nd.confirm && !window.confirm(nd.confirm)) return;
    // `gather`: fold the named input fields into the args, keyed by id — same contract as the
    // DOM panel, which read them straight out of the <input> elements.
    const all = { ...args };
    for (const id of nd.gather || []) all[id] = this.fields[id] ?? '';
    this.dispatch(nd.action || '', all);
  }

  // A purely local change (a "checks" toggle) has no server round-trip, so re-run the build.
  private pendingRebuild = false;
  private rebuildSoon() {
    if (this.pendingRebuild) return;
    this.pendingRebuild = true;
    setTimeout(() => { this.pendingRebuild = false; this.onNeedRebuild?.(); }, 0);
  }

  /** Set by the host: "re-run update() with the tree we already hold". */
  onNeedRebuild?: () => void;

  /** Set by the host: the game's loaded items, so an `animpick` can read a model's clips. */
  items?: () => { [id: string]: any };
}
