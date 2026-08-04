# MultiGameX — Code Scan & Upgrade Report

_Scan date: 2026-08-03_

## 1. What this project is

MultiGameX is a **multiplayer, multi-game platform** with a generic "game engine" design. A single backend serves several game types (Tic-Tac-Toe, Chess, D&D) through a shared abstract flow, and a 3D Angular client renders them with Three.js.

**Stack**

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core Web API, **.NET 7** (C#) |
| Realtime | SignalR (`/notifications` hub) |
| Frontend | Angular 16 + Three.js (3D board), PrimeNG |
| Data | In-memory lists (SQLite file + SQL script exist but are **unused**) |
| ML | ML.NET model (`MLModel.mlnet`) + TensorFlow.NET toy ResNet — both experimental/unwired |
| Deploy | Dockerfile (aspnet:7.0), single container |

**How it fits together**

`Program.cs` wires DI, CORS, SignalR, Swagger and serves the built Angular app from `wwwroot`. Controllers (`GameController`, `UserController`) are thin and delegate to business logic (`GameBL`, `UserBL`). `DataRepository` is a singleton holding `List<UserData>` and `List<GameData>` in memory. Each game owns a `BaseGameFlow` subclass implementing lifecycle hooks: `Create → Setup → StartGame → ExecuteAction* → EndGame`. AI players are driven by `AIAgent`, a timer that fires every 1ms, picks a random legal action, and executes it. State changes are broadcast to all clients over SignalR.

**Current maturity:** working prototype. Only Tic-Tac-Toe is implemented — Chess and D&D flows `throw new NotImplementedException()`. There are no tests, no auth, and no real persistence.

---

## 2. Critical issues (fix before anything else)

**C1 — Remote arbitrary-method invocation via reflection.**
`BaseGameFlow.ExecuteAction` does:
```csharp
MethodInfo theMethod = thisType.GetMethod(data.actionId);
await (Task)theMethod.Invoke(this, new object[] { data });
```
`data.actionId` comes straight from the client (HTTP body / SignalR message). Any client can name **any public method** on the flow object. This is an untrusted-input-to-reflection sink — a remote code execution / logic-abuse vector. Replace with an explicit allow-list dispatch (a `Dictionary<string, Func<...>>` or a `switch`) so only intended game actions are reachable.

**C2 — No authentication or authorization anywhere.**
`app.UseAuthorization()` is present but nothing is authenticated — there are no `[Authorize]` attributes, no identity, no tokens. "Login" just takes a name string and returns/creates a user (`UserBL.Login`). Any caller can create, join, act in, or **delete any game** (`DeleteGame` has no owner check and will `NullReferenceException` if the game doesn't exist). This is the biggest blocker to production.

**C3 — Wide-open CORS.**
```csharp
policy.SetIsOriginAllowed(host => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
```
`AllowCredentials()` combined with "allow every origin" is the exact combination browsers forbid for good reason — it exposes authenticated endpoints to any site. Restrict to known origins from config.

**C4 — No persistence (silent data loss).**
`DataRepository.Load()` and `Save()` are entirely commented out; `GameData.DeepCopy()` just returns `this`. All state lives in RAM and disappears on restart / redeploy. The developer's own note in `Load()` acknowledges this. Worse, the in-memory model with a 1ms AI timer mutating shared `List`/`Dictionary` state has **no thread-safety** — concurrent SignalR callbacks and timer ticks can corrupt collections or throw.

**C5 — Secrets & database committed to git.**
`Database/app.db`, `appsettings.json`, and `appsettings.Development.json` are tracked. Today they hold no secret beyond a connection string, but the pattern guarantees future key leakage. `env.js` already lists placeholders for Auth0, PayPal, and Mixpanel keys — those must never be committed.

---

## 3. High-priority issues

**H1 — `async void` everywhere it matters.** `AIAgent.Timer_Elapsed`, `NotificationHub.SetConnectionIDUser`, and `ExecuteAction` are `async void`. Exceptions in `async void` crash the process instead of being catchable. Convert to `async Task` and await/handle them.

**H2 — AI timer at 1ms interval.** `new System.Timers.Timer(1)` spins effectively as a busy-loop per AI player, burning CPU. Use a sensible interval (e.g. 500–1500ms, which also feels natural) or an event-driven "it's your turn" trigger.

**H3 — Fire-and-forget async without await.** In `BaseGameFlow.ExecuteAction`, `RunEndGameFlow()` is called without `await`; several BL methods call `_dataRepository.Save()` (a no-op today) inconsistently. Race conditions and swallowed exceptions.

**H4 — Null-reference risks.** `GameBL.DeleteGame` dereferences `game` before a null check. Most entity properties are non-nullable reference types with `<Nullable>enable</Nullable>` but are never initialized/validated from input — model validation is absent.

**H5 — Swagger and HTTPS redirect are unconditionally on.** The environment guards are commented out, so Swagger UI is exposed in every environment and `UseHttpsRedirection()` runs even inside the container that terminates TLS elsewhere.

**H6 — Duplicate / conflicting SignalR packages.** The csproj references both the **legacy** `Microsoft.AspNet.SignalR.*` 2.4.3 (ASP.NET, not Core) and the modern `Microsoft.AspNetCore.SignalR.Client.Core` 7.x. The legacy packages are unused and should be removed.

---

## 4. Medium / cleanup issues

- **Dead & commented-out code throughout** — large commented blocks in `DataRepository`, `UserBL.TensofFlowTest`, `NotificationHub`, controllers. `ExecuteAction` endpoint is commented out in the controller but reachable via the hub.
- **`UserBL.TensofFlowTest`** trains a CIFAR-10 ResNet on an HTTP GET request (`GetTensofFlowTest`). This downloads a dataset and runs training inside a web request — a debug leftover that should be removed. Note the typo "Tensof".
- **ML/TensorFlow largely unwired.** `MLModel.*`, `toy_resnet_model/`, and `TrainData/` are experiments; the ML prediction code in `AIAgent` is commented out. Decide whether ML is a real feature or should be removed to shrink the image (TensorFlow.NET + redist add hundreds of MB).
- **Naming/typos in public API** — lowercase DTO property names (`gameId`, `playerId`), `TensofFlowTest`, Hebrew commit message "ופ". Commit history is 40+ "up NN" messages — no meaningful history.
- **Stray files** — `Client/a.txt` (empty), `.gitignore` is an unusual 7 KB.
- **No input validation / model binding constraints**, no rate limiting, no request size limits.
- **`ServeUnknownFileTypes = true`** on static files serves any extension — tighten if not needed.

---

## 5. Dependency status

**.NET 7 is end-of-life** (support ended May 2024). This is the single most important upgrade: move to **.NET 8 LTS** (or .NET 9/10 if you want the newest — check current LTS before committing). All `Microsoft.AspNetCore.*` and `Microsoft.ML` packages should move to matching majors.

**Angular 16 is out of support.** Upgrade sequentially (16 → 17 → 18 → …) to a supported release; run `npm audit` after. `quill@1.3.7` is old and has known advisories — update to Quill 2.x. `three@0.157` is far behind current.

Run `dotnet list package --vulnerable --include-transitive` and `npm audit` as the first concrete step — the results will drive the upgrade order.

---

## 6. Suggested path to production-grade

**Phase 0 — Safety net (do first)**
1. Untrack secrets/DB: `git rm --cached Database/app.db appsettings*.json`, move real config to user-secrets / env vars, rotate anything ever committed.
2. Add a minimal test project so refactors are safe (start with Tic-Tac-Toe flow unit tests).
3. Framework upgrade: .NET 7 → 8 LTS; Angular 16 → supported.

**Phase 1 — Security**
4. Replace the reflection dispatch in `ExecuteAction` with an allow-list (fixes C1).
5. Add authentication (ASP.NET Identity or an OIDC provider such as the Auth0 already stubbed in `env.js`) + `[Authorize]` on controllers/hub; add ownership checks on join/delete/act.
6. Lock down CORS to known origins; gate Swagger to non-production.

**Phase 2 — Persistence & correctness**
7. Introduce EF Core (SQLite for dev, Postgres/SQL Server for prod) and replace the in-memory singleton; make repository access thread-safe.
8. Fix `async void` → `async Task`, add global exception handling/logging, implement a real `DeepCopy` for game history.
9. Replace the 1ms AI timer with an event-driven or throttled scheduler.

**Phase 3 — Ops & polish**
10. Structured logging (Serilog), health checks, config-driven settings, request validation + rate limiting.
11. CI pipeline: build, test, `dotnet list package --vulnerable`, `npm audit`, Docker build; add a multi-stage non-root Dockerfile with a persistent volume for the DB.
12. Decide the fate of the ML/TensorFlow experiments; remove if not shipping to cut image size.
13. Finish or explicitly stub Chess/D&D so they don't throw at runtime.

---

## 7. Quick reference — findings by severity

| ID | Severity | Issue | File |
|---|---|---|---|
| C1 | Critical | Client-controlled reflection → method invocation | `GameFlows/BaseGameFlow.cs` |
| C2 | Critical | No authentication/authorization | `Program.cs`, controllers |
| C3 | Critical | CORS allows any origin + credentials | `Program.cs` |
| C4 | Critical | No persistence; not thread-safe | `Database/DataRepository.cs` |
| C5 | Critical | DB + settings committed to git | repo root |
| H1 | High | `async void` swallows/crashes on errors | `AIAgent.cs`, `NotificationHub.cs` |
| H2 | High | AI timer at 1ms (CPU burn) | `BL/AIAgent.cs` |
| H3 | High | Un-awaited async / races | `BaseGameFlow.cs`, `GameBL.cs` |
| H4 | High | Null-deref + no input validation | `GameBL.DeleteGame` |
| H5 | High | Swagger + HTTPS redirect always on | `Program.cs` |
| H6 | High | Legacy + Core SignalR both referenced | `MG.Server.csproj` |
| M* | Medium | Dead code, ML leftovers, EOL frameworks | multiple |
