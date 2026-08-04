# Upgrade Notes — .NET 7 → .NET 10 (LTS) & Angular 16 → 22

_Date: 2026-08-03 (completed 2026-08-04). Targets: **.NET 10 LTS** (supported to Nov 2028) and **Angular 22** (latest)._

> **Status: COMPLETE.** Backend builds on .NET 10 (`dotnet run` serving on :5112) and the Angular 22
> frontend builds clean and runs — login → create game → game-setup verified end to end in the browser.
> Both halves were done as source edits; the Angular majors were stepped through with `ng update` on the
> dev machine (the original automation environment had no NuGet/.NET access and couldn't finish `npm install`).

---

## Part A — Backend (.NET 7 → .NET 10) — DONE (needs your build to verify)

### Files changed
- **`MG.Server.csproj`** — `TargetFramework` `net7.0` → **`net10.0`**; package list cleaned and bumped.
- **`Program.cs`** — removed the `Microsoft.AspNetCore.SpaServices.AngularCli` using and the
  `app.UseSpa(...)` block; replaced with native `app.UseRouting()` + `app.MapFallbackToFile("index.html")`.
- **`BL/UserBL.cs`** — TensorFlow usings + `TensofFlowTest` body disabled (preserved as comments);
  method now returns `Task.CompletedTask`.
- **`Dockerfile`** — base images `7.0` → `10.0`; `EXPOSE 80/443` → **`EXPOSE 8080`**; runs as non-root `$APP_UID`.
- **`Properties/launchSettings.json`** — Docker profile `ASPNETCORE_URLS` → `http://+:8080`.

### NuGet package changes
Kept & bumped:
- `Microsoft.ML` 2.0.0 → **5.0.0**
- `Microsoft.ML.FastTree` 2.0.0 → **5.0.0**
- `Swashbuckle.AspNetCore` 6.5.0 → **10.2.3** (first line supporting .NET 10)
- `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` 1.19.5 → **1.21.2**

Removed (with reasons in the csproj comment):
- `Microsoft.AspNet.SignalR.Client` / `.Core` 2.4.3 — legacy ASP.NET (not Core) SignalR, unused.
- `Microsoft.AspNetCore.SignalR.Client.Core` — SignalR *client*; server-side SignalR is in the shared framework.
- `Microsoft.AspNetCore.SpaServices` 3.1.32 — obsolete.
- `Microsoft.AspNetCore.SpaServices.Extensions` — replaced by native `MapFallbackToFile`.
- `Microsoft.AspNetCore.OpenApi` — redundant with Swashbuckle (mixing them conflicts on .NET 10).
- `SciSharp.TensorFlow.Redist`, `TensorFlow.NET`, `TensorFlow.Keras` — large native dependency used
  **only** by the experimental `TensofFlowTest` debug endpoint. Removing it gives a clean build and a
  much smaller container image. Re-add + un-comment `UserBL.TensofFlowTest` to restore.

### How to verify the backend
```bash
dotnet restore
dotnet build -c Release        # expect 0 errors; pre-existing nullable warnings are OK
dotnet run                     # then open http://localhost:5112/swagger
```

### Things to watch (likely trivial, but check)
- `Swashbuckle 10.x` upgraded its OpenAPI model to 3.1 — a plain `AddSwaggerGen()/UseSwagger()/UseSwaggerUI()`
  setup like this one should work unchanged. If Swagger UI misbehaves, consider moving to .NET 10's native
  `AddOpenApi()` + a UI (Scalar) and dropping Swashbuckle entirely.
- `MLModel.*.cs` use only stable ML.NET APIs (`MLContext`, `Model.Load`, `CreatePredictionEngine`) that carry
  fine to 5.0.0. If Model Builder complains about the `.mlnet` file version, re-train/re-export from the current
  ML.NET tooling.

> **Not touched on purpose:** the security issues (client-controlled reflection in `ExecuteAction`, no auth,
> wide-open CORS) and the missing persistence. Those are separate phases from `SCAN_REPORT.md` — this upgrade
> deliberately keeps runtime behavior the same so you can isolate upgrade breakage from feature changes.

---

## Part B — Frontend (Angular 16 → 22) — DONE (builds clean, runs)

Upgraded one major at a time with `ng update` (16→17→18→19→20→21→22), then bumped the third-party
packages `ng update` doesn't manage. `ng build` is green and the app runs (login → create game → setup verified).

### Final installed versions
| Package | Was | Now |
|---|---|---|
| @angular/* + CLI + build | 16.2 | **22.1** |
| typescript | ~5.1 | **~6.0** |
| @angular/cdk | — | **^22.1** (new peer required by PrimeNG 22) |
| primeng | ^16.5 | **^22.0** |
| @primeuix/themes | — | **^3.0** (PrimeNG 22 theming preset — `Aura`) |
| primeicons | ^6.0 | **^8.0** |
| @fortawesome/angular-fontawesome | ^0.13 | **^5.1** |
| @microsoft/signalr | ^7.0 | **^10.0** |
| three + @types/three | ^0.157 | **^0.185** |
| @tweenjs/tween.js | ^21 | **^25** |
| quill | ^1.3 | **^2.0** |
| @types/node | (floating) | **^22** (pinned) |

### Issues hit during the upgrade and how they were fixed (for future reference)
- **esbuild builder (from v17) requires explicit `.js`** on deep three imports (`three/examples/jsm/...`,
  `three/src/...`). Added `.js` to all of them.
- **`moduleResolution: bundler` (from v20)** then type-checks those deep imports against three's `exports`
  map, so even type-only ones needed `.js`; also re-added `import * as THREE from 'three'` where code uses
  the `THREE.*` namespace (the global namespace was dropped in the new types).
- **New build system moved output** to `wwwroot/browser`; set `outputPath.browser: ""` in `angular.json`
  to keep serving from `wwwroot` (the .NET server expects `index.html` there).
- **three 0.185:** `TextGeometry` param `height`→`depth`; `scene.add(transformControls)` →
  `scene.add(transformControls.getHelper())`; custom mesh events (`click`/`mouseover`/`mouseout`) cast
  through `any` (three locks its event map); `THREE.Renderer` → `THREE.WebGLRenderer`.
- **PrimeNG 22:** InputNumber selector `p-inputNumber` → `p-inputnumber` and the `step` input was removed;
  Button `severity="warning"` → `"warn"`; removed the old `primeng/resources/*` CSS from `styles.scss` and
  `angular.json` (theming is now the `@primeuix/themes` preset via `providePrimeNG`).
- **@types/node** must match the TS version (too-new broke TS 5.1; after the upgrade, pinned to `^22`).

### Frontend gotchas / follow-ups
- Angular 22 defaults to standalone/new control flow, but the **NgModule** app still works — converting is
  optional (a later refactor). The v21 control-flow migration skipped `debug-view.component.html`
  ("invalid HTML"); it still uses `*ngIf/*ngFor`, which is fine.
- `HttpClientModule` is deprecated — the app now uses `provideHttpClient(withInterceptorsFromDi())`, and the
  `AuthInterceptor` rides on that.
- Stray empty file `Client/a.txt` can be deleted.
- PrimeNG shows an "Invalid PrimeUI License" badge without a license key — cosmetic only.

---

## Part C — DigitalOcean CI/CD notes (important)

1. **Port change:** .NET 8+ containers listen on **8080**, not 80. Update your DO service's
   **HTTP port / health-check port to 8080** (the Dockerfile now `EXPOSE 8080`). This is the #1 thing that
   silently breaks a deploy after the upgrade.
2. **TLS:** terminated by DigitalOcean (App Platform / LB). Keep the container HTTP-only on 8080 and
   **reconsider `app.UseHttpsRedirection()`** — behind DO's proxy it can cause redirect loops; gate it to
   non-container environments or remove it.
3. **Persistence (blocker for real use):** App Platform has an **ephemeral filesystem** — the SQLite
   `Database/app.db` and any file writes are wiped on every deploy/restart. The current `DataRepository`
   is in-memory anyway, so **all game/user state is lost on redeploy**. Before production, move to a managed
   DB (DO Managed Postgres) via EF Core, or attach a persistent volume (Droplet, not App Platform).
4. **CORS:** the wide-open `AllowCredentials + any origin` policy must be restricted to your real front-end
   origin(s) before going live (see `SCAN_REPORT.md`).
5. **Image size:** removing TensorFlow.NET drops hundreds of MB of native libs from the image — faster
   builds and pulls on DO.

---

## Suggested next steps (from the scan)
Once this builds green on both sides: tackle Phase 1 security from `SCAN_REPORT.md` —
allow-list the `ExecuteAction` dispatch (remove client-controlled reflection), add auth, lock down CORS.
