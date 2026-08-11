using System;
using System.Collections.Generic;
using System.Linq;
using MG.Server.Controllers;
using MG.Server.Entities;

namespace MG.Server.GameFlows
{
    // D&D — a DM-driven virtual tabletop (not a win/lose game). Seat 0 is the Dungeon Master
    // (mandatory) + 2..6 players. Players see nothing until the DM acts. The DM sees a floating
    // control panel (visible only to them) to: load a scene (a map shown to everyone), place
    // each player's character, add monsters, and (Stage 2) ask a player to roll a die. The DM
    // can select any character/monster and click the scene to reposition it.
    public class DnDGameFlow : BaseGameFlow
    {
        internal class Assets
        {
            internal static AssetData TEXT = new Text3dAssetData("dnd");
            internal static AssetData BUTTON = new TokenAssetData("common/suits/button_bg.png", "common/suits/button_bg.png");
            internal static AssetData PAWN = new CylinderAssetData("pawn"); // a player's character token
        }

        // Selectable scenes (map images) and monster models.
        private static readonly (string label, string url)[] SCENES =
        {
            ("Scene 1", "dnd/map_1_0.png"), ("Scene 2", "dnd/map_1_1.png"),
            ("Scene 3", "dnd/map_1_2.png"), ("Scene 4", "dnd/map_1_3.png"), ("Scene 5", "dnd/map2.jpg"),
        };
        // Quaternius "Ultimate Monsters" pack (CC0), under Client/src/assets/games/monsters.
        // The glTFs embed their geometry and share Atlas_Monsters.png in the same folder.
        private static readonly (string label, string url)[] MONSTERS =
        {
            ("Dragon",  "monsters/Flying/glTF/Dragon.gltf"),
            ("Demon",   "monsters/Flying/glTF/Demon.gltf"),
            ("Ghost",   "monsters/Flying/glTF/Ghost.gltf"),
            ("Golem",   "monsters/Flying/glTF/Goleling.gltf"),
            ("Ooze",    "monsters/Blob/glTF/GreenBlob.gltf"),
            ("Cactoro", "monsters/Blob/glTF/Cactoro.gltf"),
        };
        private static readonly string[] CHAR_COLORS =
        { "0xE03131", "0x1971C2", "0x2F9E44", "0xF08C00", "0x9C36B5", "0x0CA678" };

        // Quaternius "RPG Character Pack" (CC0), under Client/src/assets/games/heroes.
        // Each seat is fixed to one class; the model is used for the seat avatar AND the token.
        private static readonly (string label, string url)[] HEROES =
        {
            ("Warrior", "heroes/glTF/Warrior.gltf"),
            ("Wizard",  "heroes/glTF/Wizard.gltf"),
            ("Rogue",   "heroes/glTF/Rogue.gltf"),
            ("Cleric",  "heroes/glTF/Cleric.gltf"),
            ("Ranger",  "heroes/glTF/Ranger.gltf"),
            ("Monk",    "heroes/glTF/Monk.gltf"),
        };

        private const double BOARD = 32; // scene plane size (world units) — big enough that hero
                                          // tokens (~2u) match the map's grid squares. Spans ±16.
        private const double TRAY_Z = 19; // the hero tray sits just past the board's near (+z) edge

        public override int MinPlayers => 3; // DM + at least 2 players

        public DnDGameFlow(GameData gameData) : base(gameData)
        {
            gameData.GameType = GameTypeEnum.DND;
        }

        protected override Task Create()
        {
            addAsset(Assets.TEXT);
            addAsset(Assets.BUTTON);
            addAsset(Assets.PAWN);

            GameData.Observer.Position.Set(0, 24, 0);

            // Seat 0 = DM. A close 3/4 "tabletop" angle (not straight-down) so the board fills
            // the view and the console — which hugs the near (+z) edge — sits in the foreground.
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "dm")
                .SetCameraPosition(0, 28, 30)
                .SetAvatarPosition(0, 0, 34); // behind the camera; the DM never sees their own token

            // Player seats are (re)placed evenly across the far edge at StartGame (RespaceDnDSeats)
            // once we know how many actually joined; give them sane defaults meanwhile.
            for (int i = 0; i < 6; i++)
            {
                double deg = -60 + 120.0 * i / 5.0;
                double t = deg * Math.PI / 180.0;
                int cx = (int)Math.Round(30 * Math.Sin(t));
                int cz = (int)Math.Round(-30 * Math.Cos(t));
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .AddAttribute("hero", HEROES[i].label)      // fixed class for this seat
                    .AddAttribute("heroUrl", HEROES[i].url)     // model for avatar + token
                    .SetCameraPosition(cx, 24, cz)
                    .SetAvatarPosition((int)Math.Round(22 * Math.Sin(t)), 2, (int)Math.Round(-22 * Math.Cos(t)));
            }

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            GameData.Attributes["die"] = "20";
            // Publish the catalog so the DM's HTML console can populate its dropdowns ("label|url;…").
            GameData.Attributes["dndScenes"] = string.Join(";", SCENES.Select(s => s.label + "|" + s.url));
            GameData.Attributes["dndMonsters"] = string.Join(";", MONSTERS.Select(m => m.label + "|" + m.url));
            RespaceDnDSeats();
            BuildControlPanel();
            FillHeroTray();
            return Task.CompletedTask;
        }

        // A tray box near the board's near edge that holds every player's hero, ready for the DM
        // to drag onto the map — no need to "place" them from the console. (Room for more later.)
        private void FillHeroTray()
        {
            string? dm = DmId();
            var players = GameData.Players
                .Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT && p.GetStringAttribute("type") != "dm")
                .ToList();

            // The tray mat (visible to everyone — it sits on the table).
            addItem(Assets.BUTTON)
                .SetPosition(0, 0.03, TRAY_Z).SetScale(BOARD, 1, 5)
                .AddAttribute("tray", "1").AddAttribute("tint", "0x0E1420");

            // One hero per slot, evenly spread along the tray; the DM can select & drag each.
            int n = players.Count;
            double span = BOARD - 6;
            for (int i = 0; i < n; i++)
            {
                double x = n == 1 ? 0 : -span / 2 + span * i / (n - 1);
                string heroUrl = players[i].GetStringAttribute("heroUrl");
                ItemData token = string.IsNullOrEmpty(heroUrl)
                    ? addItem(Assets.PAWN).SetScale(1.25).AddAttribute("tint", CHAR_COLORS[i % CHAR_COLORS.Length])
                    : addItem(HeroAsset(heroUrl));
                token.SetPosition(x, 0.15, TRAY_Z)
                    .AddAttribute("char", "1").AddAttribute("owner", players[i].Id);
                if (dm != null) token.ClickActions[dm] = nameof(SelectPiece); // DM drags onto the map
                AddNameLabel(token, PlayerDisplayName(players[i]), "FFFFFF", string.IsNullOrEmpty(heroUrl) ? 1.7 : 3.0);
            }
        }

        // Spread the players who actually joined evenly across the FAR edge (an arc centred on
        // -z), so 2 players don't clump on one side and 6 fan out neatly. The DM keeps its seat.
        private void RespaceDnDSeats()
        {
            var players = GameData.Players
                .Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT && p.GetStringAttribute("type") != "dm")
                .ToList();
            int n = players.Count;
            for (int i = 0; i < n; i++)
            {
                double deg = n == 1 ? 0 : -65 + 130.0 * i / (n - 1);
                double t = deg * Math.PI / 180.0;
                players[i]
                    .SetCameraPosition((int)Math.Round(30 * Math.Sin(t)), 24, (int)Math.Round(-30 * Math.Cos(t)))
                    .SetAvatarPosition((int)Math.Round(22 * Math.Sin(t)), 2, (int)Math.Round(-22 * Math.Cos(t)));
            }
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(false); // DM ends the session manually
        protected override List<PlayerData> GetGameWinners() => new List<PlayerData>();

        // ============================ DM control panel ============================
        private string? DmId() => getPlayerByAttribute("type", "dm")?.Id;

        // ---- panel geometry (world space, on the DM's near edge; players sit on the far half) ----
        private const double ROW_HDR = -10.2;  // header-label column X (left of the buttons)
        private const double ROW_SCENES   = 11.9;
        private const double ROW_MONSTERS = 11.1;
        private const double ROW_PLACE    = 10.3;
        private const double ROW_DICE     = 9.5;
        private const double ROW_ROLL     = 8.7;

        // Group colours (plate tints) so each control band reads at a glance.
        private const string C_SCENES   = "0x1971C2"; // blue
        private const string C_MONSTERS = "0xC0392B"; // red
        private const string C_PLACE    = "0x2F9E44"; // green
        private const string C_DICE     = "0x7048E8"; // purple
        private const string C_ROLL     = "0xE8590C"; // orange
        private const string C_ACTIVE   = "0xFFC300"; // selected die

        private void BuildControlPanel()
        {
            // The DM now uses the CSS3D HTML console (client-side); the in-scene plate console is
            // retired. Kept as a no-op so existing callers (StartGame/SetDie) stay valid.
            return;
#pragma warning disable CS0162 // unreachable code (intentional — legacy 3D console below)
            string? dm = DmId();
            if (dm == null) return;

            foreach (var p in getItemsByAttribute("panel")) removeItem(p.Id);

            var players = GameData.Players
                .Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT && p.GetStringAttribute("type") != "dm")
                .ToList();

            // A dark backing mat frames the whole console so it reads as the DM's control panel
            // rather than buttons floating on the grass.
            addItem(Assets.BUTTON)
                .SetPosition(-1.5, 0.02, (ROW_SCENES + ROW_ROLL) / 2).SetScale(20, 1, 4.6)
                .AddAttribute("panel", "1").AddAttribute("tint", "0x11151F")
                .Visible[dm] = true;

            AddHeader("SCENES", ROW_SCENES, dm);
            LayoutRow(SCENES.Length, (i, x) =>
                AddButton(SCENES[i].label, x, ROW_SCENES, dm, nameof(LoadScene), "sceneUrl", SCENES[i].url, C_SCENES));

            AddHeader("MONSTERS", ROW_MONSTERS, dm);
            LayoutRow(MONSTERS.Length, (i, x) =>
                AddButton(MONSTERS[i].label, x, ROW_MONSTERS, dm, nameof(AddMonster), "monsterUrl", MONSTERS[i].url, C_MONSTERS));

            AddHeader("PLACE", ROW_PLACE, dm);
            LayoutRow(players.Count, (i, x) =>
                AddButton(PlayerDisplayName(players[i]), x, ROW_PLACE, dm, nameof(PlaceCharacter), "seat", players[i].Id, C_PLACE));

            // Die selector (the active die glows).
            string die = GameData.Attributes.TryGetValue("die", out var dv) ? dv : "20";
            AddHeader("DICE", ROW_DICE, dm);
            AddButton("d6", -1.3, ROW_DICE, dm, nameof(SetDie), "sides", "6", die == "6" ? C_ACTIVE : C_DICE);
            AddButton("d20", 1.3, ROW_DICE, dm, nameof(SetDie), "sides", "20", die == "20" ? C_ACTIVE : C_DICE);

            // "Ask to roll" — one per seated player; uses the selected die.
            AddHeader("ROLL", ROW_ROLL, dm);
            LayoutRow(players.Count, (i, x) =>
                AddButton(PlayerDisplayName(players[i]), x, ROW_ROLL, dm, nameof(AskRoll), "seat", players[i].Id, C_ROLL));
        }

        // Evenly space `count` items across the board width, calling place(i, x).
        private void LayoutRow(int count, Action<int, double> place)
        {
            if (count <= 0) return;
            double span = BOARD - 2;
            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? 0 : -span / 2 + span * i / (count - 1);
                place(i, x);
            }
        }

        // A small caption at the left of a row naming the control band.
        private void AddHeader(string label, double z, string dmId)
        {
            var text = addTextItem(Assets.TEXT).SetText(label)
                .SetPosition(ROW_HDR, 0.16, z).SetScale(0.22).SetRotation(-90, 0, 0)
                .AddAttribute("panel", "1").AddAttribute("textColor", "9BB4D4");
            text.Visible[dmId] = true;
        }

        // One panel button: a coloured plate + white caption lying flat on the console mat.
        private void AddButton(string label, double x, double z, string dmId, string action, string attrKey, string attrVal, string? tint = null)
        {
            var plate = addItem(Assets.BUTTON)
                .SetPosition(x, 0.06, z).SetScale(1.7, 1, 0.68)
                .AddAttribute("panel", "1")
                .AddAttribute(attrKey, attrVal);
            if (tint != null) plate.AddAttribute("tint", tint);
            plate.ClickActions[dmId] = action;
            plate.Visible[dmId] = true;

            var text = addTextItem(Assets.TEXT).SetText(label)
                .SetPosition(x, 0.16, z).SetScale(0.30).SetRotation(-90, 0, 0)
                .AddAttribute("panel", "1")
                .AddAttribute("textColor", "ffffff");
            text.Visible[dmId] = true;
        }

        // ============================ DM actions ============================
        [GameAction]
        public async Task LoadScene(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string url = Arg(data, "sceneUrl");
            if (string.IsNullOrEmpty(url)) { await Task.CompletedTask; return; }
            GameData.Attributes["scene"] = url;

            foreach (var s in getItemsByAttribute("scene")) removeItem(s.Id);
            var board = addItem(SceneAsset(url)).SetPosition(0, -0.05, 0).SetScale(BOARD, 1, BOARD).AddAttribute("scene", "1");
            // The scene is the move surface: DM clicks it to drop the selected piece there.
            board.AddAction(data.Player!.Id, MoveHere);
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task AddMonster(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string url = Arg(data, "monsterUrl");
            if (string.IsNullOrEmpty(url)) { await Task.CompletedTask; return; }
            // Stage new monsters in a tidy row along a staging line (not all stacked at 0,0,0),
            // so the DM can drag each onto the map from there.
            int mc = getItemsByAttribute("monster").Count;
            double sx = -12 + 6 * (mc % 5);
            var m = addItem(MonsterAsset(url)).SetPosition(sx, 0.1, 12).AddAttribute("monster", "1");
            m.AddAction(data.Player!.Id, SelectPiece); // DM can select & move it
            AddNameLabel(m, MonsterLabel(url), "FFD9A0");
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task PlaceCharacter(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string seat = Arg(data, "seat");
            var owner = GameData.Players.Find(p => p.Id == seat);
            if (owner == null) { await Task.CompletedTask; return; }

            int idx = GameData.Players.Where(p => p.GetStringAttribute("type") != "dm").ToList().FindIndex(p => p.Id == seat);

            // One character per player: re-placing just resets it to that player's staging slot.
            foreach (var c in getItemsByAttribute("char").Where(c => c.GetStringAttribute("owner") == seat).ToList())
                removeItem(c.Id);

            // The token is the seat's hero model (falls back to a coloured disc if none).
            double sx = -7 + 3.2 * Math.Max(0, idx);
            string heroUrl = owner.GetStringAttribute("heroUrl");
            ItemData token = string.IsNullOrEmpty(heroUrl)
                ? addItem(Assets.PAWN).SetScale(1.25).AddAttribute("tint", CHAR_COLORS[Math.Max(0, idx) % CHAR_COLORS.Length])
                : addItem(HeroAsset(heroUrl));
            token.SetPosition(sx, 0.15, 6.6)
                .AddAttribute("char", "1").AddAttribute("owner", seat);
            token.AddAction(data.Player!.Id, SelectPiece);
            AddNameLabel(token, PlayerDisplayName(owner), "FFFFFF", string.IsNullOrEmpty(heroUrl) ? 1.7 : 3.0);
            await Task.CompletedTask;
        }

        // A floating caption above a token. It's a CHILD of the token, so it follows the token
        // wherever the DM drags it. (Scale is relative to the parent's scale.)
        private void AddNameLabel(ItemData token, string text, string colorHex, double height = 1.7)
        {
            new ItemData(Assets.TEXT.Name, token)
                .SetText(text)
                .SetPosition(0, height, 0).SetScale(0.26).SetRotation(-90, 0, 0)
                .AddAttribute("textColor", colorHex)
                .AddAttribute("label", "1");
        }

        private string MonsterLabel(string url)
        {
            foreach (var (label, u) in MONSTERS) if (u == url) return label;
            return "Monster";
        }

        // ============================ dice (d6 / d20) ============================
        private readonly Random _rnd = new Random();

        [GameAction]
        public async Task SetDie(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            GameData.Attributes["die"] = Arg(data, "sides");
            BuildControlPanel(); // refresh the highlight
            await Task.CompletedTask;
        }

        // DM asks a player to roll: a die appears that only that player can click.
        [GameAction]
        public async Task AskRoll(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string seat = Arg(data, "seat");
            if (string.IsNullOrEmpty(seat)) { await Task.CompletedTask; return; }
            // Die size from the ask-roll dropdown (d4..d100), falling back to the game default.
            int sides = int.TryParse(Arg(data, "sides"), out var sv) && sv > 0 ? sv
                        : (GameData.Attributes.TryGetValue("die", out var d) && d == "6" ? 6 : 20);

            RemoveDieFor(seat); // one pending die per player

            var plate = addItem(Assets.BUTTON)
                .SetPosition(0, 1.5, 0).SetScale(2.2, 1, 1.4)
                .AddAttribute("dieItem", "1").AddAttribute("owner", seat)
                .AddAttribute("sides", sides.ToString()).AddAttribute("tint", "0xF1C40F");
            plate.ClickActions[seat] = nameof(RollDice);
            plate.Visible[seat] = true;

            var text = addTextItem(Assets.TEXT).SetText("ROLL d" + sides)
                .SetPosition(0, 1.62, 0).SetScale(0.4).SetRotation(-90, 0, 0)
                .AddAttribute("dieItem", "1").AddAttribute("owner", seat).AddAttribute("textColor", "000000");
            text.Visible[seat] = true;
            await Task.CompletedTask;
        }

        // The prompted player clicks their die → roll, remove it, show the result to everyone.
        [GameAction]
        public async Task RollDice(ExecuteActionData data)
        {
            var item = data.Item;
            if (item == null || !item.HaveAttribute("dieItem")) { await Task.CompletedTask; return; }
            string seat = item.GetStringAttribute("owner");
            if (data.Player?.Id != seat) { await Task.CompletedTask; return; } // only the prompted player

            int sides = item.GetIntAttribute("sides");
            int roll = _rnd.Next(1, sides + 1);
            RemoveDieFor(seat);

            foreach (var r in getItemsByAttribute("rollResult")) removeItem(r.Id);
            var who = PlayerDisplayName(GameData.Players.Find(p => p.Id == seat));
            // Float the result high above the board centre so everyone sees it (and it clears the
            // console, which now sits at +z). Stays until the next roll.
            addTextItem(Assets.TEXT).SetText(who + " rolled d" + sides + " → " + roll)
                .SetPosition(0, 3.5, 0).SetScale(0.85).SetRotation(-90, 0, 0)
                .AddAttribute("rollResult", "1").AddAttribute("textColor", "ffd166");
            await Task.CompletedTask;
        }

        private void RemoveDieFor(string seat)
        {
            foreach (var d in getItemsByAttribute("dieItem").Where(x => x.GetStringAttribute("owner") == seat).ToList())
                removeItem(d.Id);
        }

        // ============================ selected-item actions (DM) ============================
        // Set the selected model's animation to a specific clip index (-1 = none/static). The DM
        // picks it from a dropdown of the model's clips; the client swaps the playing clip live.
        [GameAction]
        public async Task SetAnim(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            if (GameData.Attributes.TryGetValue("selectedItem", out var id) && !string.IsNullOrEmpty(id))
            {
                var it = GameData.FindItem(id);
                if (it != null && int.TryParse(Arg(data, "idx"), out var idx)) it.AnimationIdx = idx;
            }
            await Task.CompletedTask;
        }

        // Remove the selected item (a placed hero returns to nothing; a monster is deleted).
        [GameAction]
        public async Task RemoveSelected(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            if (GameData.Attributes.TryGetValue("selectedItem", out var id) && !string.IsNullOrEmpty(id))
            {
                removeItem(id);
                ClearSelection();
            }
            await Task.CompletedTask;
        }

        // ============================ helpers ============================
        private bool IsDm(ExecuteActionData data) => data.Player != null && data.Player.Id == DmId();

        // Read an action parameter from the HTML console's args, falling back to a clicked
        // 3D item's attribute (so both UIs work during the transition).
        private static string Arg(ExecuteActionData d, string key)
            => d.args != null && d.args.TryGetValue(key, out var v) ? v : (d.Item?.GetStringAttribute(key) ?? "");

        private AssetData SceneAsset(string url) => addAsset(new TokenAssetData(url, url));
        private AssetData MonsterAsset(string url) => addAsset(new ObjectAssetData(url) { Scale = new V3(2.2) });
        private AssetData HeroAsset(string url) => addAsset(new ObjectAssetData(url) { Scale = new V3(2.0) });
    }
}
