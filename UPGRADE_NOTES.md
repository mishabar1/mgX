# Upgrade Notes — .NET 7 → .NET 10 (LTS) & Angular 16 → 22

_Date: 2026-08-03. Targets chosen: **.NET 10 LTS** (supported to Nov 2028) and **Angular 22** (latest)._

> **Why some of this is a runbook, not finished code:** the automated environment used to
> apply these changes has no outbound access to Microsoft's .NET download servers or to
> NuGet, and cannot complete a long `npm install` (hard time limit). So the **backend was
> migrated at the source level** (done — see below) and the **frontend upgrade is scripted
> for you to run** on your machine or in CI, where `ng update` can do its schematic
> migrations properly. Build the solution in Visual Studio / `dotnet` and the Angular app
> with `ng` locally to verify.

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

## Part B — Frontend (Angular 16 → 22) — RUN THIS LOCALLY / IN CI

Angular must be upgraded **one major at a time** with `ng update` so its code-migration schematics run.
Do it on a clean tree (commit first). Node 20.19+/22.12+ is required for Angular 22 — you have a modern Node.

```bash
cd Client
# start clean if the sandbox left a partial folder:
rm -rf node_modules            # (on Windows: delete Client\node_modules)
npm install --legacy-peer-deps

# then step through the majors — commit after each step:
npx ng update @angular/core@17 @angular/cli@17
npx ng update @angular/core@18 @angular/cli@18
npx ng update @angular/core@19 @angular/cli@19
npx ng update @angular/core@20 @angular/cli@20
npx ng update @angular/core@21 @angular/cli@21
npx ng update @angular/core@22 @angular/cli@22

ng build                       # fix errors, then:
ng build --configuration production
```

### Third-party packages `ng update` will NOT manage — bump & test manually
| Package | Current | Target | Risk |
|---|---|---|---|
| **primeng** | ^16.5.0 | ^22 (match Angular) + add `@primeng/themes` | **HIGH** — v17+ completely reworked theming (styled/unstyled, then the `@primeng/themes` token system in v18/19). Expect template/style changes across every PrimeNG component. Budget the most time here. |
| **primeicons** | ^6.0.1 | ^7 | Low |
| **@fortawesome/angular-fontawesome** | ^0.13.0 | ^1/2 (per ng22 peer range) | Med — 0.13 only supports Angular 16; must bump or install fails. |
| **@microsoft/signalr** | ^7.0.12 | ^8 | Low — API stable. |
| **three** + **@types/three** | ^0.157.0 | latest 0.17x | Med — three has frequent breaking changes (moved examples, removed deprecations). Test the 3D board. |
| **three-mesh-ui** | ^6.5.4 | check | Med — verify it still supports the new `three`; may be unmaintained. |
| **@tweenjs/tween.js** | ^21 | latest | Low |
| **quill** | ^1.3.7 | ^2 | Med — Quill 2 has breaking API/format changes. |
| **ngx-json-viewer** | ^3.2.1 | latest | Low-Med — confirm Angular 22 peer support. |
| lodash / lodash-es / dayjs | — | fine | — |

### Frontend gotchas
- Set `"skipLibCheck": true` (usually already on) if `@types/three` throws during build.
- Angular 22 defaults to standalone/new control flow, but your **NgModule** app keeps working — you don't
  have to convert. Do it later as a separate refactor if desired.
- Remove the stray empty file `Client/a.txt`.

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
