import * as THREE from 'three';
import {
  Container, Text, Image as UiImage,
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
  Dice6, Music, Volume2, Volume, Eye, Ban, Trash2, X, RotateCcw, RotateCw, Square, Play, Pause,
  Plus, Minus, Check, CheckCheck, ArrowRight, ArrowLeft, ChevronRight, ChevronLeft,
  ChevronUp, ChevronDown, SkipForward, SkipBack, Flag, Moon, Sun, Scale, FlaskConical,
  MessageSquare, Swords, Shield, Coins, Gem, Crown, Users, User, Hammer, Map, MapPin,
  Clock, Timer, Zap, Star, Heart, Flame, Anchor, Ship, Sparkles, Hand, Search, Settings,
  Info, CircleHelp, TriangleAlert, Lock, Trophy, Target, Layers, CirclePlus, CircleMinus,
  CircleCheck, CircleX, Send, Repeat, Shuffle, Undo2, Redo2, Wheat, Trees, Mountain,
  Pickaxe, Book, Scroll, Wand,
} from '@pmndrs/uikit-lucide';
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
// PLACEMENT is entirely the CLIENT's business. On screen the panels are parented to the CAMERA
// and sized from its frustum, then docked to the edges of the view; in VR they ride the player's
// hand, where screen edges mean nothing. The SERVER only names an edge, per panel, via
// UiNode.Panel(dock, ...) — 'right' (the default) | 'left' | 'top' | 'bottom'.
//
// There is deliberately no world-anchored placement: nothing has asked for a panel pinned to the
// table rather than the view, and an unused second placement mode is a second thing to keep
// working. Add it here (with the per-game attributes to drive it) the day a game needs it.
// =====================================================================================

export type UiNode = {
  type?: string; text?: string; color?: string; bg?: string; size?: number; style?: string;
  icon?: string;                       // named icon, resolved through ICON_BY_NAME
  url?: string; action?: string; args?: { [k: string]: string }; argKey?: string; need?: number;
  options?: { label: string, value: string, checked?: boolean, selected?: boolean }[];
  children?: UiNode[]; id?: string; onChange?: boolean; checked?: boolean;
  confirm?: string; gather?: string[]; overlays?: { url: string, x: number, y: number, w: number }[];
  // panel placement (type === 'panel'); see UiNode.cs
  anchor?: string; visibility?: string;
  item?: any;                          // type 'item3d': a real 3D item (an ItemData) in a slot
  at?: { x: number, y: number, z: number }; rot?: { x: number, y: number, z: number };
  worldWidth?: number;
};

/**
 * DESKTOP clicks reach the panel through @pmndrs/pointer-events, the layer uikit's own vanilla
 * docs prescribe ("since three.js ships no event system, no event system is available out of the
 * box"). It brings wheel, drag, capture and proper pointerover/out — the panel SCROLLS rather
 * than shrinking to fit, and that needs wheel and drag events the app's own three.interactive
 * wrapper does not have. It is also the package the R3F/XR stack uses, so a VR ray pointer can be
 * fed through the same pipeline later.
 *
 * Exactly one system may own the panel. Both were live at once briefly and that is a trap: they
 * each dispatch 'click' on the same object, so every action ran twice — which reads as "nothing
 * happened" on anything idempotent, and as a double spend on anything that is not. The
 * three.interactive path stays wired for board items (mg.three registers those); it is simply not
 * attached to panel widgets.
 *
 * VR is unaffected either way — a headset goes through mg.three's controller raycast, which reads
 * the `userData` that makeInteractive() writes.
 */

/**
 * Authored width in px per dock. A side dock is a narrow column; a top/bottom dock is a wide
 * strip, and giving it the same 520px would wrap its rows of tiles into a tall block that then
 * has to be scaled down to fit — tiny and unreadable. Width is the only lever here, since yoga
 * derives the height from the content.
 */
const PANEL_PX_SIDE = 520;
const PANEL_PX_EDGE = 1100;
const widthFor = (dock: Dock) => (dock === 'top' || dock === 'bottom') ? PANEL_PX_EDGE : PANEL_PX_SIDE;

// =====================================================================================
// DESIGN TOKENS.
//
// Every size, colour and radius in this file comes from here. That is the whole difference
// between a panel that looks designed and one that looks assembled: before this the file had
// 18px padding next to 12px next to 10px, five different greys for secondary text, and a 2px
// border in a UI where nothing else had one. One scale, used everywhere, reads as deliberate.
// =====================================================================================

/** 4px rhythm. Every gap, pad and margin is one of these. */
const SPACE = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 };

/** Corner radii, by element size. Small controls get small corners. */
const RADIUS = { sm: 6, md: 10, lg: 16, pill: 999 };

/** Type ramp. Ratios, not arbitrary numbers. */
const TYPE = { title: 26, body: 19, label: 17, small: 15, button: 20, chip: 17 };

/** Text colours, high to low emphasis. */
const INK = {
  hi: '#f1f5f9',      // titles, button labels
  base: '#dbe3ec',    // body copy
  mute: '#93a4b8',    // secondary / notes
  faint: '#6b7c91',   // captions, disabled
  accent: '#fbbf24',  // the one warm accent — icons and highlights
};

/** Surfaces, back to front. */
const SURFACE = {
  panel: '#0d1117',   // the panel itself
  raised: '#182029',  // controls sitting on the panel
  sunken: '#080b10',  // wells (log blocks, dropdown lists)
  line: '#242e3c',    // hairline borders
  lineSoft: '#1a222d',// dividers inside a panel
};

// `style` arrives as the same free-form keyword string the CSS classes used. Unknown keywords
// fall through to a neutral look, so the server can invent a style without breaking the client.
const FILL: { [k: string]: string } = {
  ok: '#15803d', no: '#b91c1c', primary: '#b45309', team: '#1d4ed8',
  win: '#15803d', lose: '#b91c1c', cur: '#b45309', ghost: '#1f2937',
};
const NEUTRAL = '#2a3546';
const has = (style: string | undefined, k: string) => (style || '').split(/[\s,]+/).includes(k);

/**
 * Lighten (+) or darken (-) a hex colour. Used for hover / pressed states and for the hairline
 * a solid control gets along its edge, so every variant is derived from ONE base colour instead
 * of being hand-picked per style keyword — add a keyword to FILL and its states come for free.
 */
function shade(hex: string, amount: number): string {
  if (!hex) return hex;
  const h = hex.replace('#', '');
  const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
  const n = parseInt(full, 16);
  if (isNaN(n)) return hex;
  const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(v)));
  const r = clamp(((n >> 16) & 255) + 255 * amount);
  const g = clamp(((n >> 8) & 255) + 255 * amount);
  const b = clamp((n & 255) + 255 * amount);
  return '#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join('');
}

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
 * dropping them. Real vector icons put the meaning back.
 *
 * There are TWO ways to ask for one, and the server should prefer the first:
 *
 *   1. BY NAME — UiNode.Icon, e.g. UiNode.Button("Play", ..., icon: "play"). Explicit, greppable,
 *      and it survives the label being reworded or translated.
 *   2. BY LEADING EMOJI — an emoji at the START of a label is replaced by the matching icon and
 *      stripped from the text. This is how every existing game asks for one, so it keeps working.
 *
 * An unrecognised name or emoji simply yields no icon — never a box, never a crash.
 */
const ICON_BY_NAME: { [name: string]: any } = {
  // actions
  play: Play, pause: Pause, stop: Square, skip: SkipForward, back: SkipBack,
  add: Plus, plus: Plus, remove: Minus, minus: Minus,
  'add-circle': CirclePlus, 'remove-circle': CircleMinus,
  ok: Check, check: Check, 'check-all': CheckCheck, 'check-circle': CircleCheck,
  cancel: X, close: X, no: CircleX, ban: Ban, delete: Trash2, trash: Trash2,
  undo: Undo2, redo: Redo2, rotate: RotateCw, 'rotate-ccw': RotateCcw, 'rotate-cw': RotateCw,
  repeat: Repeat, shuffle: Shuffle, send: Send, search: Search, settings: Settings,
  // navigation
  next: ChevronRight, prev: ChevronLeft, up: ChevronUp, down: ChevronDown,
  right: ArrowRight, left: ArrowLeft,
  // game concepts
  dice: Dice6, die: Dice6, roll: Dice6,
  music: Music, sound: Volume2, volume: Volume, mute: Ban,
  eye: Eye, look: Eye, hidden: Ban,
  day: Sun, night: Moon, vote: Scale, judge: Scale,
  flag: Flag, finish: Flag, trophy: Trophy, star: Star, target: Target,
  sword: Swords, attack: Swords, shield: Shield, defend: Shield,
  coin: Coins, coins: Coins, gem: Gem, crown: Crown,
  player: User, players: Users, hand: Hand, card: Layers,
  build: Hammer, hammer: Hammer, mine: Pickaxe, wheat: Wheat, wood: Trees,
  mountain: Mountain, map: Map, pin: MapPin, ship: Ship, anchor: Anchor,
  time: Clock, timer: Timer, energy: Zap, heart: Heart, fire: Flame,
  magic: Wand, sparkle: Sparkles, book: Book, scroll: Scroll, lock: Lock,
  // status
  info: Info, help: CircleHelp, warning: TriangleAlert, chat: MessageSquare, lab: FlaskConical,
};

/** Leading-emoji shorthand for the same set. */
const ICONS: { [ch: string]: any } = {
  '🎲': Dice6, '🎵': Music, '🔊': Volume2, '👁': Eye, '🚫': Ban, '🗑': Trash2,
  '✖': X, '✕': X, '⟲': RotateCcw, '⟳': RotateCw, '⏹': Square, '▶': Play,
  // These were all being silently dropped: the emoji is not in the MSDF atlas, so the button
  // just lost its symbol.
  '➕': Plus, '➖': Minus, '✓': Check, '✔': Check, '→': ArrowRight, '←': ArrowLeft,
  '⏭': SkipForward, '⏮': SkipBack, '🏁': Flag, '🌙': Moon, '☀': Sun, '⚖': Scale,
  '🧪': FlaskConical, '💬': MessageSquare, '⚔': Swords, '🛡': Shield, '👑': Crown,
  '💰': Coins, '💎': Gem, '⏸': Pause, '🔇': Ban, '⭐': Star, '❤': Heart, '🔥': Flame,
  '⏱': Timer, '⚡': Zap, '🏆': Trophy, '📖': Book, '📜': Scroll, '🔒': Lock, '✨': Sparkles,
};

/** Split a leading icon off a label: the icon class (if recognised) and the remaining text. */
function splitIcon(label: string): { Icon?: any, rest: string } {
  const trimmed = (label || '').trim();
  for (const ch of Object.keys(ICONS)) {
    if (trimmed.startsWith(ch)) return { Icon: ICONS[ch], rest: trimmed.slice(ch.length).trim() };
  }
  return { rest: label };
}

/** An explicit icon name wins over a leading emoji; either may be absent. */
function resolveIcon(name: string | undefined, label: string): { Icon?: any, rest: string } {
  const named = name ? ICON_BY_NAME[name.trim().toLowerCase()] : undefined;
  if (named) return { Icon: named, rest: (label || '').trim() };
  return splitIcon(label);
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

/**
 * WHERE a panel lives.
 *   'screen' - pinned to an edge of this viewer's view; follows the camera; only the owner can
 *              ever see it, because it exists in that camera's space and nowhere else.
 *   'world'  - standing in the scene at a fixed transform. The camera orbits around it, and it is
 *              the ONLY anchor another player can see — which is what makes a visible hand of
 *              cards, or a shared scoreboard on the table, possible at all.
 * The server names the default per panel; the player may override their own (placement has always
 * been the client's business).
 */
export type Anchor = 'screen' | 'world';

/**
 * One seat's panel tree, as handed to the renderer.
 *
 * The panel used to render exactly ONE seat — the viewer's own. It now renders every seat whose
 * panel the server published to the table, which is what makes a visible hand of cards possible:
 * `interactive` is false for those, so you can see another player holding cards without being able
 * to play them.
 */
export interface PanelSource {
  seatId: string;
  screen: UiNode[] | null;
  interactive: boolean;      // true only for the viewer's own seat
}

/**
 * World units per authored pixel for a panel carried by an ITEM.
 *
 * This is FIXED on purpose. A docked panel derives its scale from the viewport, which is what made
 * it resize and flicker; an item-carried panel must not, so instead every one of them shares one
 * pixel size and the panel's authored WIDTH follows the physical width the server asked for.
 *
 * The value: at the usual HUD distance (~1.4 units, 75° FOV) the view is ~2.15 units tall, so on a
 * ~900px viewport one screen pixel is ~0.0024 world units. Matching that means one authored px is
 * about one screen px — which is the whole reason UI authored in px looks right.
 *
 * (The bug this replaces: every panel was authored at PANEL_PX_SIDE = 520 and then squeezed into
 * whatever width the server wanted, so a 0.2-unit button panel had pixelSize 0.00038 and its label
 * came out two screen pixels tall — a row of illegible coloured bars.)
 */
const ITEM_PANEL_PX = 0.0024;

/** Default physical width of a world panel when the game does not say. */
const WORLD_WIDTH_DEFAULT = 1.2;
/** How far in front of the eye a user-pinned panel is dropped when the game gave no position. */
const PIN_DIST = 2.5;

/**
 * A slot in the panel's flexbox that holds REAL geometry instead of uikit widgets.
 *
 * `box` is an ordinary uikit Container, so yoga lays it out, scrolls it and clips it exactly like
 * any other node. `group` is plain three.js and is a SIBLING of the uikit root inside the pane —
 * every frame it is moved onto the box. That is possible because uikit's `globalMatrix` is
 * expressed in WORLD UNITS in the pane root's own space (buildRootMatrix multiplies by pixelSize),
 * and it already carries the scroll offset, so the geometry scrolls with the content for free.
 */
interface ItemSlot {
  box: any;                 // the uikit Container acting as the layout slot
  group: THREE.Group;       // the geometry, positioned onto the slot each frame
  slotPx: number;           // authored slot height in px; the item is scaled to it
}

/** How much of its slot an item fills, leaving a little air around it. */
const ITEM_FILL = 0.86;

/** One panel's placement instruction, as read off the server tree. */
interface PanelSpec {
  dock: Dock;
  nodes: UiNode[];
  anchor: Anchor;
  isPublic: boolean;
  at?: { x: number, y: number, z: number };
  rot?: { x: number, y: number, z: number };
  worldWidth?: number;
}

/** Distance in front of the eye that docked panels hang at, in world units. */
const HUD_DIST = 2;
/** How wide a panel is when carried in VR, in metres. */
const HAND_WIDTH = 0.42;
/** How tall a hand-carried panel may get before it scrolls, in metres. */
const HAND_MAX_HEIGHT = 0.30;

interface Pane {
  dock: Dock;
  anchor: Anchor;           // 'screen' -> camera-docked; 'world' -> standing in the scene
  ownerSeatId: string;      // whose panel this is; actions dispatch as this seat
  interactive: boolean;     // false for somebody else's public panel — look, don't touch
  at?: { x: number, y: number, z: number };    // world transform (anchor === 'world')
  rot?: { x: number, y: number, z: number };   // degrees
  worldWidth?: number;      // physical width in world units (anchor === 'world')
  slots: ItemSlot[];        // real-geometry slots ('item3d' nodes) living in this pane
  key: string;              // content fingerprint; unchanged key = keep this panel as it is
  clickables: any[];        // registered with the InteractionManager, released with the pane
  group: THREE.Group;       // positions the panel; a child of MgPanel3d.group
  root: Container;          // the uikit tree
  wPx: number;              // authored width in px (depends on the dock)
  px: number;               // current pixelSize
  maxHPx: number;           // current maxHeight in px; content past it scrolls
}

function dockOf(style: string | undefined): Dock {
  const s = (style || '').toLowerCase();
  for (const d of DOCKS) if (s.split(/[\s,]+/).includes(d)) return d;
  return 'right';                       // unknown or absent -> the default edge
}

/**
 * Split a seat's Screen into panels.
 *
 * A `panel` node becomes one panel, pinned to the edge named in its Style. Anything that is NOT
 * a panel node is collected into one implicit panel docked right — which is exactly what every
 * game that never mentions panels already gets, so this is backward compatible by construction.
 *
 * Panels are HOISTED from anywhere in the tree, not just the top level. UiNode.Panel() is an
 * ordinary node server-side, so a game can legally build one inside a col/row; when only the top
 * level was inspected such a node fell through to the `default` branch of build() and rendered as
 * one empty Text — silently discarding its whole subtree. A panel is a placement instruction, so
 * hoisting it out of its container is the only reading that makes sense.
 */
function anchorOf(nd: UiNode): Anchor {
  return (nd.anchor || '').toLowerCase() === 'world' ? 'world' : 'screen';
}

function specOf(nd: UiNode, nodes: UiNode[]): PanelSpec {
  const anchor = anchorOf(nd);
  return {
    dock: dockOf(nd.style),
    nodes,
    anchor,
    // "public" only means anything in the world: there is no way to show one player's screen-space
    // HUD to anybody else, so it is ignored on a screen panel rather than half-honoured.
    isPublic: anchor === 'world' && (nd.visibility || '').toLowerCase() === 'public',
    at: nd.at,
    rot: nd.rot,
    worldWidth: nd.worldWidth,
  };
}

function splitPanels(screen: UiNode[]): PanelSpec[] {
  const out: PanelSpec[] = [];
  let loose: UiNode[] | null = null;

  /** Strip panel descendants out of a subtree, pushing each onto `out`; return what's left. */
  const hoist = (nodes: UiNode[]): UiNode[] => {
    const kept: UiNode[] = [];
    for (const nd of nodes) {
      if ((nd.type || '').toLowerCase() === 'panel') {
        out.push(specOf(nd, hoist(nd.children || [])));
      } else if (nd.children?.length) {
        // Copy rather than mutate: `screen` is the server's payload and the pane fingerprint is
        // taken from it, so rewriting it in place would make the cache key disagree with itself.
        kept.push({ ...nd, children: hoist(nd.children) });
      } else {
        kept.push(nd);
      }
    }
    return kept;
  };

  for (const nd of screen) {
    if ((nd.type || '').toLowerCase() === 'panel') {
      out.push(specOf(nd, hoist(nd.children || [])));
    } else {
      const [rest] = hoist([nd]);
      if (rest) {
        if (!loose) {
          loose = [];
          // The implicit panel every game that never mentions panels already gets.
          out.push({ dock: 'right', nodes: loose, anchor: 'screen', isPublic: false });
        }
        loose.push(rest);
      }
    }
  }
  return out;
}

/**
 * Is this object actually on screen? Walks up the parents checking three's `visible`.
 *
 * Needed because three's raycaster does NOT skip invisible objects, and a CLOSED dropdown's
 * option rows are still registered click targets sitting behind the closed head — without this
 * a VR controller ray could hit and fire a hidden option.
 */
function visibleInTree(o: any): boolean {
  for (let x = o; x != null; x = x.parent) if (x.visible === false) return false;
  return true;
}

/** Only push a new pixelSize when it actually changed — it invalidates uikit's layout. */
function setPixelSize(p: Pane, v: number) {
  if (!isFinite(v) || v <= 0 || Math.abs(v - p.px) < 1e-9) return;
  p.px = v;
  p.root.setProperties({ pixelSize: v });
}

/** Same deal for maxHeight: pushing it every frame would re-run yoga every frame. */
function setMaxHeightPx(p: Pane, v: number) {
  if (!isFinite(v) || v <= 0 || Math.abs(v - p.maxHPx) < 0.5) return;
  p.maxHPx = v;
  p.root.setProperties({ maxHeight: v });
}

/**
 * The panel's REAL laid-out height in px, straight from yoga — or null before the first layout.
 *
 * This replaced a hand-written estimator that walked the UiNode tree guessing at row heights,
 * wrapped-text growth and dropdown states. Guessing was only ever needed because the panel had to
 * be shrunk to fit its content; now it scrolls instead, so the one remaining use is stacking
 * panels on the same edge, and for that the actual number is available for free.
 */
function measuredHeightPx(p: Pane): number | null {
  const s: any = (p.root as any).size?.value;
  const h = Array.isArray(s) ? s[1] : undefined;
  return typeof h === 'number' && isFinite(h) && h > 0 ? h : null;
}

export class MgPanel3d {

  /** SCREEN-anchored panels live under this. Parented to the camera, or to a hand in VR. */
  readonly group = new THREE.Group();

  /**
   * WORLD-anchored panels live under this instead, and it is added to the SCENE — not the camera.
   * That is the whole difference: these panels stay where they are put while the camera orbits,
   * and because they are ordinary scene objects another player's client can render them too.
   */
  readonly worldGroup = new THREE.Group();

  /**
   * Build the geometry for an 'item3d' node. Wired by game-play to MgGame.buildPanelItem, so the
   * panel never needs to know anything about assets — it only positions what it is handed.
   */
  makeItem?: (item: any) => THREE.Object3D | null;

  /**
   * The player's own placement preference, overriding whatever the game asked for. Placement has
   * always been the client's business (see CLAUDE.md), so this never goes near the server; it is a
   * per-viewer convenience and lives in localStorage.
   */
  private anchorOverride: Anchor | null = null;

  /**
   * Where a user-pinned panel was dropped. Set when the player forces 'world' on a panel the game
   * gave no position for: we take the spot in front of the camera at that moment, so "pin it" puts
   * the panel where they were already looking and then leaves it alone.
   */
  private pinned: { at: { x: number, y: number, z: number }, rot: { x: number, y: number, z: number } } | null = null;

  private panes: Pane[] = [];
  /** The pane being built, so its interactive objects are recorded against it. */
  private building?: Pane;
  private seatId = '';

  /** Local widget state that must survive a rebuild. The SERVER still decides what it means. */
  private picks: { [argKey: string]: string[] } = {};      // "checks" groups, mid-selection
  private fields: { [id: string]: string } = {};           // "select" values, for `gather`
  private open: { [id: string]: boolean } = {};            // which dropdowns are expanded

  /** Panes the cursor is currently inside. A set, because docks can sit edge to edge. */
  private hovered = new Set<Pane>();

  private setHover(p: Pane, on: boolean) {
    if (on) this.hovered.add(p); else this.hovered.delete(p);
    this.mgThree?.setUiHover?.(this.hovered.size > 0);
  }

  /** Non-null while the panels ride a VR controller. */
  private attachedTo: THREE.Object3D | null = null;

  constructor(
    private mgThree: any,
    /** Report an activation to the server. Identical payload to what the DOM panel sent. */
    private dispatch: (action: string, args: { [k: string]: string }) => void,
  ) {
    this.group.name = 'PANEL3D';
    this.worldGroup.name = 'PANEL3D:WORLD';
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

  /**
   * Force this viewer's panels to a placement, or pass null to go back to whatever each game asked
   * for. Switching TO 'world' with no game-supplied position pins the panel where the player is
   * currently looking.
   */
  setAnchorOverride(a: Anchor | null) {
    if (a === this.anchorOverride) return;
    this.anchorOverride = a;
    if (a === 'world') this.pinned = this.pinInFrontOfCamera();
    else this.pinned = null;
    // Anchor is part of the pane fingerprint, so the next update() reparents and rebuilds.
  }

  get anchorPreference(): Anchor | null { return this.anchorOverride; }

  /** The spot the player is looking at, PIN_DIST in front of the eye, facing back at them. */
  private pinInFrontOfCamera() {
    const cam = this.mgThree?.camera as THREE.Camera | undefined;
    if (!cam) return { at: { x: 0, y: 1.5, z: 0 }, rot: { x: 0, y: 0, z: 0 } };
    const dir = new THREE.Vector3();
    cam.getWorldDirection(dir);
    const at = cam.getWorldPosition(new THREE.Vector3()).add(dir.multiplyScalar(PIN_DIST));
    const e = new THREE.Euler().setFromQuaternion(cam.getWorldQuaternion(new THREE.Quaternion()));
    const deg = (r: number) => r * 180 / Math.PI;
    return { at: { x: at.x, y: at.y, z: at.z }, rot: { x: deg(e.x), y: deg(e.y), z: deg(e.z) } };
  }

  /** Apply the viewer's placement preference to what the game asked for. */
  private withOverride(sp: PanelSpec): PanelSpec {
    if (!this.anchorOverride || this.anchorOverride === sp.anchor) return sp;
    if (this.anchorOverride === 'screen') return { ...sp, anchor: 'screen' };
    const p = this.pinned ?? this.pinInFrontOfCamera();
    return { ...sp, anchor: 'world', at: sp.at ?? p.at, rot: sp.rot ?? p.rot };
  }

  /**
   * Build a uikit panel as a FREE-STANDING scene object, for an item to carry (asset type PANEL).
   *
   * This is the whole "a panel is 3D geometry" idea made usable: the caller owns where it sits and
   * how big it is, and this only builds the contents. It is deliberately NOT registered in
   * `panes`, so `place()` never touches it — no camera-frustum fitting, no per-frame re-placement,
   * which is exactly what made the docked panels flicker and resize.
   *
   * pixelSize is set ONCE from the physical width the caller asked for. The layout runs once, here;
   * a rebuild happens when the server sends different content, same as a docked panel.
   */
  buildDetached(nodes: UiNode[], worldWidth: number, seatId: string, interactive = true): THREE.Group {
    // Authored width follows the requested PHYSICAL width at a fixed pixel size, so text is the
    // same legible size in every item-panel no matter how wide the panel is.
    const wPx = Math.max(80, Math.round(worldWidth / ITEM_PANEL_PX));
    const group = new THREE.Group();
    group.name = 'PANEL3D:ITEM';

    const root = new Container({
      width: wPx,
      flexDirection: 'column',
      alignItems: 'stretch',
      padding: SPACE.lg,
      backgroundColor: SURFACE.panel,
      opacity: 0.94,
      borderRadius: RADIUS.lg,
      borderWidth: 1,
      borderColor: SURFACE.line,
      // mg.three sets scene.pointerEvents = 'none' for the board; opt this subtree back in or
      // @pmndrs/pointer-events never sees a widget.
      pointerEvents: 'auto',
    });
    group.add(root);

    const content = new Container({
      flexDirection: 'column',
      alignItems: 'stretch',
      gap: 10,
      width: '100%',
      flexShrink: 0,
    });
    root.add(content);

    const px = ITEM_PANEL_PX;
    const pane: Pane = {
      dock: 'right', anchor: 'world', ownerSeatId: seatId, interactive,
      key: 'item-panel', clickables: [], slots: [],
      group, root, wPx, px, maxHPx: 0,
    };
    root.setProperties({ pixelSize: px });

    this.building = pane;
    try { for (const nd of nodes) this.build(nd, content); }
    finally { this.building = undefined; }

    // One layout now, so the panel has a real size the moment it is added to the scene.
    try { root.update(0); } catch { /* the next frame can do it */ }

    this.detached.push(pane);
    return group;
  }

  /** Panels carried by ITEMS. Kept apart from `panes` so the placement code cannot reach them. */
  private detached: Pane[] = [];

  /** Release an item-carried panel (its item was removed from the scene). */
  disposeDetached(group: THREE.Object3D) {
    const i = this.detached.findIndex(p => p.group === group);
    if (i < 0) return;
    this.clearPane(this.detached[i]);
    this.detached.splice(i, 1);
  }

  // ------------------------------------------------------------------ lifecycle

  /** @returns true if anything is on screen (false = nothing to show at all). */
  update(sources: PanelSource[], mySeatId: string): boolean {
    this.seatId = mySeatId;
    this.reparent();

    // Flatten every seat's panels into one list, each tagged with who owns it.
    //
    // Somebody else's panels are filtered to the ones they PUBLISHED to the table: a screen-docked
    // panel exists only in its owner's camera space, so there is nothing to draw for it here even
    // if the server sent it. The viewer's own placement override applies only to their own panels —
    // moving another player's hand around your view would be nonsense.
    const specs: (PanelSpec & { ownerSeatId: string, interactive: boolean })[] = [];
    for (const src of sources) {
      if (!src.screen || !src.screen.length) continue;
      for (let sp of splitPanels(src.screen)) {
        if (src.interactive) sp = this.withOverride(sp);
        else if (!(sp.anchor === 'world' && sp.isPublic)) continue;
        specs.push({ ...sp, ownerSeatId: src.seatId, interactive: src.interactive });
      }
    }

    if (!specs.length) { this.clear(); this.group.visible = false; return false; }
    this.group.visible = true;

    // Rebuild PER PANEL, not per screen. A click usually changes one panel (the DM's die roll does
    // not touch his scene buttons), and tearing down a panel that did not change is both wasted
    // work and a visible flicker. The local widget state is part of the fingerprint so a
    // half-finished "checks" selection still survives.
    // NOTE `open` is deliberately NOT in here: a dropdown opens by toggling its list in place, so
    // including it would rebuild the panel for something purely visual — the old flicker.
    const local = JSON.stringify(this.picks) + '#' + JSON.stringify(this.fields);
    const keyed = specs.map(sp => ({
      ...sp,
      // Anchor is part of the fingerprint: switching placement changes the pane's PARENT, so it
      // has to be rebuilt rather than merely repositioned. So is the OWNER, so one seat's panel is
      // never mistaken for another's when a seat joins or leaves.
      key: sp.ownerSeatId + '#' + sp.interactive + '#' + sp.dock + '#' + sp.anchor + '#'
           + JSON.stringify(sp.nodes) + '#' + local + '#' + this.clipSig(sp.nodes),
    }));

    // If the panel LIST changed shape, positional matching is meaningless — start over.
    const sameShape = keyed.length === this.panes.length
      && keyed.every((k, i) => k.dock === this.panes[i].dock && k.anchor === this.panes[i].anchor);
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


    // Seat any real geometry ('item3d') now too. This has to come AFTER safePlace (which sets
    // pixelSize) and after a layout pass (which is what produces the slot matrices we read).
    //
    // It is done HERE, and not only in tick(), on purpose: tick() is driven by mg.three's frame
    // hook, and that hook is currently never firing (see the note on tick) — so a panel is laid
    // out once per rebuild and nothing else. Seating the geometry on the same schedule as the
    // layout it is derived from keeps the two in step regardless.
    for (const p of this.panes) {
      if (!p.slots.length) continue;
      try { p.root.update(0); } catch { /* layout can wait */ }
      this.syncSlots(p);
    }



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

  private buildPane(spec: PanelSpec & { key: string, ownerSeatId?: string, interactive?: boolean }): Pane {
    const paneGroup = new THREE.Group();
    paneGroup.name = 'PANEL3D:' + spec.anchor + ':' + spec.dock;
    // THE anchor difference, in one line: a screen panel hangs off the camera, a world panel off
    // the scene. Everything below is identical for both.
    (spec.anchor === 'world' ? this.worldGroup : this.group).add(paneGroup);

    const wPx = widthFor(spec.dock);
    // No height: yoga derives it from the content, up to the maxHeight place() sets.
    const root = new Container({
      width: wPx,
      flexDirection: 'column',
      alignItems: 'stretch',
      padding: SPACE.lg,
      backgroundColor: SURFACE.panel,
      opacity: 0.94,
      borderRadius: RADIUS.lg,
      // 1px, not 2px. A 2px border on a floating panel reads as a debug outline; a hairline
      // reads as an edge.
      borderWidth: 1,
      borderColor: SURFACE.line,
      // mg.three sets scene.pointerEvents = 'none' so board items belong to the InteractionManager
      // alone. That 'none' is INHERITED, so the panel has to opt its own subtree back in here or
      // @pmndrs/pointer-events would never see a single widget. 'auto' is what uikit itself uses
      // (defaultPointerEvents = 'auto' on every component), so this restores uikit's own default
      // rather than inventing a policy; individual nodes still override it — the dropdown list
      // toggles between 'auto' and 'none' to stop a closed list swallowing clicks.
      pointerEvents: 'auto',
      // A panel is no longer shrunk until it fits — it keeps a readable size and SCROLLS. That is
      // only possible because @pmndrs/pointer-events feeds uikit the wheel and drag events it
      // needs; the InteractionManager has neither, and would clip the overflow unreachably.
      overflow: 'scroll',
      scrollbarWidth: SPACE.xs + 2,
      scrollbarColor: SURFACE.line,
      scrollbarBorderTopLeftRadius: 3,
      scrollbarBorderTopRightRadius: 3,
      scrollbarBorderBottomLeftRadius: 3,
      scrollbarBorderBottomRightRadius: 3,
    });
    paneGroup.add(root);

    // The classic scroll-viewport / content pair, and it is NOT optional. `root` is the viewport:
    // it has the maxHeight and clips. Every node goes into `content`, which carries flexShrink: 0
    // so yoga lays it out at its natural height and lets it overflow.
    //
    // Building straight into a height-capped root instead does what flexbox always does when a
    // column is too small: it SHRINKS the children. Rows collapsed into each other and section
    // labels drew on top of the buttons below them — which looked like a rendering bug and is
    // really just flex-shrink doing its job.
    const content = new Container({
      flexDirection: 'column',
      alignItems: 'stretch',
      gap: 10,
      width: '100%',
      flexShrink: 0,
    });
    root.add(content);

    const pane: Pane = {
      dock: spec.dock, key: spec.key, clickables: [],
      anchor: spec.anchor,
      ownerSeatId: spec.ownerSeatId ?? this.seatId,
      // Only your own panel is clickable. A public panel belonging to somebody else is scenery:
      // it shows you their hand backs, it does not let you play their cards.
      interactive: spec.interactive !== false,
      at: spec.at, rot: spec.rot, worldWidth: spec.worldWidth,
      slots: [],
      group: paneGroup, root, wPx, px: 0, maxHPx: 0,
    };

    // Hand the camera controls off while the cursor is on this panel — otherwise scrolling the
    // panel also zooms the view. enter/leave rather than over/out on purpose: those two bubble, so
    // moving between a panel's own children would report leaving the panel it never left.
    (root as any).addEventListener('pointerenter', () => this.setHover(pane, true));
    (root as any).addEventListener('pointerleave', () => this.setHover(pane, false));
    this.building = pane;
    try { for (const nd of spec.nodes) this.build(nd, content); }
    finally { this.building = undefined; }
    // Run one layout NOW so place() below has a real height to stack with on the very first frame.
    // Yoga works in px and knows nothing about pixelSize or maxHeight yet, so this is safe to do
    // before either is set — and it is what keeps a fresh panel from being positioned off a guess.
    try { root.update(0); } catch { /* first layout can wait for the next frame */ }
    return pane;
  }

  /**
   * uikit computes layout and flushes transforms once per frame; mg.three drives this through its
   * frame-hook list.
   *
   * KNOWN ISSUE (2026-08-27): this is NOT actually running. game-play registers
   * `panel3d.frameHook` with `mgThree.addFrameHook`, and mg.three's animationLoop iterates
   * `frameHooks` unconditionally, yet a probe at the top of this method never fired on a fresh
   * load. The panel still looks right because buildPane lays out once and update() calls
   * safePlace() at the end, so every SERVER update re-lays-out the panel — which is most of them.
   * What is lost is anything that needs a real per-frame pass: scrolling a long panel, and
   * reflowing on a window resize before the next server update.
   * (Related, and separate: disposePanel3d never calls mgThree.removeFrameHook, so the hook leaks
   * when leaving a game.)
   */
  tick(deltaMs: number) {
    if (!this.panes.length && !this.detached.length) return;
    // place() FIRST: it sets each panel's pixelSize, and root.update() below is what consumes it.
    // Laying out first and correcting after costs one visibly wrong frame — which is exactly the
    // flash this used to produce on every click, because a fresh root starts at uikit's default
    // pixelSize of 0.01 (a 520px panel = 5.2 world units, two units from the eye: enormous).
    this.safePlace();
    for (const p of this.panes) p.root.update(deltaMs);
    // Geometry LAST: it is seated from uikit's own layout, so it has to read a fresh one.
    for (const p of this.panes) if (p.slots.length) this.syncSlots(p);
    // Item-carried panels are laid out too, but never PLACED — their item owns their transform.
    for (const p of this.detached) p.root.update(deltaMs);
  }

  /**
   * Move each 'item3d' group onto the uikit box that reserves its space.
   *
   * `globalMatrix` is in WORLD UNITS in the pane root's local space and already includes the
   * scroll offset, so its translation is exactly where the slot's centre currently is. Only the
   * position is taken from it: the group is a child of the pane, so it already inherits the
   * panel's own orientation, and the item's own rotation was applied when it was built.
   */
  private syncSlots(p: Pane) {
    for (const s of p.slots) {
      const m: THREE.Matrix4 | undefined = s.box?.globalMatrix?.value;
      // Clipped means scrolled out of the viewport. uikit clips its own meshes in the shader, but
      // plain geometry is not clipped by anything — without this a card scrolled out of the panel
      // would go on floating in mid-air.
      if (!m || s.box?.isClipped?.value) { s.group.visible = false; continue; }
      s.group.visible = true;
      s.group.position.setFromMatrixPosition(m);
      // The item is normalised to ~1 unit, so scaling by the slot's physical height fits it.
      s.group.scale.setScalar(s.slotPx * p.px * ITEM_FILL);
    }
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
    // World panels are placed by their own transform and are unaffected by the view or by VR.
    this.placeWorld();

    const screenPanes = this.panes.filter(p => p.anchor === 'screen');
    if (!screenPanes.length) return;

    if (this.attachedTo) { this.placeOnHand(screenPanes); return; }
    this.group.position.set(0, 0, 0);
    this.group.rotation.set(0, 0, 0);
    this.group.scale.setScalar(1);

    const cam = this.mgThree.camera as THREE.PerspectiveCamera;
    if (!cam?.isPerspectiveCamera) return;
    const viewH = 2 * HUD_DIST * Math.tan((cam.fov * Math.PI / 180) / 2);   // world height at that depth
    const viewW = viewH * cam.aspect;
    const margin = viewW * 0.015;
    const gap = viewH * 0.02;

    // ONE text size for the whole HUD, taken from a side panel filling its column. Every dock then
    // uses it, so a wide bottom strip reads exactly like a narrow side column.
    //
    // Scaling each dock to fill its own width instead — the obvious thing — makes the 1100px edge
    // panels about 2.3x larger than the 520px side panels, which on a wide monitor is comically big
    // and pushes rows into each other. Deriving the size once, from the view rather than from the
    // panel, is what keeps a game looking the same on every screen.
    const hudPx = (viewW * 0.30) / PANEL_PX_SIDE;

    for (const dock of DOCKS) {
      const group = screenPanes.filter(p => p.dock === dock);
      if (!group.length) continue;
      const n = group.length;
      const vertical = dock === 'right' || dock === 'left';

      // Share the edge between the panels pinned to it: along the edge each gets a slice, across
      // it they all get the same allowance. allowH is now a SCROLL LIMIT rather than something to
      // shrink into — a panel taller than its slice keeps its size and scrolls.
      const allowW = vertical ? viewW * 0.30 : (viewW * 0.94) / n;
      const allowH = vertical ? (viewH * 0.94) / n : (viewH * 0.42) / n;
      for (const p of group) {
        // The shared HUD size, narrowed only if this panel would not otherwise fit its slice.
        // Height is NOT part of this any more: the old code fitted both axes, so a content-heavy
        // panel shrank itself until its text was unreadable. Too tall now means scrolling.
        setPixelSize(p, Math.min(hudPx, allowW / p.wPx));
        setMaxHeightPx(p, allowH / p.px);
      }

      // Stack with the height yoga actually produced, clamped to the slot. Before the first layout
      // (or if uikit has not published a size yet) assume a full slot: erring tall keeps panels
      // from overlapping, and place() runs every frame so it corrects itself immediately.
      const size = (p: Pane) => ({
        w: p.wPx * p.px,
        h: Math.min((measuredHeightPx(p) ?? p.maxHPx) * p.px, allowH),
      });
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
  private placeOnHand(screenPanes: Pane[]) {
    this.group.position.set(0.12, 0.04, -0.22);
    this.group.rotation.set(-Math.PI / 5, -0.35, 0);
    this.group.scale.setScalar(1);

    const gap = 0.02;
    let cursor = 0;
    for (const p of screenPanes) {
      setPixelSize(p, HAND_WIDTH / p.wPx);
      setMaxHeightPx(p, HAND_MAX_HEIGHT / p.px);
      const h = Math.min((measuredHeightPx(p) ?? p.maxHPx) * p.px, HAND_MAX_HEIGHT);
      p.group.position.set(0, -cursor - h / 2, 0);
      p.group.rotation.set(0, 0, 0);
      cursor += h + gap;
    }
  }

  /**
   * Stand the world-anchored panels in the scene.
   *
   * Unlike a screen panel there is no view to derive a size from — a world panel has a PHYSICAL
   * size next to the board, and only the game knows what that should be — so pixelSize comes from
   * WorldWidth instead of from the frustum. Position and rotation are simply applied; the camera
   * is not consulted at all, which is the entire point of this anchor.
   */
  private placeWorld() {
    const worldPanes = this.panes.filter(p => p.anchor === 'world');
    if (!worldPanes.length) return;

    // Parent to the SCENE (lazily — the scene may not exist when the panel is constructed).
    const scene = this.mgThree?.scene;
    if (scene && this.worldGroup.parent !== scene) scene.add(this.worldGroup);
    this.worldGroup.position.set(0, 0, 0);
    this.worldGroup.rotation.set(0, 0, 0);
    this.worldGroup.scale.setScalar(1);

    const d2r = (d: number) => d * Math.PI / 180;
    for (const p of worldPanes) {
      const w = p.worldWidth ?? WORLD_WIDTH_DEFAULT;
      setPixelSize(p, w / p.wPx);
      // Physical width implies a physical height budget: without one a long log turns the panel
      // into a tower beside the board instead of scrolling.
      setMaxHeightPx(p, (w * 1.6) / p.px);

      const at = p.at ?? { x: 0, y: 1.5, z: 0 };
      const rot = p.rot ?? { x: 0, y: 0, z: 0 };
      p.group.position.set(at.x, at.y, at.z);
      p.group.rotation.set(d2r(rot.x), d2r(rot.y), d2r(rot.z));
    }
  }

  dispose() {
    this.clear();
    for (const p of this.detached) this.clearPane(p);
    this.detached = [];
    this.worldGroup.removeFromParent();
    // clear() releases the hover set pane by pane, but say it once more outright: leaving the view
    // with the cursor on a panel must not leave the camera controls switched off.
    this.mgThree?.setUiHover?.(false);
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
    // A pane destroyed while the cursor was on it never gets its pointerleave, so the camera
    // controls would stay switched off for good. Drop it from the hover set explicitly.
    this.setHover(p, false);
    for (const b of p.clickables) {
      try {
        b.removeEventListener('click', b.userData['__onClick']);
      } catch { /* already gone */ }
    }
    p.clickables = [];
    // Release the real-geometry slots with the pane, or a rebuild strands their meshes in the scene.
    for (const s of p.slots) {
      s.group.removeFromParent();
      s.group.traverse((o: any) => {
        if (o.isMesh) {
          o.geometry?.dispose?.();
          const mats = Array.isArray(o.material) ? o.material : [o.material];
          for (const mm of mats) mm?.dispose?.();
        }
      });
    }
    p.slots = [];
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
          // A row of buttons should sit on one baseline; a column should fill the panel width so
          // its children line up on a single left edge instead of each centring itself.
          alignItems: isRow ? 'center' : 'stretch',
          justifyContent: 'flex-start',
          // `size` is the GAP on a container, as in CSS. Rows are tighter than columns: side by
          // side needs less separation than stacked.
          gap: nd.size ?? (isRow ? SPACE.sm : SPACE.md),
          ...(nd.bg ? {
            backgroundColor: col(nd.bg, SURFACE.raised),
            borderRadius: RADIUS.md,
            borderWidth: 1,
            borderColor: SURFACE.lineSoft,
            padding: SPACE.md,
          } : {}),
        });
        parent.add(box);
        for (const k of nd.children || []) this.build(k, box);
        return;
      }

      case 'title': {
        // The icon carries the accent colour and the words stay near-white. An all-amber heading
        // shouts; an amber mark next to calm type reads as a brand. Size/Color are honoured (both
        // used to be hardcoded, so a server that set them on a title was ignored).
        const { Icon, rest } = resolveIcon(nd.icon, nd.text || '');
        const size = nd.size ?? TYPE.title;
        const colour = col(nd.color, INK.hi);

        const head = new Container({
          flexDirection: 'row', alignItems: 'center', gap: SPACE.sm,
          paddingBottom: SPACE.sm,
          // A hairline under the heading is the cheapest possible "this is a designed thing"
          // signal, and it gives the panel an obvious top section.
          borderBottomWidth: 1, borderColor: SURFACE.lineSoft,
          marginBottom: SPACE.xs,
        });
        parent.add(head);
        if (Icon) head.add(new Icon({ width: size * 0.9, height: size * 0.9, color: col(nd.color, INK.accent), flexShrink: 0 }));
        head.add(new Text({ text: txt(rest), fontSize: size, fontWeight: 'bold', color: colour }));
        return;
      }

      case 'note':
        parent.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? TYPE.label, color: col(nd.color, INK.mute) }));
        return;

      case 'banner': {
        const base = fill(nd.style);
        const box = new Container({
          backgroundColor: base,
          // A lighter hairline along the edge of a saturated block stops it looking like a flat
          // sticker — it is the same trick the buttons use below.
          borderWidth: 1, borderColor: shade(base, 0.12),
          borderRadius: RADIUS.md, padding: SPACE.md, marginY: SPACE.xs,
          alignItems: 'center', justifyContent: 'center',
        });
        parent.add(box);
        box.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? TYPE.title, fontWeight: 'bold', color: col(nd.color, '#ffffff') }));
        return;
      }

      case 'log': {
        // A well: darker than the panel, with a hairline, so a running log reads as a distinct
        // region rather than as more body text.
        const box = new Container({
          backgroundColor: col(nd.bg, SURFACE.sunken),
          borderWidth: 1, borderColor: SURFACE.lineSoft,
          borderRadius: RADIUS.sm, padding: SPACE.md,
          flexDirection: 'column', alignItems: 'flex-start',
        });
        parent.add(box);
        box.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? TYPE.small, color: col(nd.color, INK.mute) }));
        return;
      }

      case 'space':
        parent.add(new Container({ height: nd.size ?? SPACE.sm, flexShrink: 0 }));
        return;

      case 'image': {
        const h = nd.size ?? 84;
        // Overlays are positioned in PERCENT of the base picture, so they stay glued to the same
        // spot at any panel size — the base is the positioning context, exactly as in the DOM.
        const holder = nd.overlays?.length
          ? new Container({ height: h, flexShrink: 0, positionType: 'relative' })
          : null;
        const img = new UiImage({ height: h, borderRadius: RADIUS.sm, flexShrink: 0, src: gamesUrl(nd.url || '') });
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

      case 'item3d': {
        // A slot in the flexbox, and geometry parked on top of it. The Container is what yoga sees;
        // the geometry is plain three.js and is re-seated onto the Container every frame.
        // A SQUARE slot. Width matters as much as height: with a height only, `alignSelf:
        // 'stretch'` stretches the CROSS axis, so inside a row every slot got ~zero width and the
        // items stacked on top of each other (measured 0.034 apart at 0.327 wide). A square slot
        // reserves real space, leaves a little air around a portrait card, and is predictable.
        const slotPx = nd.size ?? 96;
        const box = new Container({ width: slotPx, height: slotPx, flexShrink: 0 });
        parent.add(box);

        const pane = this.building;
        if (pane) {
          const group = new THREE.Group();
          group.name = 'PANEL3D:ITEM';
          group.visible = false;                 // until the first sync gives it a real transform
          pane.group.add(group);
          const built = nd.item && this.makeItem ? this.makeItem(nd.item) : null;
          if (built) {
            // The item's own rotation is honoured, so a game can lie a card flat or stand it up.
            const r = nd.item?.rotation;
            if (r) group.rotation.set(
              (r.x || 0) * Math.PI / 180, (r.y || 0) * Math.PI / 180, (r.z || 0) * Math.PI / 180);
            group.add(built);
          }
          pane.slots.push({ box, group, slotPx });

          // Interaction goes through the uikit SLOT, not the geometry: the panel already owns a
          // working click path (@pmndrs/pointer-events on desktop, the userData shape on the VR
          // ray), and putting the board's InteractionManager on a panel object is precisely the
          // both-systems-fire-once trap the notes at the top of this file warn about.
          if (nd.action) {
            const action = nd.action, args = nd.args || {};
            this.makeInteractive(box, () => this.dispatch(action, args));
          }
        }
        break;
      }

      case 'model': {
        const h = nd.size ?? 84;
        const box = new UiImage({ height: h, width: h, borderRadius: RADIUS.sm, flexShrink: 0 });
        parent.add(box);
        this.thumbnail(nd.url, box);
        return;
      }

      case 'button':
        this.addButton(parent, nd.text || '', nd.style, nd.url, () => this.fire(nd, nd.args || {}),
                       nd.bg, nd.color, nd.size, nd.icon);
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
        // `selected` is the documented field for a dropdown's current value, but UiOption also
        // carries `checked` (which "checks" uses) and games set it on selects too — honour both
        // rather than silently rendering nothing as chosen.
        const chosen = this.fields[id] ?? all.find(o => o.selected || o.checked)?.value;

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
          parent.add(new Text({ text: '(model still loading)', fontSize: TYPE.small, color: INK.faint }));
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

      case 'text':
      default: {
        // A chip/pill, or plain text. Unknown node types land here and show their text rather
        // than breaking, so the server can add a node type without breaking an old client.
        if (has(nd.style, 'pill') || has(nd.style, 'chip') || nd.bg) {
          const base = col(nd.bg, fill(nd.style));
          const chip = new Container({
            backgroundColor: base,
            borderWidth: 1, borderColor: shade(base, 0.1),
            borderRadius: RADIUS.pill,
            paddingX: SPACE.md, paddingY: SPACE.xs, flexShrink: 0,
            alignItems: 'center', justifyContent: 'center',
          });
          parent.add(chip);
          chip.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? TYPE.chip, fontWeight: 'bold', color: col(nd.color, '#ffffff') }));
          return;
        }
        parent.add(new Text({ text: txt(nd.text), fontSize: nd.size ?? TYPE.body, color: col(nd.color, INK.base) }));
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
      backgroundColor: SURFACE.raised, borderWidth: 1,
      borderColor: isOpen ? '#3b82f6' : SURFACE.line,
      borderRadius: RADIUS.md, paddingX: SPACE.md, paddingY: SPACE.sm + 2,
      cursor: 'pointer',
      hover: { borderColor: isOpen ? '#3b82f6' : shade(SURFACE.line, 0.16) },
    } as any);
    box.add(head);
    const caption = new Text({
      text: txt(chosen ? chosen.label : (placeholder || 'Choose...')),
      fontSize: TYPE.label, color: chosen ? INK.base : INK.faint,
    });
    head.add(caption);
    // 'v' / '^' rather than a caret glyph: the atlas has no arrows at all (see GLYPHS).
    const caret = new Text({ text: isOpen ? '^' : 'v', fontSize: TYPE.small, color: INK.mute });
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
      // NOTE `visible` is set alongside this below — `display:'none'` removes the list from
      // LAYOUT, but three's raycaster still happily intersects it.
      // pointerEvents must follow the open state. pointerEventsOrder below makes this list beat
      // its siblings in the hit-test, and 'display: none' does NOT take an element out of
      // hit-testing — so a CLOSED list left at 'auto' silently swallowed every click in the whole
      // panel, buttons included.
      pointerEvents: isOpen ? 'auto' : 'none',
      zIndexOffset: 100, renderOrder: 1000, depthTest: false, pointerEventsOrder: 100,
      flexDirection: 'column', gap: 2, alignItems: 'stretch',
      backgroundColor: SURFACE.sunken, borderRadius: RADIUS.md, padding: SPACE.xs,
      borderWidth: 1, borderColor: SURFACE.line,
      // opacity is INHERITED, and the panel root sets 0.92 — without overriding it here the
      // options list stays see-through and the controls behind it read straight through the menu.
      opacity: 1,
    });
    list.visible = isOpen;
    box.add(list);

    // Open/closed is pure LOCAL view state, so it is driven in place. Rebuilding the panel for it
    // was what made the whole panel flicker on every open.
    const setOpen = (v: boolean) => {
      isOpen = v;
      this.open[id] = v;                // remembered, so a later server-driven rebuild agrees
      list.visible = v;                 // keeps the closed list out of every raycast (see hitTargets)
      list.setProperties({ display: v ? 'flex' : 'none', pointerEvents: v ? 'auto' : 'none' });
      head.setProperties({ borderColor: v ? '#3b82f6' : SURFACE.line });
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
        caption.setProperties({ text: txt(o.label), color: INK.base });
        choose(o.value);
      };
      const row = new Container({
        paddingX: SPACE.md, paddingY: SPACE.sm, borderRadius: RADIUS.sm,
        backgroundColor: selected ? FILL['team'] : 'transparent',
        cursor: 'pointer',
        hover: { backgroundColor: selected ? shade(FILL['team'], 0.09) : SURFACE.raised },
      } as any);
      list.add(row);
      row.add(new Text({ text: txt(o.label), fontSize: TYPE.label, color: INK.base }));
      this.makeInteractive(row, activate);
    }
  }

  /** Stable local-state key for a choice node (open/closed, remembered value). */
  private choiceId(nd: UiNode): string {
    return (nd.type || '').toLowerCase() === 'animpick'
      ? 'anim:' + (nd.id || '')
      : (nd.id || ('sel' + (nd.action || '')));
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
    // The row highlights as one thing and is padded to a comfortable hit height — a 16px checkbox
    // is a poor target for a mouse and a hopeless one for a VR laser.
    const row = new Container({
      flexDirection: 'row', alignItems: 'center', gap: SPACE.md, flexShrink: 0,
      paddingX: SPACE.sm, paddingY: SPACE.xs + 2,
      borderRadius: RADIUS.sm,
      // 'transparent' is a real uikit colour (writeColor zeroes RGBA for it) — NOT a three colour
      // name, and NOT backgroundOpacity, which uikit has no such property for and silently drops.
      backgroundColor: 'transparent',
      cursor: 'pointer',
      hover: { backgroundColor: SURFACE.raised },
    } as any);
    row.add(control);

    // The listener goes on the CONTROL and on the CAPTION — deliberately not on the row.
    //
    // pointer-events dispatches to the deepest hit object and then BUBBLES to the parents, and
    // nothing calls stopPropagation. A listener on the row therefore also fires for a click on
    // the control, which already runs its own handler — so every checkbox toggled twice, i.e.
    // "checks" groups could not be ticked at all (splice then push = no change). One listener per
    // reachable target, no overlap: the caption is a plain Text with no handler of its own.
    this.makeInteractive(control, activate, true);
    if (caption) {
      const label = new Text({ text: txt(caption), fontSize: TYPE.label, color: INK.base });
      row.add(label);
      if (activate) this.makeInteractive(label, activate);
    }
    return row;
  }

  /**
   * A button.
   *
   * Three visual weights, chosen by the style keyword so the server keeps expressing INTENT and
   * the client keeps deciding what that looks like:
   *   solid    — a FILL keyword (ok / no / primary / team / cur / win / lose). The call to action.
   *   outlined — the "ghost" keyword: transparent with a hairline. Secondary, recedes.
   *   neutral  — no keyword. The default, a muted solid.
   *
   * Plus hover and pressed states. uikit supports conditional `hover` / `active` property blocks,
   * and every state is derived from the ONE base colour via shade() — so adding a keyword to FILL
   * gets its whole state set for free, and nothing drifts out of step.
   */
  private addButton(parent: Container, rawLabel: string, style: string | undefined,
                    iconUrl: string | undefined, onClick: () => void,
                    bg?: string, color?: string, size?: number, iconName?: string) {
    const big = has(style, 'big');
    const outlined = has(style, 'ghost');
    // Bg / Color / Size were all dropped here, so a button could only be styled through the FILL
    // style keywords — the one node type that ignored the common fields every other type honours.
    // An explicit value now wins over the keyword default.
    const base = col(bg, fill(style));
    const solid = !outlined;
    const ink = col(color, outlined ? INK.base : '#ffffff');
    const fontSize = size ?? (big ? TYPE.title : TYPE.button);
    const { Icon, rest: label } = resolveIcon(iconName, rawLabel);

    const btn = new Container({
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'center',
      gap: SPACE.sm,
      // 'transparent' is handled by uikit's own colour writer (writeColor zeroes RGBA), so it is
      // safe here even though THREE.Color would parse it as white. `backgroundOpacity` is NOT a
      // uikit property — passing it looks right and does nothing, which is how the outlined
      // variant ended up rendering as a solid block.
      backgroundColor: solid ? base : 'transparent',
      // A hairline slightly lighter than the fill gives a flat rectangle an edge; on an outlined
      // button it IS the button.
      borderWidth: 1,
      borderColor: solid ? shade(base, 0.14) : SURFACE.line,
      borderRadius: RADIUS.md,
      paddingX: big ? SPACE.xl : SPACE.lg,
      paddingY: big ? SPACE.md : SPACE.sm + 2,
      flexShrink: 0,
      cursor: 'pointer',
      hover: {
        backgroundColor: solid ? shade(base, 0.09) : SURFACE.raised,
        borderColor: solid ? shade(base, 0.22) : shade(SURFACE.line, 0.16),
      },
      active: {
        // Pressed goes DARKER and loses its edge highlight — the button reads as pushed in.
        backgroundColor: solid ? shade(base, -0.07) : shade(SURFACE.raised, -0.04),
        borderColor: solid ? shade(base, 0.04) : SURFACE.line,
      },
    } as any);
    parent.add(btn);

    if (Icon) {
      const sz = fontSize * 0.95;
      btn.add(new Icon({ width: sz, height: sz, color: ink, flexShrink: 0 }));
    }
    if (iconUrl) {
      const isModel = /\.(gltf|glb|obj|stl)$/i.test(iconUrl);
      const px = fontSize * 1.35;
      const icon = new UiImage({ height: px, width: px, borderRadius: RADIUS.sm, flexShrink: 0 });
      btn.add(icon);
      if (isModel) this.thumbnail(iconUrl, icon);
      else icon.setProperties({ src: gamesUrl(iconUrl) });
    }
    if (label) btn.add(new Text({ text: txt(label), fontSize, fontWeight: 'bold', color: ink }));

    this.makeInteractive(btn, onClick);
  }

  /**
   * @param selfHandled true for a uikit-default WIDGET (Checkbox, RadioGroupItem, ...), which binds
   *   its own click handler at construction. Adding ours as well would run the action twice, so it
   *   only gets the userData (for VR) and, under 'interaction-manager', the registration that makes
   *   the dispatched 'click' reach it at all. Under 'pointer-events' nothing is needed: the widget
   *   is inside the panel subtree, so the pointer system finds it and its own handler runs.
   */
  private makeInteractive(obj: any, activate?: () => void, selfHandled = false) {
    // Another seat's published panel is scenery. Registering nothing is what enforces that — it
    // keeps the widget out of both input paths (the desktop listener and the VR ray's userData).
    if (this.building && !this.building.interactive) return;
    // VR: mg.three's controller raycast walks up parents looking for exactly this userData shape
    // (ItemData.clickActions with at least one key). `uiArgs` marks it as a UI activation rather
    // than a clicked board item.
    obj.userData['ItemData'] = { id: '', clickActions: { [this.seatId]: '__panel3d' }, attributes: {} };
    if (activate) obj.userData['uiArgs'] = { fire: activate };

    // DESKTOP: one 'click' listener, delivered by @pmndrs/pointer-events. The panel is
    // deliberately NOT registered with the InteractionManager — that is what keeps the two
    // systems from both firing the same action.
    if (activate && !selfHandled) {
      obj.userData['__onClick'] = activate;
      obj.addEventListener('click', activate);
    }
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

  /**
   * Every object currently carrying a panel action, for the VR controller ray.
   *
   * It cannot find them by walking the scene: uikit's Component.raycast always returns FALSE,
   * and three treats that as "do not descend", so an ordinary recursive raycast stops at a
   * pane's root Container and never sees a single widget. uikit's own input path gets around
   * this by consulting root.interactableDescendants explicitly — this is the equivalent, using
   * the list makeInteractive already keeps.
   */
  hitTargets(): any[] {
    const out: any[] = [];
    for (const p of this.panes) {
      for (const c of p.clickables) if (visibleInTree(c)) out.push(c);
    }
    return out;
  }
}
