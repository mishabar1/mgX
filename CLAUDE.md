# MultiGameX (MGx) — architecture rules

## THE CORE RULE: the client is DUMB. All logic lives on the server. ONE source of truth.

The Angular client is a pure renderer. It contains **no game logic of any kind** — not the
rules, not the buttons, not the texts, not the positions, not the settings, not even the list of
games. Everything is decided by the server and sent as data; the client only shows what it's told
and relays user actions back. If we swapped the client for another framework tomorrow, only the
generic renderer would be rewritten — no game would change.

**When adding or changing a game, edit the SERVER only. Never add game-specific code to the client.**

### What this means concretely
- **Game rules / state / AI** → in the game's `GameFlow` (C#), server-authoritative. State lives in
  `GameData.Attributes` + the item tree; actions are `[GameAction]` methods dispatched over SignalR.
- **The 3D scene** → the server builds `GameData.Table` / `player.Hand` / `player.Table` as `ItemData`
  (asset + position/scale/rotation/attributes). The client renders whatever items it's given.
- **The 2D panel / control UI** → the server builds `PlayerData.Screen` (a `UiNode` tree:
  title/text/image/model/button/select/input/check/checks/banner/log/animpick/row/col). The client
  has ONE generic renderer (`renderNode` in `game-play.component.ts`) that draws it and dispatches
  button actions via `executeActionArgs`. `panelMode` attribute = `full` (covers screen) or `side` (docked).
  Rebuilt every action via the `BaseGameFlow.RefreshScreens()` hook (override it per game).
- **The "Create a game" list** → from the server catalog `BaseGameFlow.GameCatalog()` via
  `GET /api/Game/GameTypes`. The client renders the buttons dynamically.
- **Seat zone placement** → where each seat's `player.Hand` / `player.Table` anchors sit is server-set,
  never hard-coded in the client: attributes `handAnchor`/`tableAnchor` ("x,y,z") and `handRot`/`tableRot`
  ("xDeg,yDeg,zDeg"). `BaseGameFlow.RunStartFlow` supplies standard defaults; a game overrides them
  (game-level in `GameData.Attributes`, or per-seat in `PlayerData.Attributes` — per-seat wins). An absent
  attribute is identity on the client (it decides nothing). E.g. Splendor computes per-seat anchors so each
  player's tokens/cards rest ON the felt, in front of them, facing centre.
- **HUD / labels / any derived UI text** → server sets an attribute (e.g. `hud`); the client displays it.
- **Per-game setup options** → generic attributes the server sets, e.g. `noAvatars=1` (no seated
  figures), `usesCardBack=1` (offer card-back chooser). No game type is ever hard-coded in the client.

### Adding a new game = server only
1. `GameData.cs`: add a `GameTypeEnum` const.
2. `BaseGameFlow.cs`: add a `GameCatalog()` line (button appears) + a `CreateGame()` / `PrettyName()` case.
3. `DataRepository.cs`: add an `AttachGameFlow()` case (so saved games reload).
4. New `GameFlows/<Name>GameFlow.cs : BaseGameFlow`: build the scene + `RefreshScreens()` panel +
   any generic flags (`noAvatars` / `usesCardBack` / `hud` / `panelMode`).

No client file is touched. See `GameFlows/DemoGameFlow.cs` for a heavily-commented reference of
every capability (adding items to board/hand/table/panel + interactions).

### Generic client capabilities (rendering only — NOT game logic)
These are things the client knows how to *draw*, driven entirely by server data: render 3D models,
thumbnail a model into a picture (`model` node / model-icon buttons), enumerate a loaded model's
animation clips (`animpick` node), render text/images/buttons/inputs. The server decides *what* and
*when*; the client only knows *how to draw*.

- **Server-driven camera (mid-game)** → the server owns the camera. The client applies the seat's
  `Camera` (or `Observer`) position at load, and re-applies it (smooth glide) whenever the
  SERVER-SENT value *changes* between updates; while it's unchanged the user's manual orbit is never
  touched. A game with a growing scene (e.g. Carcassonne) recomputes each seat's camera in its
  `Render()`/`RefreshScreens()` so the view pulls back as the board grows. Games with growing boards
  should also RE-CENTRE the scene on the origin each render (draw everything relative to the board's
  bounding-box centre) — the camera orbits (0,0,0), so the map stays framed no matter where it grows.

### Game assets live with the SERVER
Art/models/sounds + the fallback avatar heads live in `GameContent/` (`/games`, `/heads`), served by
ASP.NET static files (`Program.cs`, with a permissive CORS header for the cross-origin 3D loader).
The client points at them via `GAMES_BASE`/`HEADS_BASE` (`mg.three.ts`) = `environment.serverURL + '/games'|'/heads'`.
Game flows emit **relative** asset paths (e.g. `"resistance/map.png"`, `"monsters/.../Dragon.gltf"`);
the client prepends the base. Adding a game's assets = drop files in `GameContent/` (server only) —
no client rebuild. Only generic client render assets stay in the client: `src/assets/fonts` (3D-text
font) and `src/assets/skyboxes`. Angular's build ignores `src/assets/games|heads` (see `angular.json`).

### Pragmatic secrecy note
`GameData` is broadcast in full to every client, so "hidden" info (e.g. Resistance roles) is hidden
in the UI only, not on the wire. True per-player redaction would plug in at
`DataRepository.HubGameUpdated` (per-user SignalR groups already exist). Not yet implemented.
