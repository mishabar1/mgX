using System;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // =====================================================================================
    // DEMO — a living reference for building on this platform.  This is NOT a playable game:
    // it never ends and keeps no score.  Instead it shows, in one place, the two things you
    // need to build any game here:
    //
    //   1) HOW TO ADD ITEMS to every zone of the scene:
    //        • the shared BOARD            -> addItem(asset)
    //        • a player's HAND             -> addItemToPlayerHand(seat, asset)
    //        • a player's personal TABLE   -> addItemToPlayerTable(seat, asset)
    //        • the on-screen CONTROL PANEL -> built as HTML on the client (see the demo console
    //                                          in game-play.component.ts, setupDemoConsole())
    //
    //   2) HOW TO ADD INTERACTION to items:
    //        • click a 3D item            -> item.AddAction(Method)  +  a [GameAction] method
    //        • a control-panel button     -> executeActionArgs(...)  ->  Arg(data,"key")
    //
    // ARCHITECTURE IN ONE PARAGRAPH: the server is authoritative.  A game is a subclass of
    // BaseGameFlow that builds a tree of ItemData (the scene) and mutates GameData.Attributes
    // (your state).  After every action the base broadcasts the whole GameData to all clients,
    // and the Angular client just RENDERS it with Three.js — there is no game-specific code on
    // the client except optional HTML panels.  Actions travel client -> server over SignalR and
    // are dispatched by name, but ONLY to methods marked [GameAction] (a security allow-list).
    //
    // TO ADD A BRAND-NEW GAME you touch four places (grep for "DEMO" to see them all):
    //    A) Entities/GameData.cs .......... add a const to GameTypeEnum
    //    B) GameFlows/BaseGameFlow.cs ...... add a case to CreateGame() and to PrettyName()
    //    C) Database/DataRepository.cs ..... add a case to AttachGameFlow() (so saved games reload)
    //    D) Client .../games-list.component.html ... add a create button
    // ...then write a *GameFlow.cs like this one.
    // =====================================================================================
    public class DemoGameFlow : BaseGameFlow
    {
        // ---------------------------------------------------------------------------------
        // ASSETS.  An asset is a REUSABLE definition of "what a thing looks like" (a model, an
        // image, a piece of text, a sound, or a procedural shape).  You register each asset once
        // (addAsset) and then create as many ITEMS from it as you like.  Asset keys are content-
        // derived and de-duplicated, so re-adding the same asset is harmless.
        //
        // The asset TYPES available (see Entities/AssetData.cs):
        //   TokenAssetData(front[,back]) - a thin textured card/tile (needs an image under assets/games)
        //   ObjectAssetData(url)         - a 3D model: .glb / .gltf / .obj / .stl
        //   Text3dAssetData(text)        - 3D text (default text; override per item with SetText)
        //   CylinderAssetData(key)       - a procedural round disc (no image needed) — tint per item
        //   ArrowAssetData(key)          - a procedural flat arrow
        //   SoundAssetData(url)          - an mp3 you can play on demand
        //   DieAssetData(key)            - a procedural numbered die
        // ---------------------------------------------------------------------------------
        internal class Assets
        {
            internal static AssetData TEXT  = new Text3dAssetData("demo");                 // captions
            internal static AssetData DISC  = new CylinderAssetData("demo");               // procedural, no file
            internal static AssetData MODEL = new ObjectAssetData("ticktacktoe/x.glb");    // a 3D model file
            internal static AssetData CARD  = new TokenAssetData("common/back/red-56.jpg", // an image card, with a
                                                                 "common/back/red-56.jpg");// separate back face
            internal static AssetData BEEP  = new SoundAssetData("ticktacktoe/beep.mp3");  // a short sound (ONCE)
            internal static AssetData MUSIC = new SoundAssetData("dnd/deutschland.mp3");   // a track to LOOP
            internal static AssetData DIE   = new DieAssetData("demo");                    // procedural numbered die
            internal static AssetData ARROW = new ArrowAssetData("demo");                  // procedural flat arrow
            internal static AssetData BLOCK = new TextBlockAssetData("demo");              // billboard text block
            internal static AssetData HERO  = new ObjectAssetData("heroes/glTF/Warrior.gltf"); // a RIGGED model (has animation clips)
        }

        // A colour palette for the "spawn a disc" panel demo. NOTE the format quirk you must know:
        //   • CYLINDER/model "tint"  wants "0xRRGGBB"  (parsed as a hex number on the client)
        //   • TEXT3D       "textColor" wants "RRGGBB"  (bare hex, no 0x)
        private static readonly string[] TINTS = { "0xE03131", "0x22C55E", "0x2563EB", "0xF59E0B", "0x9333EA" };

        // Read a value posted by a control-panel button (args), falling back to a clicked item's
        // attribute. This is THE helper that lets HTML-panel buttons drive server actions.
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        public DemoGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DEMO;
        }

        // Let a single person open the demo alone (a real game would require all its seats).
        public override int MinPlayers => 1;

        // =============================== lifecycle ===============================
        // Create() runs ONCE when the game is created. Register assets, define the seats, and
        // place the spectator ("observer") camera. Do NOT build the playable scene here — that's
        // StartGame()'s job (Create's seats/observer survive; the scene is (re)built on start).
        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            addAsset(Assets.DISC);
            addAsset(Assets.MODEL);
            addAsset(Assets.CARD);
            addAsset(Assets.BEEP);
            addAsset(Assets.MUSIC);
            addAsset(Assets.DIE);
            addAsset(Assets.ARROW);
            addAsset(Assets.BLOCK);
            addAsset(Assets.HERO);

            // Where a spectator (no seat) looks from.
            GameData.Observer.Position.Set(0, 9, 10);

            // Two seats. A seat starts as EMPTY_SEAT; a human/AI claims it on the setup screen.
            // Each seat gets a camera position (where that player looks from) and an avatar
            // position (where their figure/hand sits). We tag a "type" so we can find them later.
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "a").SetCameraPosition(0, 3, 9).SetAvatarPosition(0, 1, 8);
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "b").SetCameraPosition(0, 3, -9).SetAvatarPosition(0, 1, -8);

            return Task.CompletedTask;
        }

        // Setup() resets to a clean pre-start state. The base already wipes the table/hands and
        // clears Attributes before calling this, so most games leave it empty.
        protected override Task Setup() => Task.CompletedTask;

        // StartGame() builds the scene the moment the game starts. Here we lay out one example of
        // everything. In a real game you'd deal cards / place pieces instead.
        protected override Task StartGame()
        {
            BuildBoardItems();      // 1) items on the shared board + click interactions
            BuildPlayerZones();     // 2) items in each player's hand and personal table
            BuildAssetGallery();    // 6) the remaining asset types: die, arrow, text block, music, rigged model
            BuildMoveDemo();        // 5) the built-in "select a piece, click the surface to move it" system
            BuildDragDemo();        // 4) item-onto-item interaction (click source, then target)
            advanceNextTurn();      // 1) turn system: set the first seat to move
            RefreshTurnArea();      // 1) show whose turn + a token only the current seat can click
            BuildScreens();         // the CONTROL PANEL, described by the server (client just renders it)
            return Task.CompletedTask;
        }

        // The control panel, as server-described UI. The client renders these nodes verbatim and
        // sends button actions back — no panel logic on the client.
        private void BuildScreens()
        {
            GameData.Attributes["panelMode"] = "side";   // dock on the right, keep the 3D scene visible
            var colors = new List<UiOption> {
                new("random colour", ""), new("red", "0xE03131"), new("green", "0x22C55E"),
                new("blue", "0x2563EB"), new("amber", "0xF59E0B"), new("purple", "0x9333EA")
            };
            foreach (var seat in GameData.Players)
            {
                if (seat.Type == PlayerTypeEnum.EMPTY_SEAT) { seat.Screen = null; continue; }
                seat.Screen = new List<UiNode>
                {
                    UiNode.Title("🧪 Demo panel"),
                    UiNode.Text_("Spawn a disc on the board", "8aa0c0", 13),
                    UiNode.Select("color", colors),
                    UiNode.Row(
                        UiNode.Button("➕ Spawn disc", nameof(PanelSpawn), gather: new() { "color" }),
                        UiNode.Button("🗑 Clear spawned", nameof(PanelClear))),
                    UiNode.Text_("Drop a floating label", "8aa0c0", 13),
                    UiNode.Input("text", "type text…"),
                    UiNode.Button("💬 Add label", nameof(PanelSay), gather: new() { "text" }),
                    UiNode.Text_("Music (looping sound)", "8aa0c0", 13),
                    UiNode.Row(UiNode.Button("🎵 Play", nameof(PlayMusic)), UiNode.Button("⏹ Stop", nameof(StopMusic))),
                    UiNode.Text_("Turn / end", "8aa0c0", 13),
                    UiNode.Row(UiNode.Button("⏭ End turn", nameof(EndTurn)),
                               UiNode.Button("🏁 End game", nameof(EndDemo), confirm: "End the demo?")),
                };
            }
        }

        // 2) ENDING A GAME. The demo normally never ends, but the panel's "End game" button sets an
        //    "over" flag. The base checks IsEndGame() after every action; when true it sets
        //    GameStatus=ENDED, fills Winners from GetGameWinners(), runs EndGame(), and the client
        //    pops the game-over overlay showing Attributes["result"].
        protected override Task<bool> IsEndGame() => Task.FromResult(GameData.Attributes.ContainsKey("over"));
        protected override Task EndGame()
        {
            GameData.Attributes["result"] = GameData.Attributes.GetValueOrDefault("result", "Demo ended.");
            return Task.CompletedTask;
        }
        protected override List<PlayerData> GetGameWinners()
        {
            var id = GameData.Attributes.GetValueOrDefault("winnerId", "");
            return GameData.Players.Where(p => p.Id == id).ToList();
        }

        // No AI behaviour — keep the sandbox still. (A real game overrides PlayAI; the default AI
        // would otherwise click random clickable items on an AI seat's turn.)
        public override bool IsAITurn(PlayerData player) => false;

        // ============================================================================
        // 1) ADDING ITEMS TO THE BOARD  (GameData.Table)  — addItem(asset)
        // ============================================================================
        private void BuildBoardItems()
        {
            // A caption. TEXT3D: set the text per item with SetText; colour with "textColor" (bare hex).
            Caption("BOARD ITEMS  (click them!)", 0, 3.2, 0);

            // Every item supports a fluent chain: SetPosition(x,y,z) / SetScale(s | x,y,z) /
            // SetRotation(xDeg,yDeg,zDeg) / AddAttribute(key,val).  X = left/right, Y = up,
            // Z = toward/away from the near seat.  We lay these out left-to-right along X.

            // (a) A procedural DISC (no image file). "tint" colours it ("0xRRGGBB").
            //     INTERACTION: clicking it runs LiftItem (toggles it up/down).
            addItem(Assets.DISC).SetPosition(-4, 0, 0).SetScale(1.4)
                .AddAttribute("tint", "0x2563EB")
                .AddAttribute("demo", "1")
                .AddAction(LiftItem);                 // clickable by ANYONE (ClickActions[""])
            Caption("disc · click = lift", -4, 1.2, 0, 0.28);

            // (b) A 3D MODEL from a file. INTERACTION: clicking spins it (SpinItem).
            addItem(Assets.MODEL).SetPosition(-1.3, 0, 0).SetScale(1)
                .AddAttribute("demo", "1")
                .AddAction(SpinItem);
            Caption("model · click = spin", -1.3, 1.2, 0, 0.28);

            // (c) An image CARD (TOKEN). INTERACTION: clicking plays a sound (Beep).
            addItem(Assets.CARD).SetPosition(1.3, 0, 0).SetScale(1.6)
                .AddAttribute("demo", "1")
                .AddAction(Beep);
            Caption("card · click = beep", 1.3, 1.2, 0, 0.28);

            // (d) An item only ONE seat can see & click. Visible[seatId]=true hides it from
            //     everyone else; giving ClickActions only that seat makes it their button.
            var seatA = getPlayerByAttribute("type", "a");
            if (seatA != null)
            {
                var it = addItem(Assets.DISC).SetPosition(4, 0, 0).SetScale(1.4)
                    .AddAttribute("tint", "0x22C55E").AddAttribute("demo", "1");
                it.AddAction(seatA.Id, SpinItem);     // only seat A's user can click it
                it.Visible[seatA.Id] = true;          // and only seat A even sees it
                Caption("private to seat A", 4, 1.2, 0, 0.28);
            }

            // (e) MOVING an item across the board: each click steps it right (and wraps).
            addItem(Assets.DISC).SetPosition(-4, 0, -2.4).SetScale(1.2)
                .AddAttribute("tint", "0x9333EA").AddAttribute("demo", "1")
                .AddAction(MoveItem);
            Caption("disc · click = walk right", 0, 3.2 - 3.5, -2.4, 0.28);
        }

        // ============================================================================
        // 2) ADDING ITEMS TO A PLAYER'S HAND  (player.Hand)  and  TABLE  (player.Table)
        //    Hands/tables render anchored near that seat. Do it for BOTH seats so whoever
        //    joins sees their own.
        // ============================================================================
        private void BuildPlayerZones()
        {
            foreach (var seat in GameData.Players)
            {
                // --- the player's HAND: addItemToPlayerHand(seat, asset) ---
                // A private card whose FACE only its owner sees: set the "owner" attribute and the
                // client draws the back to everyone else. (The data is still broadcast — this hides
                // it in the UI, which is the platform's pragmatic privacy; see the notes on secrecy.)
                addItemToPlayerHand(seat, Assets.CARD).SetPosition(-0.8, 0, 0).SetScale(1.4)
                    .AddAttribute("owner", seat.Id);
                addItemToPlayerHand(seat, Assets.CARD).SetPosition(0.8, 0, 0).SetScale(1.4)
                    .AddAttribute("owner", seat.Id);

                // --- the player's personal TABLE: addItemToPlayerTable(seat, asset) ---
                // A disc parked in front of that seat, clickable by anyone (e.g. a shared token).
                addItemToPlayerTable(seat, Assets.DISC).SetPosition(0, 0, 0).SetScale(1.0)
                    .AddAttribute("tint", "0xF59E0B")
                    .AddAction(SpinItem);
            }
        }

        // Little helper: a floating 3D caption. Shows SetText + the "textColor" (bare hex) attribute.
        private ItemData Caption(string text, double x, double y, double z, double scale = 0.4)
            => addTextItem(Assets.TEXT).SetText(text).SetPosition(x, y, z).SetScale(scale)
                   .AddAttribute("textColor", "e8edf5");

        // ============================================================================
        // 6) THE REMAINING ASSET TYPES — a DIE, an ARROW, a TEXTBLOCK, a rigged MODEL, and
        //    LOOPING music (played from the panel). Laid out in a back row (z = -4).
        // ============================================================================
        private void BuildAssetGallery()
        {
            Caption("ASSET GALLERY", 0, 3.2, -4, 0.4);

            // A procedural DIE. "sides" sets the range; "result" is the shown face ("0" = "?").
            // INTERACTION: click to roll (RollDie sets a new result).
            addItem(Assets.DIE).SetPosition(-4, 0.5, -4).SetScale(1)
                .AddAttribute("sides", "6").AddAttribute("result", "0")
                .AddAttribute("demo", "1").AddAction(RollDie);
            Caption("die · click = roll", -4, 1.6, -4, 0.26);

            // A procedural ARROW. "len" = length; rotation.y aims it; "tint" colours it.
            // INTERACTION: click to spin it around (re-aim).
            addItem(Assets.ARROW).SetPosition(-1.3, 0.1, -4).SetRotation(0, 0, 0)
                .AddAttribute("len", "2").AddAttribute("tint", "0x22C55E")
                .AddAttribute("demo", "1").AddAction(SpinItem);
            Caption("arrow · click = aim", -1.3, 1.6, -4, 0.26);

            // A TEXTBLOCK — a billboard panel of text that always faces the camera.
            addItem(Assets.BLOCK).SetText("Hello!\nI'm a text block").SetPosition(1.5, 1.2, -4).SetScale(1)
                .AddAttribute("demo", "1");
            Caption("text block (billboard)", 1.5, 2.2, -4, 0.26);

            // A RIGGED model with animation clips. AnimationIdx picks the clip (-1 = none).
            // INTERACTION: click to cycle to the next clip (CycleAnim).
            addItem(Assets.HERO).SetPosition(4, 0, -4).SetScale(2)
                .AddAttribute("demo", "1").AddAction(CycleAnim);
            Caption("model · click = next animation", 4, 2.4, -4, 0.26);
        }

        // ============================================================================
        // 5) BUILT-IN SELECT-THEN-MOVE. The base class provides makeMovable() (click to select,
        //    highlights in place) and makeMoveSurface() (click the surface to move the selected
        //    piece to the click point). This is exactly how Chess and D&D move pieces.
        // ============================================================================
        private void BuildMoveDemo()
        {
            Caption("SELECT A PIECE, THEN CLICK THE MAT", -3.5, 3.0, 4, 0.3);

            // The mat: a wide flat disc that acts as the movement surface.
            var mat = addItem(Assets.DISC).SetPosition(-3.5, -0.05, 4).SetScale(5, 0.2, 3)
                .AddAttribute("tint", "0x334155").AddAttribute("demo", "1");
            makeMoveSurface(mat);

            // The movable piece sitting on the mat.
            var piece = addItem(Assets.MODEL).SetPosition(-4.5, 0.2, 4).SetScale(1)
                .AddAttribute("demo", "1");
            makeMovable(piece);
        }

        // ============================================================================
        // 4) ITEM-ONTO-ITEM ("drag"). NOTE: the client has no pointer-drag gesture, so the
        //    platform pattern for "drop A onto B" is: click the SOURCE (DragStart remembers it),
        //    then click the TARGET (DragOnto acts on both). ExecuteActionData also carries a
        //    dragTargetItemId field reserved for a future true drag.
        // ============================================================================
        private void BuildDragDemo()
        {
            Caption("CLICK A GREEN DISC, THEN A RED ONE", 3.5, 3.0, 4, 0.3);

            // Two "source" discs (green) and one "target" disc (red). Click a source, then the
            // target, and the source jumps onto the target.
            addItem(Assets.DISC).SetPosition(2.5, 0, 4).SetScale(1)
                .AddAttribute("tint", "0x22C55E").AddAttribute("demo", "1").AddAction(DragStart);
            addItem(Assets.DISC).SetPosition(3.5, 0, 4).SetScale(1)
                .AddAttribute("tint", "0x22C55E").AddAttribute("demo", "1").AddAction(DragStart);
            addItem(Assets.DISC).SetPosition(4.8, 0, 4.6).SetScale(1.3)
                .AddAttribute("tint", "0xE03131").AddAttribute("demo", "1").AddAction(DragOnto);
        }

        // ============================================================================
        // 1) TURN SYSTEM. CurrentTurnId tracks whose turn it is; advanceNextTurn() rotates it
        //    through the seats. Here we show the current player's name and a token that ONLY the
        //    current seat can click; clicking it (or the panel's "End turn") passes the turn.
        //    We rebuild just this small area each time (tagged "turnarea") rather than the whole scene.
        // ============================================================================
        private void RefreshTurnArea()
        {
            foreach (var it in getItemsByAttribute("turnarea")) removeItem(it.Id);

            var current = GameData.Players.FirstOrDefault(p => p.Id == GameData.CurrentTurnId);
            string who = current != null ? PlayerDisplayName(current) : "?";
            addTextItem(Assets.TEXT).SetText("TURN: " + who).SetPosition(0, 5, 0).SetScale(0.6)
                .AddAttribute("textColor", "ffd166").AddAttribute("turnarea", "1");

            // A token clickable ONLY by whoever controls the current seat (hotseat-friendly).
            var token = addItem(Assets.DISC).SetPosition(0, 0.2, 2).SetScale(1.2)
                .AddAttribute("tint", "0xF59E0B").AddAttribute("turnarea", "1");
            if (current != null)
            {
                var controllingSeatIds = GameData.Players
                    .Where(p => current.User != null && p.User?.Id == current.User.Id)
                    .Select(p => p.Id).ToList();
                if (controllingSeatIds.Count == 0) controllingSeatIds.Add(current.Id);  // AI/empty seat
                foreach (var sid in controllingSeatIds) token.AddAction(sid, EndTurn);
            }
            Caption("your turn · click = end turn", 0, 1.4, 2, 0.26).AddAttribute("turnarea", "1");
        }

        // ============================================================================
        // 3) INTERACTIONS.  A [GameAction] is a public async Task M(ExecuteActionData data).
        //    Only [GameAction]-marked methods can be invoked by clients (security allow-list).
        //    `data.Item`   = the clicked item (null for panel-only actions)
        //    `data.Player` = the acting seat
        //    Mutate GameData / items here; the base broadcasts the result automatically.
        // ============================================================================

        // Clicking a model spins it a bit (and pulses its scale). Reads/writes the clicked item.
        // 3) UNDO: SaveUndoPoint() snapshots the state BEFORE the change, so the Undo button (shown
        //    to whoever made the last move) can roll it back. Add it to any real, reversible action.
        [GameAction]
        public async Task SpinItem(ExecuteActionData data)
        {
            if (data.Item == null) return;
            SaveUndoPoint();
            data.Item.Rotation.Y += 30;                       // rotate 30° each click
            data.Item.Scale.X = data.Item.Scale.X > 2 ? 1 : data.Item.Scale.X + 0.3;  // pulse width
            await Task.CompletedTask;
        }

        // Clicking a disc toggles it up/down. Stores the up/down flag as an attribute on the item.
        [GameAction]
        public async Task LiftItem(ExecuteActionData data)
        {
            if (data.Item == null) return;
            SaveUndoPoint();
            bool up = data.Item.GetStringAttribute("up") == "1";
            data.Item.Position.Y = up ? 0 : 1.5;
            data.Item.Attributes["up"] = up ? "0" : "1";      // overwrite (never AddAttribute an existing key)
            await Task.CompletedTask;
        }

        // Clicking a disc walks it one step to the right, wrapping around. Shows moving an item
        // by changing its Position (the client tweens/renders the new position on the next update).
        [GameAction]
        public async Task MoveItem(ExecuteActionData data)
        {
            if (data.Item == null) return;
            SaveUndoPoint();
            double x = data.Item.Position.X + 2;
            if (x > 4) x = -4;                                 // wrap back to the left
            data.Item.Position.X = x;
            await Task.CompletedTask;
        }

        // 6) Roll the clicked die: set a new "result" (1..sides). result "0" renders as "?".
        [GameAction]
        public async Task RollDie(ExecuteActionData data)
        {
            if (data.Item == null) return;
            SaveUndoPoint();
            int sides = data.Item.GetIntAttribute("sides"); if (sides < 2) sides = 6;
            data.Item.Attributes["result"] = new Random().Next(1, sides + 1).ToString();
            data.Item.Rotation.Y += 90;                        // visible feedback on every roll
            await Task.CompletedTask;
        }

        // 6) Cycle a rigged model to its next animation clip. The client plays AnimationIdx; we
        //    don't know the clip count here, so we cycle 0..5 then back to none (-1). Out-of-range
        //    indices simply play nothing until they wrap to a valid clip.
        [GameAction]
        public async Task CycleAnim(ExecuteActionData data)
        {
            if (data.Item == null) return;
            SaveUndoPoint();
            data.Item.AnimationIdx = data.Item.AnimationIdx >= 5 ? -1 : data.Item.AnimationIdx + 1;
            await Task.CompletedTask;
        }

        // 4) "Drag" step 1: remember which item was picked (store its id in an attribute).
        [GameAction]
        public async Task DragStart(ExecuteActionData data)
        {
            if (data.Item == null) return;
            GameData.Attributes["dragSrc"] = data.itemId;      // remember the source item
            await Task.CompletedTask;
        }

        // 4) "Drag" step 2: drop the remembered source onto this target (snap it on top of it).
        [GameAction]
        public async Task DragOnto(ExecuteActionData data)
        {
            if (data.Item == null) return;
            var srcId = GameData.Attributes.GetValueOrDefault("dragSrc", "");
            var src = GameData.FindItem(srcId);
            if (src == null) return;
            SaveUndoPoint();
            src.Position.X = data.Item.Position.X;             // land on the target
            src.Position.Y = data.Item.Position.Y + 0.3;
            src.Position.Z = data.Item.Position.Z;
            GameData.Attributes.Remove("dragSrc");
            await Task.CompletedTask;
        }

        // 1) Pass the turn to the next seat, then refresh the turn indicator + per-turn token.
        [GameAction]
        public async Task EndTurn(ExecuteActionData data)
        {
            SaveUndoPoint();
            advanceNextTurn();
            RefreshTurnArea();
            await Task.CompletedTask;
        }

        // Clicking a card plays a sound. We drop any previous sound item first so it can replay.
        [GameAction]
        public async Task Beep(ExecuteActionData data)
        {
            getItemsByAsset(Assets.BEEP).ForEach(s => removeItem(s.Id));
            playSound(Assets.BEEP, "ONCE");                    // "ONCE" or "LOOP"
            await Task.CompletedTask;
        }

        // ============================================================================
        // 4) CONTROL-PANEL actions.  These come from the HTML demo console (client-side,
        //    game-play.component.ts -> setupDemoConsole()).  The panel calls
        //    executeActionArgs(gameId, seatId, "PanelSpawn", { color: "..." }); the value
        //    arrives here via Arg(data,"color").  No clicked 3D item is involved.
        // ============================================================================

        // Spawn a new clickable disc at a random spot on the board, in the chosen colour.
        [GameAction]
        public async Task PanelSpawn(ExecuteActionData data)
        {
            var rnd = new Random();
            string color = Arg(data, "color");
            if (string.IsNullOrEmpty(color)) color = TINTS[rnd.Next(TINTS.Length)];
            double x = Math.Round((rnd.NextDouble() * 8 - 4), 2);
            double z = Math.Round((rnd.NextDouble() * 3 - 1.5), 2);
            addItem(Assets.DISC).SetPosition(x, 0, z).SetScale(1.2)
                .AddAttribute("tint", color)
                .AddAttribute("demo", "1")
                .AddAttribute("spawned", "1")
                .AddAction(LiftItem);
            await Task.CompletedTask;
        }

        // Remove everything the panel spawned.
        [GameAction]
        public async Task PanelClear(ExecuteActionData data)
        {
            getItemsByAttribute("spawned").ForEach(it => removeItem(it.Id));
            await Task.CompletedTask;
        }

        // Add a floating text label using text typed into the panel.
        [GameAction]
        public async Task PanelSay(ExecuteActionData data)
        {
            string text = Arg(data, "text");
            if (string.IsNullOrWhiteSpace(text)) return;
            var rnd = new Random();
            addTextItem(Assets.TEXT).SetText(text)
                .SetPosition(Math.Round(rnd.NextDouble() * 6 - 3, 2), 2.2, 0).SetScale(0.5)
                .AddAttribute("textColor", "ffd166")
                .AddAttribute("spawned", "1");
            await Task.CompletedTask;
        }

        // 6) Start looping music (a SOUND item with PlayType "LOOP"). Drop any prior copy first.
        [GameAction]
        public async Task PlayMusic(ExecuteActionData data)
        {
            getItemsByAsset(Assets.MUSIC).ForEach(s => removeItem(s.Id));
            playSound(Assets.MUSIC, "LOOP");
            await Task.CompletedTask;
        }

        // 6) Stop the looping music by removing the sound item.
        [GameAction]
        public async Task StopMusic(ExecuteActionData data)
        {
            getItemsByAsset(Assets.MUSIC).ForEach(s => removeItem(s.Id));
            await Task.CompletedTask;
        }

        // 2) End the demo "game": flag it over and record who clicked as the winner. The base's
        //    end-of-action check then ends the game and the client shows the game-over overlay.
        [GameAction]
        public async Task EndDemo(ExecuteActionData data)
        {
            GameData.Attributes["over"] = "1";
            GameData.Attributes["winnerId"] = data.Player?.Id ?? "";
            GameData.Attributes["result"] = (data.Player != null ? PlayerDisplayName(data.Player) : "Someone")
                                            + " ended the demo.";
            await Task.CompletedTask;
        }
    }
}
