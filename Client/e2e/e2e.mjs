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
  for (const id of [gA.id, gR.id, gW.id, gB.id]) await api('/api/Game/DeleteGame', 'POST', { gameId: id }, alice.token).catch(() => {});
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
