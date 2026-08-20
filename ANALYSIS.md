# MultiGameX (MGx) — Project Analysis

*Scan date: 2026-08-19. Covers the full server (`/`, ~12.5k LOC C#) and client (`Client/`, ~4.8k LOC TS). Complements the existing `SCAN_REPORT.md` / `SECURITY_FIXES.md` / `UPGRADE_NOTES.md`, which document the .NET 7→10 + Angular 16→22 upgrade and the C1–C5 hardening pass.*

---

## 1. What it is

A **server-authoritative 3D multiplayer board-game platform**. One ASP.NET Core host serves the API, the SignalR hub, the built Angular SPA (`wwwroot/`) and all game art (`GameContent/`). The Angular client is a *dumb renderer*: the server ships a 3D item tree + a per-seat 2D UI tree, the client draws them and posts actions back.

**14 games ship today:** Tic-Tac-Toe, Chess, Gomoku, Reversi, Checkers, Durak, Splendor, Carcassonne, Catan, The Resistance, One Night Werewolf, Small World, D&D (GM sandbox), Demo (reference).

### Stack
| Layer | Tech |
|---|---|
| Server | .NET 10 (`net10.0`), ASP.NET Core, SignalR, EF Core 10 + SQLite, JWT bearer, Swashbuckle 10.2.3 |
| Client | Angular 22 (NgModule, zone.js), TypeScript 6, three.js 0.185, `@pmndrs/uikit` (3D panels), PrimeNG 22, `@microsoft/signalr` 10 |
| Persistence | **Two JSON blobs** in one SQLite table (`Store`: keys `users`, `games`) — no relational mapping |
| Deploy | Dockerfile → DigitalOcean App Platform (ephemeral FS, so the SQLite file is wiped on each deploy) |

---

## 2. Architecture map

### 2.1 Request paths

```
Angular
  ├─ REST  /api/User/Login              → UserBL   → JWT
  ├─ REST  /api/Game/*                  → GameBL   → BaseGameFlow.Run*Flow()
  └─ WS    /notifications  ExecuteAction → NotificationHub → GameBL → BaseGameFlow.ExecuteAction()
                                                                          ↓
                                          DataRepository.HubGameUpdated  → Clients.All "GameUpdated"
```

Everything real happens through `ExecuteAction` over SignalR. The REST `ExecuteAction` endpoint is commented out in `GameController.cs:114`; the two `dal.service.ts` methods that call it are dead.

### 2.2 Data model (`Entities/`)

```
BaseData<T>            Id, Name, Attributes: Dictionary<string,string>   ← the universal escape hatch
 ├─ GameData           GameType, GameStatus, Assets, Table:ItemData, Players, CurrentTurnId,
 │                     Winners, MinPlayers, Observer,  [JsonIgnore] GameFlow
 ├─ PlayerData         Type (EMPTY_SEAT|HUMAN|AI|OBSERVER), User, Table, Hand,
 │                     Avatar, Camera, Screen: List<UiNode>,  [JsonIgnore] AIAgent
 ├─ ItemData           Asset, Position/Rotation/Scale (V3), Visible, ClickActions, HoverActions,
 │                     Text, AnimationIdx, Items (children), ParentItemId
 ├─ AssetData          polymorphic: TOKEN | OBJECT | SOUND | TEXT3D | TEXTBLOCK | CYLINDER | ARROW | DIE
 └─ LocationData / UserData
UiNode (not BaseData)  the server-driven 2D panel tree
```

**Everything game-specific is a string in `Attributes`.** Turn state, roles, board encodings, hidden info — all CSV-in-a-dictionary. That is what makes adding a game a single-file exercise, and it is also the single biggest source of the runtime `FormatException`/`KeyNotFoundException` risk listed in §5.

### 2.3 Game lifecycle (`BaseGameFlow.cs`, 627 lines)

| Phase | Entry | Abstract hook |
|---|---|---|
| Create | `CreateGame(type,userId)` → `RunCreateFlow()` :145 | `Create()` — seats, cameras, assets, static flags |
| Setup | `RunSetupFlow()` :154 (wipes Attributes except `allowVoice, voiceSpectators, showHeads, cardBack, noAvatars, usesCardBack`) | `Setup()` |
| Start | `RunStartFlow()` :189 — gated on `CanStart`, sets the default seat anchors, spawns one `AIAgent` per AI seat | `StartGame()` |
| Act | `ExecuteAction()` :261 → `_turnLock` → `DispatchAction()` :314 → `AfterAction()` :347 | `[GameAction]` methods |
| End | `AfterAction` → `IsEndGame()` → `RunEndGameFlow()` :228 | `IsEndGame()`, `GetGameWinners()`, `EndGame()` |
| Undo | `UndoLastMove()` :280 — pops `_undo` past AI turns | `AfterUndo()` |

`AIAgent` = one `System.Timers.Timer` @800 ms per AI seat, ticking `IsAITurn()` → `PlayAI()`.

`BoardGameFlow` (38 lines) is the only intermediate base — used by Chess, Checkers, Gomoku, Reversi; supplies turn-by-colour, `IsEndGame`, `GetGameWinners`.

### 2.4 Client rendering

- `MgThree` (867 L) — three.js engine: scene, WebGLRenderer + EffectComposer/OutlinePass, PMREM lighting, OrbitControls, `InteractionManager` raycasting, **WebXR (immersive-vr, hand tracking, controller rays, desktop→VR view alignment)**, a magnifier loupe, GLB/STL/OBJ/texture loaders.
- `MgGame` (1373 L) — `GameData` → scene graph, with a mark-and-sweep diff on each update, tween-glided positions, avatar heads, animation mixers, tint/emissive/outline from item attributes.
- `MgPanel3d` (1108 L) — **the real UiNode renderer**, drawn as `@pmndrs/uikit` geometry inside the 3D scene so panels exist in VR too.

> ⚠️ `CLAUDE.md:24` is stale: it says the panel renderer is `renderNode` in `game-play.component.ts`. There is no `renderNode` and no HTML panel any more — it moved to `MgPanel3d.build()` (`mg.panel3d.ts:629`). Worth fixing before it misleads the next change.

---

## 3. Extension points — how to add things

### 3.1 A new game (server-only, 4 files)

1. `Entities/GameData.cs` → add a `GameTypeEnum` const.
2. `GameFlows/BaseGameFlow.cs` → **three** edits: `GameCatalog()` :32, `PrettyName()` :50, `CreateGame()` :69.
3. `Database/DataRepository.cs` → `AttachGameFlow()` :112 (else persisted games reload with a null flow and NRE on first action).
4. `GameFlows/<Name>GameFlow.cs`:

```csharp
public MyGameFlow(GameData gameData) : base(gameData) { }

protected override Task Create();                 // seats, Observer, assets, static flags
protected override Task Setup();                  // usually => Task.CompletedTask
protected override Task StartGame();              // build the scene + first turn
protected override Task EndGame();
protected override Task<bool> IsEndGame();
protected override List<PlayerData> GetGameWinners();

// expected in practice
public    override int  MinPlayers => 2;
protected override void RefreshScreens();
protected override PlayerData? CurrentTurnPlayer();
protected override void AfterUndo();              // REQUIRED if state lives in player.Hand/Table
public    override bool IsAITurn(PlayerData p);
public    override async Task<bool> PlayAI(PlayerData p, Random rnd);

[GameAction] public async Task MyMove(ExecuteActionData data) { ... }
```

Assets: drop files in `GameContent/games/<name>/`, emit **relative** paths. No client rebuild.

> The step-by-step comment at the top of `DemoGameFlow.cs:17,30-35` is **out of date** — it still tells you to edit `games-list.component.html` and to build the control panel as client HTML. Both are wrong now.

### 3.2 A new UiNode type
Add the factory in `Entities/UiNode.cs`, then a `case` in the `MgPanel3d.build()` switch (`mg.panel3d.ts:632`). Unknown types degrade to plain text, so a server-only change never crashes the client — it just renders wrong.

### 3.3 A new asset type
`[JsonDerivedType]` + subclass in `Entities/AssetData.cs` + const in `AssetTypeEnum`, then a branch in `MgGame.createItem()` (`mg.game.ts:476`). **Asset `Name` must be deterministic** (derived from content) — random names broke persisted games on restart; see the comment at `AssetData.cs:44`.

### 3.4 A new REST endpoint
`GameController` DTO + action → `GameBL` method → `dal.service.ts`. Note every controller/hub method is currently **unauthenticated** (§5.1).

---

## 4. Health of what exists

**Genuinely strong:**
- The dumb-client contract mostly holds — the client contains *zero* game-type literals; the catalog, camera, seat anchors, and panel tree are all server-driven.
- Deliberate, well-commented resilience: `safeFrame()` isolates third-party per-frame hooks (`mg.three.ts:531`), unknown assets and unknown UI nodes degrade instead of throwing, `_turnLock` serializes human/AI/undo per game, `DeepCopy()` via JSON gives real history snapshots.
- Real WebXR support with desktop→headset view continuity (`alignXrToDesktopView`, `mg.three.ts:819`) — unusual and hard-won.
- AI is not a token effort: perfect minimax in Tic-Tac-Toe, 3-ply αβ in Chess, depth-6 Checkers, phase-aware heuristics in Catan and Small World.

**Structurally weak:**
- **Zero tests anywhere.** No `*.spec.ts`, no test project, no karma/jasmine dependency — despite `README.md` and `Client.esproj` claiming Jasmine.
- 5 of 14 games split rules into a `*Rules.cs`; the other 9 keep engine + rendering + AI in one file (Small World 1038 L, Catan 996 L, ONW 929 L, D&D 862 L).
- ~300 lines of copy-pasted helpers across flows: `Arg()` is byte-identical in **8** files; `Shuffle`, `Log`, `Name`, `Attr/Set/Ints`, `MyTurn`, `SetBoardText`, the seat-ring layout are each duplicated 3–6×.
- Three incompatible turn models coexist (`CurrentTurnId`, `Attributes["turn"]`, derived roles), so `advanceNextTurn()` is unusable by most games.

---

## 5. Risks worth knowing before adding features

Ranked by what will actually bite. Items marked **verified** I confirmed directly in source.

### 5.1 🔴 No caller↔seat binding — anyone can act as any seat (**verified**)
`DispatchAction` resolves the actor purely from client-supplied `data.playerId` (`BaseGameFlow.cs:316-320`). `NotificationHub` has no `[Authorize]` and never correlates `Context.User` / `Context.ConnectionId` with a seat. `Program.cs:46-48` documents this as an accepted POC gap. Every "is it your turn" check compares the *claimed* player, so none of them help.

**One-line-ish fix:** `[Authorize]` on the hub + resolve the seat from `Context.UserIdentifier` instead of trusting `playerId`. Everything else (JWT issuance, `accessTokenFactory`, the `OnMessageReceived` query-string handler) is already wired.

### 5.2 🔴 `SelectPiece` / `MoveHere` are inherited into every game (**verified**)
`[GameAction]` is `Inherited = true`, and both live on `BaseGameFlow` (:547, :573). `MoveHere` writes `piece.Position` straight from the client-supplied `data.point` with **no turn check, no ownership check, no status check**. In Chess/Checkers/Splendor/Catan/… a client can `SelectPiece{any item}` then `MoveHere{anywhere}` and teleport a piece at any moment, including after `ENDED`. Also: `MoveHere` special-cases `GameType == "DND"` (:598) — game logic in the base class.

### 5.3 🔴 `ItemData.FindItem` returns the wrong item for nested items (**verified**)
`Entities/ItemData.cs:62-83`:
```csharp
var f = item.FindItem(itemId);
if (f != null) { found = item; return; }   // ← returns the PARENT, not f
```
Plus `return` inside `List.ForEach` doesn't short-circuit, so a later sibling can overwrite `found`. Today this is masked because every flow parents items directly to `GameData.Table`. **The first game you add with a nested item tree will hit it.** Related: `GameData.FindItem` searches hands/tables but `RemoveItem` / `removeItem()` search `Table` only.

### 5.4 🟠 No status gate, and the AI-turn check has a hole
`ExecuteAction` never checks `GameStatus` — actions are accepted before `StartGame()` built the scene and after `ENDED`. And the guard repeated in every board game,
```csharp
if (current.User != null && data.Player.User?.Id != current.User.Id) return;
```
(`TikTakToe:167`, `Gomoku:84`, `Reversi:79`, `Checkers:88`) **skips entirely when the turn holder is an AI** (AI seats have `User == null`), so in human-vs-AI a human can play the AI's moves. Chess has no caller check at all (`ChessSelect:289`, `ChessMove:331`).

### 5.5 🟠 Every action does a full double save + a global broadcast
`AfterAction` calls `HubGameUpdated` **and** `HubGamesUpdated`; each calls `DataRepository.Save()`, which serializes **all users + all games** to JSON and writes both rows. So one click = two full-database serializations. And every broadcast is `Clients.All` with the complete `GameData` — every connected client receives every game's full state and filters by id (`game-play.component.ts:131`).

This is also the **information leak** `CLAUDE.md` flags: Resistance roles, ONW cards, other seats' `Screen` trees all go over the wire to everyone. Per-user SignalR groups already exist (`NotificationHub.cs:46`) — the redaction point is `DataRepository.HubGameUpdated`.

### 5.6 🟠 Unvalidated args crash the action, silently desyncing state
Any exception inside a `[GameAction]` propagates out of `DispatchAction` and **skips `AfterAction()`** — so mutations already applied are neither broadcast nor saved. Clients and DB silently diverge until the next successful action. Reachable examples: `int.Parse(Arg(d,"x"))` before any check (`Carcassonne:191-192`), `Attributes["lastX"]` direct indexer (`Carcassonne:210`), unchecked `data.Item` (`TikTakToe:172-181`, `Durak:166`), `Convert.ToDouble(null-or-garbage)` in `BaseData.GetNumberAttribute` (`BaseData.cs:35`).

Also: `DnD` `LoadScene`/`PlaySound` (`DnDGameFlow.cs:288, 690`) accept an **arbitrary URL** from args with no allow-list against their own `SCENES`/`SOUNDS` tables.

### 5.7 🟠 Client leaks a WebGL context per game view
`MgThree.dispose()` (`mg.three.ts:248`) removes a resize listener and disposes the *magnifier* renderer only. It never disposes `renderer`, `composer`, the outline passes, the 2048² shadow map, the texture cache, or any scene geometry/material; `MgGame` has no `dispose()` at all; `InteractionManager.dispose()` exists and is **never called** (its listeners are on `document`, so they outlive the canvas). Browsers cap live WebGL contexts around 16 — repeated navigation into `/game-play/:id` will eventually blank the canvas.

Bonus bug: `mg.game.ts:1369-1370` removes the `'click'` listener twice instead of removing `'mouseover'`, and never removes `'mouseout'`.

### 5.8 🟡 Undo is half-implemented
`UndoLastMove` restores only `Table, Attributes, Winners, CurrentTurnId, GameStatus` — not `player.Hand`/`Table`/`Screen` — and never calls `RefreshScreens()`, so panels stay pre-undo for every game that overrides it. **Catan, Carcassonne, Splendor, ONW and D&D never call `SaveUndoPoint()` at all**, so the undo button is a silent no-op there. Small World saves 8 undo points but has no `AfterUndo()`.

### 5.9 🟡 Unbounded memory
`HistoryGameData` (`:360`) and `_undo` (`:258`) both grow forever, one full `GameData` JSON round-trip per action, retained for the process lifetime, per game. `mgThree.animationMixers` is append-only. Nothing trims.

### 5.10 🟡 Missing client assets + a broken dev/prod split
`angular.json:32-38` declares `src/favicon.ico` and `src/assets/**` — **neither exists**. So `assets/env.js` (`index.html:9`), six skybox JPGs (`mg.three.ts:280`) and `Roboto-msdf.json/.png` (`mg.game.ts:27,709`) all 404 into the SPA index.html fallback. Separately, `environment.development.ts:3` hardcodes `production: true`, so there's no runtime dev/prod distinction; budgets are raised to 100 MB; there's no lazy loading; and `debug-view.component.ts:61` ships a live `debugger` statement.

Also: `mg.game.ts:667` fetches the TEXT3D font from **`threejs.org`** at render time — a third-party CDN on the hot path.

### 5.11 🟡 Client-side leaks of game logic (violations of the core rule)
Mostly clean, but these will drift as games are added:
- `game-setup.component.html:39` hardcodes a **D&D image path**; `game-setup.component.ts:56` hardcodes the card-back filename table.
- `mg.game.ts:327,467` renders a literal **`'DEFENDING'`** badge (Durak rules text).
- `mg.game.ts:1275` — `isHoverable()` implements *"a piece whose colour ≠ the turn isn't hoverable"* (a game rule, client-side).
- `mg.game.ts:882` — a hardcoded vocabulary `['piece','disc','stone','item','card','fieldAtt','fieldDef']` used to **infer that a capture happened** from the diff.
- `mg.game.ts:423-653` — `makeCharCard()` bakes a **D&D character sheet** into the renderer.
- `mg.game.ts:342` — parses the seat name as `"<Colour> <Animal>"` to pick a head model and tint.
- `game-play.component.ts:101` / `game-setup.component.ts:79-89` — undo and seat-permission rules duplicated client-side.

### 5.12 🟡 Reconnect has no resync
`.withAutomaticReconnect()` is the whole story. No `onreconnected` (so `SetConnectionIDUser` is never re-sent), **no state resync after a gap** — `GameUpdated` is never replayed, so a missed update leaves a permanently stale board with no indication. `startConnection` can also be called twice (`app.component.ts:26` + `home-view.component.ts:46`) and overwrites the hub connection without stopping the old one.

### 5.13 🟡 No error handling on any HTTP call
Zero `catchError` in `dal.service.ts`; no error callback at any call site. A failed `getGameById` → blank screen, silently. `AuthInterceptor` never handles **401**, and `clearAuth()` is never invoked anywhere — **there is no logout or re-auth path**; an expired token means every request fails forever.

---

## 6. What I'd do before adding features

**Cheap, high leverage (a day or so):**
1. Bind the hub caller to the seat (§5.1) + add an `[Authorize]` — this is the difference between "POC" and "shareable".
2. Move `SelectPiece`/`MoveHere` off `BaseGameFlow` into an explicit `SandboxGameFlow`/`FreeMoveGameFlow` mixin that only D&D and Demo inherit (§5.2).
3. Fix `ItemData.FindItem` (§5.3) — 5 lines, and it's a landmine for anything you add.
4. Wrap `DispatchAction`'s `Invoke` in try/catch so a bad arg logs instead of desyncing (§5.6).
5. Gate `ExecuteAction` on `GameStatus == PLAY` (§5.4).
6. Refresh `CLAUDE.md` (§2.4) and the `DemoGameFlow` header comment (§3.1) — both now teach the wrong thing.

**Worth doing if this grows:**
7. Pull the duplicated helpers into `BaseGameFlow` / an `AttrCodec` mixin (~300 lines deleted, and new games get them free).
8. Per-seat redaction at `DataRepository.HubGameUpdated` + switch to per-game SignalR groups — fixes the leak *and* the broadcast cost in one change (§5.5).
9. Debounce/queue `Save()` instead of two full serializations per click (§5.5).
10. Implement `MgThree.dispose()` / `MgGame.dispose()` properly (§5.7).
11. Add a test project — even 20 tests over `ChessRules`, `CheckersRules`, `DurakRules`, `ReversiRules`, `GomokuRules` would pay for itself immediately; they're already cleanly separated and pure.
12. Fix the `src/assets` gap so skyboxes/fonts stop 404-ing (§5.10).

---

## 7. Quick reference

| Question | Answer |
|---|---|
| Where's the turn state? | `GameData.CurrentTurnId`, or `Attributes["turn"]` (BoardGameFlow games), or derived roles (Durak/Resistance/ONW) |
| Where's the 3D scene built? | The game flow's `StartGame()` / `Render()` — `GameData.Table` + `player.Hand` / `player.Table` |
| Where's the 2D panel built? | `RefreshScreens()` → `player.Screen` (`List<UiNode>`); rendered by `MgPanel3d.build()` |
| How does a click reach the server? | `ItemData.ClickActions[playerId or ""] = "<ActionName>"` → SignalR `ExecuteAction` → reflection to the `[GameAction]` method |
| Where do assets live? | `GameContent/games/**` and `GameContent/heads/**`, served at `/games` and `/heads` |
| Where's state persisted? | Two JSON blobs in SQLite (`Store` table, keys `users`/`games`), file in the OS temp dir |
| How do I run it? | `dotnet run` at the repo root; `cd Client && npm start` for the dev client (builds into `../wwwroot`) |
