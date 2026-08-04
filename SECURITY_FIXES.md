# Security & Production-Readiness Fixes

_Applied on top of the .NET 10 / Angular 22 upgrade. Build in Visual Studio / CI to verify — these
could not be compiled in the environment they were written in (no NuGet/.NET access there)._

## What was fixed

### C1 — Client-controlled reflection (RCE) — FIXED
`BaseGameFlow.ExecuteAction` previously called `GetType().GetMethod(clientActionId).Invoke(...)`, letting a
client invoke **any** method by name. Now:
- New marker attribute `GameFlows/GameActionAttribute.cs` (`[GameAction]`).
- `ExecuteAction` only invokes a `public instance Task(ExecuteActionData)` method **that carries `[GameAction]`**; anything else is rejected and logged.
- The three real actions are decorated: `TikTakToeGameFlow.HoverClick`, `TikTakToeGameFlow.RotateMe`, `DnDGameFlow.MapClick`.
- Also fixed a fire-and-forget bug: `RunEndGameFlow()` is now `await`ed.

> When you add new game actions, remember to tag them `[GameAction]` or they won't be callable.

### C2 — Authentication (JWT) — ADDED
- `Services/TokenService.cs` + `JwtSettings` issue signed JWTs.
- `Program.cs` wires `AddAuthentication().AddJwtBearer(...)`, validates issuer/audience/lifetime/signing key, and reads the token from the `access_token` query string for the `/notifications` SignalR path.
- `UserBL.Login` now returns `{ Token, User }` (`LoginResult`); `UserController.Login` is `[AllowAnonymous]`, everything else on `UserController` and `GameController` is `[Authorize]`, and `NotificationHub` is `[Authorize]`.
- **Key handling:** `Jwt:Key` is *not* in source. Dev uses a throwaway key; **production must set env var `Jwt__Key`** (32+ random chars) or the app refuses to start.

### C3 — CORS lockdown — FIXED
Replaced "allow every origin + credentials" (which browsers forbid and which exposed authenticated
endpoints to any site) with a config-driven allow-list: `Cors:AllowedOrigins` in `appsettings.json`
(defaults to `http://localhost:4200`). Set your real front-end origin(s) for production.

### C4 — Persistence + thread-safety — ADDED
- `Database/AppDbContext.cs` — EF Core + SQLite. State is stored as two JSON documents (`users`, `games`) in a `Store` table, preserving the tree-shaped game object graph without a fragile relational mapping.
- `Database/DataRepository.cs` — real `Load`/`Save` (was fully commented out). On load it deserializes state and **rebuilds the `[JsonIgnore]` runtime objects**: each game's `GameFlow`, and `AIAgent` timers for games in `PLAY`.
- All reads/writes go through a `lock`, so concurrent SignalR callbacks and AI ticks can't corrupt the collections mid-serialize.

### C5 — Secrets / DB in git — PARTIALLY (one manual step)
- `.gitignore` now ignores `Database/app.db` and `*.db*`.
- **You must untrack the already-committed DB yourself** (couldn't be done in the build environment — see manual steps).

### High-severity
- **H1** `async void` → `async Task` on both `NotificationHub` methods; the AI timer callback (which must stay `void`) now wraps its work in try/catch so it can't crash the process.
- **H2** AI timer interval `1ms` → `800ms` (was a per-player busy-loop).
- **H4** `GameBL.DeleteGame` now null-checks the game before use.
- **H5** Swagger is Development-only; `UseHttpsRedirection` is skipped in Production (TLS terminated by DigitalOcean).

### Correctness fixes (found during runtime testing)
- **`GameData.DeepCopy()`** previously returned `this`, so every `HistoryGameData` entry aliased the live
  game object and the whole history reflected only the latest state. Now does a real snapshot via
  System.Text.Json serialize/deserialize (GameFlow/AIAgent are `[JsonIgnore]`, so snapshots carry state only).
- **Removed class-level `[Consumes("application/json")]`** from `GameController` and `UserController`.
  That attribute made actions match only when the request carried `Content-Type: application/json`, which
  **GET requests don't** — so every GET (`GetGamesList`, `GetGameByID`) failed to match the controller, fell
  through to the SPA fallback, and returned `index.html`. Angular then failed to parse HTML as JSON and the
  game-setup screen rendered blank. (Latent in the original code; only surfaced once served from `wwwroot`.)

---

## Manual steps you must run (once)

1. **Clear the stale git lock** left by the build environment, then untrack the DB:
   ```bash
   del .git\index.lock            # Windows (or: rm -f .git/index.lock)
   git rm --cached Database/app.db
   git commit -m "untrack sqlite db; add gitignore rules"
   ```
2. **Delete the old `Database/app.db`** before first run. It's stale (the old app never actually
   persisted to it), and EF's `EnsureCreated()` won't add the new `Store` table to a DB that already
   has tables. A fresh file is created automatically on startup.
   ```bash
   del Database\app.db
   ```
3. **Set the JWT signing key** for any non-Development run:
   ```bash
   setx Jwt__Key "<32+ random characters>"     # or configure it in your host / DO env vars
   ```
4. **Set production CORS origins** — edit `Cors:AllowedOrigins` (or override via `Cors__AllowedOrigins__0` env var) to your deployed front-end URL.

## Front-end changes (Angular) — APPLIED

The client now sends the token. Changes made:
- **`bl/general.service.ts`** — holds `Token`, restores it from `localStorage` on start, `setAuth()`/`clearAuth()` helpers.
- **`bl/auth.interceptor.ts`** (new) — `HttpInterceptor` that adds `Authorization: Bearer <token>` to every request.
- **`app.module.ts`** — registers the interceptor via `HTTP_INTERCEPTORS`.
- **`services/SignalrService.ts`** — hub connection uses `accessTokenFactory: () => this.general.Token` (sends `?access_token=...`, which the server reads for `/notifications`).
- **`dal.service.ts`** — `login()` now returns `LoginResult { token, user }`.
- **`view/home-view/home-view.component.ts`** — login handler stores `res.token` + `res.user` via `setAuth()`.

> After the Angular 16→22 `ng update`, `HttpClientModule` still works but is deprecated; you can later
> switch to `provideHttpClient(withInterceptorsFromDi())` and keep the same interceptor.

## DigitalOcean deployment notes
- Container port is **8080** (set in the Dockerfile during the upgrade) — point your DO service/health-check there.
- Set env vars on the service: `Jwt__Key`, `Cors__AllowedOrigins__0`, `ASPNETCORE_ENVIRONMENT=Production`.
- **Persistence:** App Platform's filesystem is ephemeral — the SQLite file is wiped on every deploy.
  For durable data either attach a **persistent volume** (Droplet-based deploy) or switch the EF provider
  to **Managed Postgres** (change `UseSqlite` → `UseNpgsql` + connection string; the JSON-document storage
  model works unchanged).

## Build & verify
```bash
dotnet restore && dotnet build -c Release      # backend
dotnet run                                     # POST /api/User/Login -> get token; call /api/Game/* with Bearer token
```
