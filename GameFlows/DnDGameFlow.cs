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
        private static readonly (string label, string url)[] MONSTERS =
        {
            ("Skeleton", "dnd/skeleton.stl"), ("Knight", "dnd/death_knight.stl"),
            ("Angel", "dnd/angel.stl"), ("Flytrap", "dnd/flytrap.glb"), ("Rover", "dnd/rover.glb"),
        };
        private static readonly string[] CHAR_COLORS =
        { "0xE03131", "0x1971C2", "0x2F9E44", "0xF08C00", "0x9C36B5", "0x0CA678" };

        private const double BOARD = 16; // scene plane size (world units), so the grid spans ±8

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

            // Seat 0 = DM, looking straight down for a full overview. The DM owns the near (-z)
            // edge of the table — the control panel lives there. Players sit around the FAR
            // half of the ring (angles 90..270), so no one sits under the DM's panel.
            new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                .AddAttribute("type", "dm")
                .SetCameraPosition(0, 24, 1)
                .SetAvatarPosition(0, 3, -17); // the DM's side, just behind the panel

            for (int i = 0; i < 6; i++)
            {
                double deg = 90 + i * 36.0; // 90..270 → right side, around the far edge, to left side
                double t = deg * Math.PI / 180.0;
                int cx = (int)Math.Round(15 * Math.Sin(t));
                int cz = (int)Math.Round(-15 * Math.Cos(t));
                new PlayerData(this.GameData) { Type = PlayerTypeEnum.EMPTY_SEAT }
                    .AddAttribute("type", "p" + (i + 1))
                    .SetCameraPosition(cx, 12, cz)
                    .SetAvatarPosition((int)Math.Round(11 * Math.Sin(t)), 2, (int)Math.Round(-11 * Math.Cos(t)));
            }

            return Task.CompletedTask;
        }

        protected override Task Setup() => Task.CompletedTask;

        protected override Task StartGame()
        {
            GameData.Attributes["die"] = "20"; // (used in Stage 2)
            BuildControlPanel();
            return Task.CompletedTask;
        }

        protected override Task EndGame() => Task.CompletedTask;
        protected override Task<bool> IsEndGame() => Task.FromResult(false); // DM ends the session manually
        protected override List<PlayerData> GetGameWinners() => new List<PlayerData>();

        // ============================ DM control panel ============================
        private string? DmId() => getPlayerByAttribute("type", "dm")?.Id;

        private void BuildControlPanel()
        {
            string? dm = DmId();
            if (dm == null) return;

            foreach (var p in getItemsByAttribute("panel")) removeItem(p.Id);

            // Panel rows stacked just above the board (tight enough to all stay in the DM's
            // top-down view). Scenes farthest, dice/roll nearest the board.
            var players = GameData.Players
                .Where(p => p.Type != PlayerTypeEnum.EMPTY_SEAT && p.GetStringAttribute("type") != "dm")
                .ToList();

            LayoutRow(SCENES.Length, -14.3, (i, x) =>
                AddButton(SCENES[i].label, x, -14.3, dm, nameof(LoadScene), "sceneUrl", SCENES[i].url));

            LayoutRow(MONSTERS.Length, -13.1, (i, x) =>
                AddButton(MONSTERS[i].label, x, -13.1, dm, nameof(AddMonster), "monsterUrl", MONSTERS[i].url));

            LayoutRow(players.Count, -11.9, (i, x) =>
                AddButton("Place: " + PlayerDisplayName(players[i]), x, -11.9, dm, nameof(PlaceCharacter), "seat", players[i].Id));

            // Die selector (active one highlighted green).
            string die = GameData.Attributes.TryGetValue("die", out var dv) ? dv : "20";
            AddButton("d6", -1.3, -10.7, dm, nameof(SetDie), "sides", "6", die == "6" ? "0x2F9E44" : null);
            AddButton("d20", 1.3, -10.7, dm, nameof(SetDie), "sides", "20", die == "20" ? "0x2F9E44" : null);

            // "Ask to roll" — one per seated player; uses the selected die.
            LayoutRow(players.Count, -9.5, (i, x) =>
                AddButton("Roll: " + PlayerDisplayName(players[i]), x, -9.5, dm, nameof(AskRoll), "seat", players[i].Id));
        }

        // Evenly space `count` items across the board width at depth z, calling place(i, x).
        private void LayoutRow(int count, double z, Action<int, double> place)
        {
            if (count <= 0) return;
            double span = BOARD - 2;
            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? 0 : -span / 2 + span * i / (count - 1);
                place(i, x);
            }
        }

        private void AddButton(string label, double x, double z, string dmId, string action, string attrKey, string attrVal, string? tint = null)
        {
            var plate = addItem(Assets.BUTTON)
                .SetPosition(x, 0.05, z).SetScale(1.9, 1, 1.1)
                .AddAttribute("panel", "1")
                .AddAttribute(attrKey, attrVal);
            if (tint != null) plate.AddAttribute("tint", tint); // highlight (e.g. active die)
            plate.ClickActions[dmId] = action;
            plate.Visible[dmId] = true;

            var text = addTextItem(Assets.TEXT).SetText(label)
                .SetPosition(x, 0.14, z).SetScale(0.32).SetRotation(-90, 0, 0)
                .AddAttribute("panel", "1")
                .AddAttribute("textColor", "ffffff");
            text.Visible[dmId] = true;
        }

        // ============================ DM actions ============================
        [GameAction]
        public async Task LoadScene(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string url = data.Item!.GetStringAttribute("sceneUrl");
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
            string url = data.Item!.GetStringAttribute("monsterUrl");
            // Asset scale (1.6) sizes the normalized model; leave item scale at 1.
            var m = addItem(MonsterAsset(url)).SetPosition(0, 0.1, 0).AddAttribute("monster", "1");
            m.AddAction(data.Player!.Id, SelectPiece); // DM can select & move it
            await Task.CompletedTask;
        }

        [GameAction]
        public async Task PlaceCharacter(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string seat = data.Item!.GetStringAttribute("seat");
            var owner = GameData.Players.Find(p => p.Id == seat);
            if (owner == null) { await Task.CompletedTask; return; }

            int idx = GameData.Players.Where(p => p.GetStringAttribute("type") != "dm").ToList().FindIndex(p => p.Id == seat);
            string color = CHAR_COLORS[Math.Max(0, idx) % CHAR_COLORS.Length];

            var pawn = addItem(Assets.PAWN).SetPosition(0, 0.15, 0).SetScale(0.9)
                .AddAttribute("char", "1").AddAttribute("owner", seat).AddAttribute("tint", color);
            pawn.AddAction(data.Player!.Id, SelectPiece);
            await Task.CompletedTask;
        }

        // ============================ dice (d6 / d20) ============================
        private readonly Random _rnd = new Random();

        [GameAction]
        public async Task SetDie(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            GameData.Attributes["die"] = data.Item!.GetStringAttribute("sides");
            BuildControlPanel(); // refresh the highlight
            await Task.CompletedTask;
        }

        // DM asks a player to roll: a die appears that only that player can click.
        [GameAction]
        public async Task AskRoll(ExecuteActionData data)
        {
            if (!IsDm(data)) { await Task.CompletedTask; return; }
            string seat = data.Item!.GetStringAttribute("seat");
            int sides = (GameData.Attributes.TryGetValue("die", out var d) ? d : "20") == "6" ? 6 : 20;

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
            addTextItem(Assets.TEXT).SetText(who + " rolled d" + sides + " → " + roll)
                .SetPosition(0, 0.3, 9).SetScale(0.6).SetRotation(-90, 0, 0)
                .AddAttribute("rollResult", "1").AddAttribute("textColor", "ffd166");
            await Task.CompletedTask;
        }

        private void RemoveDieFor(string seat)
        {
            foreach (var d in getItemsByAttribute("dieItem").Where(x => x.GetStringAttribute("owner") == seat).ToList())
                removeItem(d.Id);
        }

        // ============================ helpers ============================
        private bool IsDm(ExecuteActionData data) => data.Player != null && data.Player.Id == DmId();

        private AssetData SceneAsset(string url) => addAsset(new TokenAssetData(url, url));
        private AssetData MonsterAsset(string url) => addAsset(new ObjectAssetData(url) { Scale = new V3(1.6) });
    }
}
