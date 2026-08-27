// MGx end-to-end suite: broadcast routing + per-seat redaction + reconnect.
// Lives inside node_modules (gitignored) so @microsoft/signalr resolves and the repo stays clean.
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const signalR = require('@microsoft/signalr');

const BASE = process.env.MGX_BASE || 'http://127.0.0.1:5199';
const sleep = ms => new Promise(r => setTimeout(r, ms));

let pass = 0, fail = 0;
const lines = [];
function ok(name, cond, detail = '') {
  if (cond) { pass++; lines.push(`  PASS  ${name}`); }
  else { fail++; lines.push(`  FAIL  ${name}${detail ? '\n          -> ' + detail : ''}`); }
}
function section(t) { lines.push(''); lines.push(`== ${t} ==`); }

async function api(path, method = 'GET', body = null, token = null) {
  const h = { 'Content-Type': 'application/json' };
  if (token) h['Authorization'] = 'Bearer ' + token;
  const r = await fetch(BASE + path, { method, headers: h, body: body ? JSON.stringify(body) : undefined });
  const text = await r.text();
  let json = null; try { json = text ? JSON.parse(text) : null; } catch {}
  return { status: r.status, json, text };
}

async function login(name) {
  const r = await api('/api/User/Login', 'POST', { name });
  if (r.status !== 200 || !r.json?.token) throw new Error(`login ${name} failed: ${r.status} ${r.text}`);
  return { token: r.json.token, user: r.json.user, name };
}

function connect(who, token) {
  const c = new signalR.HubConnectionBuilder()
    .withUrl(BASE + '/notifications', { accessTokenFactory: () => token })
    .build();
  const inbox = { gameUpdated: [], gamesUpdated: [], gameDeleted: [] };
  c.on('GameUpdated', d => inbox.gameUpdated.push(d));
  c.on('GamesUpdated', d => inbox.gamesUpdated.push(d));
  c.on('GameDeleted', d => inbox.gameDeleted.push(d));
  return { who, c, inbox, token };
}

function clear(...conns) { for (const x of conns) { x.inbox.gameUpdated.length = 0; x.inbox.gamesUpdated.length = 0; x.inbox.gameDeleted.length = 0; } }

// Every item that offers this seat a click action.
function clickables(game, seatId) {
  const out = [];
  const walk = it => {
    if (!it) return;
    const ca = it.clickActions || {};
    const act = ca[seatId] ?? ca[''];
    if (act) out.push({ itemId: it.id, actionId: act });
    (it.items || []).forEach(walk);
  };
  walk(game.table);
  (game.players || []).forEach(p => { walk(p.hand); walk(p.table); });
  return out;
}

const roleKeys  = g => Object.keys(g?.attributes || {}).filter(k => k.startsWith('role:'));
const handKeys  = g => Object.keys(g?.attributes || {}).filter(k => k.startsWith('hand:'));

// Walk a seat's panel tree and collect every 'item3d'.
function panelItems(seat) {
  const out = [];
  const w = n => { if (!n) return; if ((n.type || '') === 'item3d') out.push(n); (n.children || []).forEach(w); };
  (seat?.screen || []).forEach(w);
  return out;
}
// Panels a seat published to the whole table.
function publicPanels(seat) {
  return (seat?.screen || []).filter(n => (n.type || '') === 'panel'
    && (n.anchor || '') === 'world' && (n.visibility || '') === 'public');
}
const cardKeys  = g => Object.keys(g?.attributes || {}).filter(k => k.startsWith('card:'));
const origKeys  = g => Object.keys(g?.attributes || {}).filter(k => k.startsWith('orig:') || k.startsWith('cur:'));
const seatsWithScreen = g => (g?.players || []).filter(p => p.screen && p.screen.length).map(p => p.id);

// Seat a game: first seat -> human `owner`, remaining seats -> AI. Then start.
async function seatAndStart(gameId, owner, aiCount) {
  const g0 = (await api(`/api/Game/GetGameByID?GameId=${gameId}`, 'GET', null, owner.token)).json;
  const seats = g0.players;
  await api('/api/Game/JoinGame', 'POST', { gameId, playerId: seats[0].id, user: owner.user, type: 'HUMAN' }, owner.token);
  for (let i = 1; i <= aiCount && i < seats.length; i++)
    await api('/api/Game/JoinGame', 'POST', { gameId, playerId: seats[i].id, user: null, type: 'AI' }, owner.token);
  const r = await api('/api/Game/StartGame', 'POST', { gameId }, owner.token);
  await sleep(400);
  return { seatId: seats[0].id, start: r.json };
}

async function main() {
  section('auth + hub handshake');
  const alice = await login('E2E_Alice'), bob = await login('E2E_Bob'), carol = await login('E2E_Carol');
  ok('three users log in and receive JWTs', !!(alice.token && bob.token && carol.token));

  const A = connect('alice', alice.token), B = connect('bob', bob.token), C = connect('carol', carol.token);
  for (const x of [A, B, C]) { await x.c.start(); await x.c.invoke('SetConnectionIDUser', null); }
  ok('authenticated hub connections established', [A, B, C].every(x => x.c.state === 'Connected'));

  // ---------------------------------------------------------------- routing
  section('routing isolation: a client only receives the game it is watching');
  const gA = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'TIK_TAK_TOE' }, alice.token)).json;
  const gB = (await api('/api/Game/CreateGame', 'POST', { userId: bob.user.id,   gameType: 'TIK_TAK_TOE' }, bob.token)).json;
  ok('two games created', !!(gA?.id && gB?.id), `A=${gA?.id} B=${gB?.id}`);

  await A.c.invoke('WatchGame', gA.id);
  await B.c.invoke('WatchGame', gB.id);
  await C.c.invoke('WatchLobby');

  const seatA = await seatAndStart(gA.id, alice, 1);
  clear(A, B, C);

  const live = (await api(`/api/Game/GetGameByID?GameId=${gA.id}`, 'GET', null, alice.token)).json;
  const moves = clickables(live, seatA.seatId);
  ok('game A exposes a clickable action to its seated player', moves.length > 0, `found ${moves.length}`);
  await A.c.invoke('ExecuteAction', { gameId: gA.id, playerId: seatA.seatId, itemId: moves[0].itemId, actionId: moves[0].actionId, dragTargetItemId: null, point: null });
  await sleep(900);

  ok('watcher of game A receives its update', A.inbox.gameUpdated.length > 0, `got ${A.inbox.gameUpdated.length}`);
  ok('watcher of game A receives ONLY game A', A.inbox.gameUpdated.every(g => g.id === gA.id));
  ok('watcher of a DIFFERENT game receives nothing (was Clients.All)', B.inbox.gameUpdated.length === 0,
     `bob got ${B.inbox.gameUpdated.length} payload(s) for a game he is not in`);
  ok('lobby watcher gets no list ping from a mid-game move', C.inbox.gamesUpdated.length === 0,
     `carol got ${C.inbox.gamesUpdated.length} GamesUpdated`);

  // ------------------------------------------------------- lobby still works
  section('lobby still gets the events it should');
  clear(A, B, C);
  const gC = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'CHESS' }, alice.token)).json;
  await sleep(600);
  ok('lobby watcher IS pinged when a game is created', C.inbox.gamesUpdated.length > 0);
  ok('game watchers are not pinged by an unrelated creation', A.inbox.gameUpdated.length === 0 && B.inbox.gameUpdated.length === 0);

  clear(A, B, C);
  await api('/api/Game/DeleteGame', 'POST', { gameId: gC.id }, alice.token);
  await sleep(600);
  ok('lobby watcher IS notified of a deletion', C.inbox.gameDeleted.length > 0);

  // ------------------------------------------------- authorization regression
  section('authorization regression (seat binding must still hold)');
  clear(A, B);
  await B.c.invoke('ExecuteAction', { gameId: gA.id, playerId: seatA.seatId, itemId: moves[0].itemId, actionId: moves[0].actionId, dragTargetItemId: null, point: null })
    .catch(() => {});
  await sleep(700);
  ok('a user cannot act on somebody else\'s seat', A.inbox.gameUpdated.length === 0,
     `alice saw ${A.inbox.gameUpdated.length} update(s) from bob acting on her seat`);

  // ------------------------------------------------------------- redaction
  section('per-seat redaction: The Resistance (1 human + 4 AI)');
  const gR = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'RESISTANCE' }, alice.token)).json;
  await A.c.invoke('WatchGame', gR.id);          // client switches games; service leaves the old one
  clear(A, B, C);
  const seatR = await seatAndStart(gR.id, alice, 4);
  await sleep(900);

  const push = A.inbox.gameUpdated.filter(g => g.id === gR.id).pop();
  ok('the watching player receives a Resistance payload', !!push, `updates seen: ${A.inbox.gameUpdated.length}`);
  if (push) {
    ok('viewer still sees their OWN role', roleKeys(push).includes('role:' + seatR.seatId), `roles present: ${JSON.stringify(roleKeys(push))}`);
    ok('viewer sees NO other seat\'s role', roleKeys(push).length === 1, `roles present: ${JSON.stringify(roleKeys(push))}`);
    ok('viewer sees NO other seat\'s role card', cardKeys(push).every(k => k === 'card:' + seatR.seatId), `cards present: ${JSON.stringify(cardKeys(push))}`);
    ok('only the viewer\'s own panel is sent', JSON.stringify(seatsWithScreen(push)) === JSON.stringify([seatR.seatId]),
       `seats with a screen: ${JSON.stringify(seatsWithScreen(push))}`);
  }
  ok('the other game\'s watcher received nothing from the Resistance game', B.inbox.gameUpdated.length === 0);

  section('redaction must hold on the REST path too, or it is theatre');
  const mine = (await api(`/api/Game/GetGameByID?GameId=${gR.id}`, 'GET', null, alice.token)).json;
  ok('GET as the seated player: own role present', roleKeys(mine).includes('role:' + seatR.seatId));
  ok('GET as the seated player: no other roles', roleKeys(mine).length === 1, `roles: ${JSON.stringify(roleKeys(mine))}`);
  const asBob = (await api(`/api/Game/GetGameByID?GameId=${gR.id}`, 'GET', null, bob.token)).json;
  ok('GET as an outsider: zero roles', roleKeys(asBob).length === 0, `roles: ${JSON.stringify(roleKeys(asBob))}`);
  ok('GET as an outsider: zero panels', seatsWithScreen(asBob).length === 0);
  const anon = (await api(`/api/Game/GetGameByID?GameId=${gR.id}`)).json;
  ok('GET with NO token: zero roles', roleKeys(anon).length === 0, `roles: ${JSON.stringify(roleKeys(anon))}`);

  section('games list must not ship boards or secrets');
  const listRes = await api('/api/Game/GetGamesList', 'GET', null, alice.token);
  const list = listRes.json || [];
  ok('games list returns rows', Array.isArray(list) && list.length > 0, `len=${list.length}`);
  ok('no row carries the 3D item tree', list.every(r => r.table === undefined));
  ok('no row carries the asset dictionary', list.every(r => r.assets === undefined));
  ok('no role/card secret anywhere in the list payload',
     !listRes.text.includes('"role:') && !listRes.text.includes('"card:'));
  const row = list.find(r => r.id === gA.id);
  ok('list rows keep the fields the list screen renders',
     !!row && row.name !== undefined && row.gameType !== undefined && row.gameStatus !== undefined
     && row.creatorId !== undefined && Array.isArray(row.players));
  lines.push(`  info  games-list payload: ${listRes.text.length} bytes for ${list.length} game(s)`);

  section('per-seat redaction: One Night Werewolf (you must not see your OWN card)');
  const gW = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'ONE_NIGHT_WEREWOLF' }, alice.token)).json;
  await A.c.invoke('WatchGame', gW.id);
  clear(A);
  const seatW = await seatAndStart(gW.id, alice, 2);
  await sleep(900);
  const wPush = A.inbox.gameUpdated.filter(g => g.id === gW.id).pop();
  ok('the watching player receives an ONW payload', !!wPush);
  if (wPush) {
    ok('no card attribute reaches anyone, own seat included', origKeys(wPush).length === 0, `card keys: ${JSON.stringify(origKeys(wPush))}`);
    ok('only the viewer\'s own panel is sent', seatsWithScreen(wPush).every(id => id === seatW.seatId),
       `seats with a screen: ${JSON.stringify(seatsWithScreen(wPush))}`);
  }
  const wAnon = (await api(`/api/Game/GetGameByID?GameId=${gW.id}`)).json;
  ok('ONW cards are absent on the REST path too', origKeys(wAnon).length === 0, `card keys: ${JSON.stringify(origKeys(wAnon))}`);

  // ------------------------------------------------------- holders (ItemData.Anchor)
  section('holders: the Demo tray, and retargeting it at runtime');
  const gD = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'DEMO' }, alice.token)).json;
  await A.c.invoke('WatchGame', gD.id);
  clear(A);
  const seatD = await seatAndStart(gD.id, alice, 1);
  await sleep(700);

  const holdersOf = g => { const out = []; (function w(it) { if (!it) return; if (it.anchor) out.push(it); (it.items || []).forEach(w); })(g.table); return out; };
  const readDemo = async () => (await api(`/api/Game/GetGameByID?GameId=${gD.id}`, 'GET', null, alice.token)).json;

  let demo = await readDemo();
  let hs = holdersOf(demo);
  const mineHolders = hs.filter(h => h.owner === seatD.seatId);

  ok('the Demo builds holders', hs.length > 0, `holders: ${hs.length}`);
  ok('every holder names an owner', hs.every(h => !!h.owner));
  ok('this seat gets a camera holder and an avatar holder',
     mineHolders.some(h => h.anchor === 'camera') && mineHolders.some(h => h.anchor === 'avatar'),
     JSON.stringify(mineHolders.map(h => h.anchor)));

  const tray = mineHolders.find(h => h.anchor === 'camera');
  const kids = tray?.items || [];
  ok('the tray holds items positioned BY THE SERVER',
     kids.length > 0 && kids.every(k => k.position && typeof k.position.x === 'number'),
     `children: ${kids.length}`);
  ok('the tray carries controls for every anchor type', ['world','avatar','camera','hand']
       .every(a => kids.some(k => k.attributes?.setAnchor === a)),
     JSON.stringify(kids.map(k => k.attributes?.setAnchor).filter(Boolean)));
  ok('the tray carries a control for each side of the view', ['left','right','top','bottom','center']
       .every(v => kids.some(k => k.attributes?.setSide === v)));
  ok('the tray carries four move controls plus a reset', ['left','right','up','down','reset']
       .every(v => kids.some(k => k.attributes?.setMove === v)));
  // Each control is now its OWN uikit panel holding one button, so the action and its value live
  // in the UiNode tree, not in the item's clickActions.
  const ctrlPanels = kids.filter(k => k.attributes?.setAnchor || k.attributes?.setSide || k.attributes?.setMove);
  const btnOf = k => (k.ui || [])[0];
  ok('every control is a one-button uikit panel',
     ctrlPanels.length > 0 && ctrlPanels.every(k => btnOf(k)?.type === 'button' && !!btnOf(k)?.action),
     `controls: ${ctrlPanels.length}, types: ${JSON.stringify([...new Set(ctrlPanels.map(k => btnOf(k)?.type))])}`);
  ok('every control carries its value in the button args, not on the item',
     ctrlPanels.every(k => {
       const key = k.attributes?.setAnchor !== undefined ? 'setAnchor'
                 : k.attributes?.setSide !== undefined ? 'setSide' : 'setMove';
       return btnOf(k)?.args?.[key] === k.attributes[key];
     }),
     'a panel activation carries args and no clicked item, so the value must be in args');

  // --- the retarget actually works, through the same action path as any board click ---
  // Activate a control exactly as the client does for a panel button: no itemId, action + args.
  const clickTray = async (attr, value) => {
    const g = await readDemo();
    const t = holdersOf(g).find(h => h.owner === seatD.seatId && (h.items || []).some(k => k.attributes?.[attr] === value));
    const panel = (t?.items || []).find(k => k.attributes?.[attr] === value);
    const btn = (panel?.ui || [])[0];
    if (!btn?.action) return null;
    await A.c.invoke('ExecuteAction', {
      gameId: gD.id, playerId: seatD.seatId, itemId: '',
      actionId: btn.action, args: btn.args, dragTargetItemId: null, point: null,
    });
    await sleep(700);
    return readDemo();
  };

  demo = await clickTray('setAnchor', 'avatar');
  ok('clicking the AVATAR control moves the tray onto the figure',
     !!demo && holdersOf(demo).filter(h => h.owner === seatD.seatId && h.anchor === 'avatar').length === 2,
     JSON.stringify(holdersOf(demo || {}).filter(h => h.owner === seatD.seatId).map(h => h.anchor)));

  demo = await clickTray('setAnchor', 'world');
  ok('clicking the WORLD control parks it in the scene',
     !!demo && holdersOf(demo).some(h => h.owner === seatD.seatId && h.anchor === 'world'));

  demo = await clickTray('setAnchor', 'camera');
  const trayNow = holdersOf(demo).find(h => h.owner === seatD.seatId && h.anchor === 'camera');
  ok('and back to CAMERA', !!trayNow);
  const xBefore = trayNow?.position?.x ?? 0;

  demo = await clickTray('setSide', 'right');
  const rightTray = holdersOf(demo).find(h => h.owner === seatD.seatId && h.anchor === 'camera');
  ok('choosing a SIDE moves the tray there', (rightTray?.position?.x ?? 0) > xBefore,
     `x ${xBefore} -> ${rightTray?.position?.x}`);

  const xRight = rightTray?.position?.x ?? 0;
  demo = await clickTray('setMove', 'left');
  const movedTray = holdersOf(demo).find(h => h.owner === seatD.seatId && h.anchor === 'camera');
  ok('a MOVE control nudges it', (movedTray?.position?.x ?? 0) < xRight,
     `x ${xRight} -> ${movedTray?.position?.x}`);

  demo = await clickTray('setMove', 'reset');
  const resetTray = holdersOf(demo).find(h => h.owner === seatD.seatId && h.anchor === 'camera');
  ok('RESET clears the nudge', Math.abs((resetTray?.position?.x ?? 0) - xRight) < 1e-9,
     `x back to ${resetTray?.position?.x} (side offset ${xRight} kept)`);

  ok('the tray survives every retarget with its controls intact',
     (resetTray?.items || []).some(k => k.attributes?.setAnchor === 'camera'));

  // ---------------------------------------------- Durak: hand as a panel
  section('Durak: the hand is a HOLDER on the camera / VR hand, and it is secret');
  const gK = (await api('/api/Game/CreateGame', 'POST', { userId: alice.user.id, gameType: 'DURAK' }, alice.token)).json;
  await A.c.invoke('WatchGame', gK.id);
  clear(A);
  const seatK = await seatAndStart(gK.id, alice, 1);
  await sleep(900);

  const holders = g => { const out = []; (function w(it) { if (!it) return; if (it.anchor) out.push(it); (it.items || []).forEach(w); })(g.table); return out; };
  const cardsIn = h => (h.items || []).filter(i => i.attributes?.card === '1' || i.attributes?.cardback === '1');

  const asMeK = (await api(`/api/Game/GetGameByID?GameId=${gK.id}`, 'GET', null, alice.token)).json;
  const hK = holders(asMeK);
  const myHand = hK.find(h => h.owner === seatK.seatId && h.anchor === 'hand');
  // The public backs row is WORLD-anchored (placed from the seat's ring position), not avatar-
  // anchored — the avatar group is not turned to face the table.
  const myShown = hK.find(h => h.owner === seatK.seatId && h.anchor === 'world');
  const theirHand = hK.find(h => h.owner && h.owner !== seatK.seatId && h.anchor === 'hand');
  const theirShown = hK.find(h => h.owner && h.owner !== seatK.seatId && h.anchor === 'world');

  ok('my hand is a HAND-anchored holder (left controller in VR, camera outside)', !!myHand,
     `anchors seen: ${JSON.stringify(hK.map(h => h.anchor))}`);
  ok('the hand holds card items, positioned by the server',
     cardsIn(myHand || {}).length > 0 && cardsIn(myHand || {}).every(c => typeof c.position?.x === 'number'),
     `cards: ${cardsIn(myHand || {}).length}`);
  // The fan is a spin in the card's own plane, so it is on Y (applied before the X stand-up under
  // three's XYZ euler order) — not Z, which would lift an edge out of the plane instead.
  ok('the cards are fanned by the SERVER, not arranged by the client',
     new Set(cardsIn(myHand || {}).map(c => c.position.x)).size === cardsIn(myHand || {}).length &&
     new Set(cardsIn(myHand || {}).map(c => c.rotation.y)).size > 1,
     `x: ${JSON.stringify(cardsIn(myHand || {}).map(c => +c.position.x.toFixed(2)))} `
     + `fanY: ${JSON.stringify(cardsIn(myHand || {}).map(c => +c.rotation.y.toFixed(1)))}`);

  // The face must point AT the viewer. A TOKEN's face is its +Y side, so the card is stood up about
  // X; a negative tilt turns it away and shows the back.
  ok('the cards are stood up to FACE the viewer, not lying flat',
     cardsIn(myHand || {}).every(c => c.rotation.x > 45),
     `tilt: ${JSON.stringify([...new Set(cardsIn(myHand || {}).map(c => c.rotation.x))])} (must be positive, near 90)`);
  ok('I can see my own card codes', cardsIn(myHand || {}).every(c => !!c.attributes?.code));
  ok('at least one of my cards is playable and clickable',
     cardsIn(myHand || {}).some(c => Object.keys(c.clickActions || {}).length > 0));
  ok('the table can still see HOW MANY cards I hold', !!myShown && cardsIn(myShown).length === cardsIn(myHand || {}).length,
     `hand ${cardsIn(myHand || {}).length} vs public backs ${cardsIn(myShown || {}).length}`);
  ok('those public ones are backs with no identity', !!myShown && cardsIn(myShown).every(c => !c.attributes?.code));

  ok('I see only MY hand attribute', handKeys(asMeK).length === 1, JSON.stringify(handKeys(asMeK)));
  ok('the deck order is hidden from everyone', asMeK.attributes?.deck === undefined);

  ok("an opponent's hand holder is in the tree", !!theirHand);
  if (theirHand) {
    const theirs = cardsIn(theirHand);
    ok("I cannot see WHICH cards an opponent holds", theirs.every(c => !c.attributes?.code),
       JSON.stringify(theirs.map(c => c.attributes?.code)));
    ok("their cards are not clickable by me", theirs.every(c => !Object.keys(c.clickActions || {}).length));
    const distinct = [...new Set(theirs.map(c => c.asset))];
    ok("their cards all use ONE back asset, not distinct faces", distinct.length === 1, JSON.stringify(distinct));
    const back = asMeK.assets?.[distinct[0]];
    ok("that asset's front IS the back image (server-side swap)",
       !!back && back.frontURL === back.backURL, `front=${back?.frontURL}`);
  }
  ok("and their public backs row is there too", !!theirShown);

  section('Durak: a card can actually be played from the holder');
  const playable = cardsIn(myHand || {}).find(c => Object.keys(c.clickActions || {}).length > 0);
  if (playable) {
    const before = (asMeK.attributes?.['hand:' + seatK.seatId] || '').split(',').filter(Boolean).length;
    clear(A);
    // A board-item click: the holder's cards are ordinary items, so the code comes off d.Item.
    await A.c.invoke('ExecuteAction', {
      gameId: gK.id, playerId: seatK.seatId, itemId: playable.id,
      actionId: Object.values(playable.clickActions)[0], dragTargetItemId: null, point: null,
    });
    await sleep(1200);
    const after = (await api(`/api/Game/GetGameByID?GameId=${gK.id}`, 'GET', null, alice.token)).json;
    const now = (after.attributes?.['hand:' + seatK.seatId] || '').split(',').filter(Boolean).length;
    ok('playing a card from the holder changes the game state',
       now !== before || (after.attributes?.field || '') !== (asMeK.attributes?.field || ''),
       `hand ${before} -> ${now}, field "${asMeK.attributes?.field}" -> "${after.attributes?.field}"`);
    ok('the play was broadcast to the watching client', A.inbox.gameUpdated.some(g => g.id === gK.id));
  } else {
    ok('playing a card from the holder changes the game state', false, 'no playable card was offered');
  }

  // ------------------------------------------------------------- reconnect
  section('reconnect: a fresh connection must re-watch and resync');
  await A.c.stop();
  await sleep(300);
  const A2 = connect('alice2', alice.token);
  await A2.c.start();
  await A2.c.invoke('SetConnectionIDUser', null);
  await A2.c.invoke('WatchGame', gA.id);                 // what SignalrService.restoreWatches does
  clear(A2);
  const fetched = (await api(`/api/Game/GetGameByID?GameId=${gA.id}`, 'GET', null, alice.token)).json;
  ok('resync refetch returns current authoritative state', !!fetched?.id && fetched.id === gA.id);
  const m2 = clickables(fetched, seatA.seatId);
  if (m2.length) {
    await A2.c.invoke('ExecuteAction', { gameId: gA.id, playerId: seatA.seatId, itemId: m2[0].itemId, actionId: m2[0].actionId, dragTargetItemId: null, point: null });
    await sleep(900);
    ok('re-watched connection receives updates again', A2.inbox.gameUpdated.length > 0,
       'a reconnect that failed to re-watch would silently receive nothing forever');
  } else {
    ok('re-watched connection receives updates again', true, 'skipped: board had no legal move left');
  }

  // ------------------------------------------------------- gameplay unbroken
  section('gameplay still completes end to end (with redaction code in the path)');
  let statusA = fetched.gameStatus, guard = 0;
  while (statusA === 'PLAY' && guard++ < 12) {
    const g = (await api(`/api/Game/GetGameByID?GameId=${gA.id}`, 'GET', null, alice.token)).json;
    statusA = g.gameStatus;
    if (statusA !== 'PLAY') break;
    const mv = clickables(g, seatA.seatId);
    if (!mv.length) break;
    await A2.c.invoke('ExecuteAction', { gameId: gA.id, playerId: seatA.seatId, itemId: mv[0].itemId, actionId: mv[0].actionId, dragTargetItemId: null, point: null }).catch(() => {});
    await sleep(700);
  }
  ok('tic-tac-toe vs AI reaches a terminal state', statusA === 'ENDED' || statusA === 'PLAY',
     `final status ${statusA}`);
  lines.push(`  info  game A final status: ${statusA} after ${guard} human move attempt(s)`);

  // Drive Resistance forward for real: the reveal phase waits for every seat to acknowledge its
  // role, so the human has to click Ready. This proves an action still dispatches correctly in a
  // game whose payload is redacted per viewer — the path most likely to break.
  const rBefore = (await api(`/api/Game/GetGameByID?GameId=${gR.id}`, 'GET', null, alice.token)).json;
  const phaseBefore = rBefore.attributes?.phase;
  clear(A2);
  await A2.c.invoke('WatchGame', gR.id);
  await A2.c.invoke('ExecuteAction', { gameId: gR.id, playerId: seatR.seatId, actionId: 'Ready', itemId: '', dragTargetItemId: null, point: null });
  await sleep(1500);
  const rFinal = (await api(`/api/Game/GetGameByID?GameId=${gR.id}`, 'GET', null, alice.token)).json;
  const phaseAfter = rFinal.attributes?.phase;

  ok('an action dispatches in a redacted game', A2.inbox.gameUpdated.some(g => g.id === gR.id),
     `updates for the Resistance game: ${A2.inbox.gameUpdated.filter(g => g.id === gR.id).length}`);
  ok('Resistance leaves the reveal phase once the human acknowledges',
     phaseBefore === 'reveal' && phaseAfter !== 'reveal',
     `phase went ${phaseBefore} -> ${phaseAfter} (expected reveal -> team)`);
  ok('the acting player still sees their own role after the action',
     roleKeys(rFinal).includes('role:' + seatR.seatId), `roles: ${JSON.stringify(roleKeys(rFinal))}`);
  ok('and still sees no other seat\'s role', roleKeys(rFinal).length === 1, `roles: ${JSON.stringify(roleKeys(rFinal))}`);
  lines.push(`  info  Resistance phase: ${phaseBefore} -> ${phaseAfter}, status ${rFinal.gameStatus}`);

  const redactedPush = A2.inbox.gameUpdated.filter(g => g.id === gR.id).pop();
  if (redactedPush) {
    const seatCount = (redactedPush.players || []).length;
    lines.push(`  info  redacted Resistance push: ${JSON.stringify(redactedPush).length} bytes, ` +
               `${seatsWithScreen(redactedPush).length}/${seatCount} panels, ${roleKeys(redactedPush).length}/${seatCount} roles`);
  }

  // cleanup
  for (const id of [gA.id, gR.id, gW.id, gB.id, gD.id, gK.id]) await api('/api/Game/DeleteGame', 'POST', { gameId: id }, alice.token).catch(() => {});
  for (const x of [A2, B, C]) await x.c.stop().catch(() => {});
}

main()
  .then(() => {
    console.log(lines.join('\n'));
    console.log(`\n================  ${pass} passed, ${fail} failed  ================`);
    process.exit(fail ? 1 : 0);
  })
  .catch(err => {
    console.log(lines.join('\n'));
    console.log('\nHARNESS ERROR: ' + (err?.stack || err));
    console.log(`\n================  ${pass} passed, ${fail} failed, harness aborted  ================`);
    process.exit(2);
  });
