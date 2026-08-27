# MGx end-to-end suite

Black-box tests over the running server: **broadcast routing**, **per-seat redaction**,
**reconnect re-watch**, plus regression cover for seat authorization and normal gameplay.
No test framework — plain node, so there is nothing to install.

It lives under `Client/` on purpose: node then resolves `@microsoft/signalr` from
`Client/node_modules`, which the Angular client already depends on.

## Run

Build and start a server on an isolated port and database so a dev instance is untouched:

```powershell
dotnet build MG.Server.csproj -o C:\temp\mgx-e2e
$env:ASPNETCORE_ENVIRONMENT   = 'Production'      # skips the HTTPS redirect
$env:ConnectionStrings__DefaultConnection = 'Data Source=C:\temp\mgx-e2e-data\e2e.db'
Start-Process C:\temp\mgx-e2e\MG.Server.exe -ArgumentList '--urls','http://127.0.0.1:5199' `
  -WorkingDirectory 'C:\projects\my\mgX'
```

Then:

```bash
cd Client
npm run e2e                      # defaults to http://127.0.0.1:5199
MGX_BASE=http://localhost:5000 npm run e2e   # or point it anywhere
```

Exit code 0 = all green. The suite deletes the games it created; it leaves three
`E2E_*` users behind, which is why an isolated database is worth the two env vars.

## What it asserts

| Group | Covers |
|---|---|
| routing isolation | a watcher of game A gets A and nothing else; a watcher of B gets nothing from A |
| lobby | no list ping on a mid-game move; still pinged on create and delete |
| authorization | a user still cannot act on someone else's seat |
| redaction (Resistance) | own role visible, other seats' roles/cards absent, only own panel sent |
| redaction (REST) | same over `GetGameByID`, as the player / as an outsider / anonymous |
| games list | no item tree, no assets, no secrets in `GetGamesList` |
| redaction (ONW) | card attributes absent from **everyone**, own seat included |
| reconnect | a fresh connection re-watches and receives updates again |
| gameplay | tic-tac-toe vs AI reaches ENDED; Resistance advances reveal -> team |

## Note on coverage

This drives the **server and the wire protocol**. The Angular reconnect handler
(`SignalrService.reconnected$` -> the views' refetch) is type-checked and built, but its
runtime behaviour needs a browser; the suite covers the server half by reconnecting a raw
client, re-watching, and asserting delivery resumes.
