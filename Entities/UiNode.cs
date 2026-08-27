using System.Collections.Generic;

namespace MG.Server.Entities
{
    // =====================================================================================
    // SERVER-DRIVEN UI.  The client is a DUMB renderer: the server describes the ENTIRE panel
    // (every text, image, button, layout container, input) as a tree of UiNode, per seat, and the
    // client just draws it and sends button actions back. No game logic — not even which button to
    // show — lives in the client. Swap the client framework tomorrow and only the (generic) renderer
    // is rewritten; the games never change.
    //
    // A node's meaning comes from Type. Common fields are shared (Text/Color/Size/Style/Url) and
    // interactive nodes carry Action + Args (an [GameAction] name + a key/value bag). Containers
    // ("row"/"col") hold Children so the SERVER controls layout and ordering.
    //
    // Node types the generic renderer understands (MgPanel3d.build, Client/src/app/bl/mg.panel3d.ts):
    //   "panel"  - one panel             (Children; Style = right|left|top|bottom for a screen
    //                                     dock, or Anchor="world" + At/Rot/WorldWidth to stand it
    //                                     in the scene where everyone can see it; see below)
    //   "col"    - vertical container    (Children, Size=gap, Bg)
    //   "row"    - horizontal container  (Children, Size=gap, Bg) — wraps
    //   "title"  - big heading           (Text, Icon, Color, Size)
    //   "text"   - a line of text        (Text, Color, Size, Bg, Style: pill|chip)
    //   "note"   - muted hint            (Text, Color, Size)
    //   "image"  - a picture             (Url, Size=height px, Overlays)
    //   "model"  - a 3D model shown as a picture (Url, Size=height px)
    //   "item3d" - a REAL 3D item standing in the panel (Item, Size=slot height px) — geometry,
    //                                     not a thumbnail; scrolls and clips with the panel
    //   "button" - a button              (Text, Icon=named icon, Color=label/icon ink,
    //                                     Size=label px, Bg=fill, Url=optional picture or model
    //                                     to use INSTEAD of a named icon, Action, Args, Confirm,
    //                                     Gather, Style: fill keyword, "ghost" for outlined,
    //                                     optional "big")
    //   "check"  - one checkbox          (Text, Action, ArgKey, Checked) — sends "1"/"0"
    //   "select" - a choice list         (Id, Options, Action, ArgKey, OnChange, Args).
    //                                     Renders as radio buttons up to 5 options and as a
    //                                     dropdown beyond that; Style "list"/"dropdown" forces one.
    //                                     An Option with an EMPTY Value is the placeholder caption.
    //   "checks" - checkbox group + submit (Options, Need, Action, ArgKey, Text=submit label)
    //   "animpick" - pick an animation clip off a loaded model (Id = the item id, Action, ArgKey)
    //   "banner" - big highlighted result (Text, Color, Size, Style: win|lose)
    //   "log"    - multi-line block      (Text, Color, Size, Bg)
    //   "space"  - vertical gap          (Size)
    // Anything unknown renders as plain text, so adding a game can't crash the client.
    //
    // Style keywords that pick a FILL colour (button / banner / chip): ok, no, primary, team,
    // win, lose, cur. Anything else falls through to a neutral fill, so the server can invent a
    // keyword without breaking the client. ("s" and "f" were CSS classes on the old HTML panel
    // and mean nothing now — removed rather than resurrected, because no game uses them.)
    //
    // BUTTON WEIGHT: a fill keyword = a solid call-to-action button; "ghost" = an outlined,
    // secondary one; no keyword = a neutral solid. Reach for "ghost" on the safe/cancel option so
    // a row of buttons has an obvious primary.
    //
    // ICONS: set Icon to a name ("play", "dice", "coins", ...). The older convention — an emoji at
    // the START of a label, which the client swaps for a vector icon and strips from the text —
    // still works, so existing games are unaffected.
    // =====================================================================================
    public class UiNode
    {
        public string Type { get; set; } = "text";
        public string? Text { get; set; }
        public string? Color { get; set; }   // text colour, hex without '#', e.g. "ffd166"
        public string? Bg { get; set; }       // background colour, hex without '#' (col/row/text/button)
        public double? Size { get; set; }     // px (text/image) or gap (containers)
        public string? Style { get; set; }    // free-form keyword(s): ok|no|big|primary|pill|s|f|cur|win|lose|team
        public string? Url { get; set; }       // image src / button icon PICTURE (or model to thumbnail)

        /// <summary>
        /// A named vector icon, e.g. "play", "dice", "coins" — see ICON_BY_NAME in
        /// Client/src/app/bl/mg.panel3d.ts for the list. Prefer this over the older convention of
        /// putting an emoji at the start of the label: it is explicit, it survives the label being
        /// reworded, and an unknown name simply renders no icon.
        ///
        /// This names an INTENT, not a picture file — the client decides what "play" looks like,
        /// exactly like the PrimeNG icon names in GameCatalog. Use Url instead for a game's own
        /// artwork on a button.
        /// </summary>
        public string? Icon { get; set; }
        public string? Action { get; set; }    // [GameAction] name for interactive nodes
        public Dictionary<string, string>? Args { get; set; }
        public string? ArgKey { get; set; }     // for "checks"/"select"/"check": which arg key the value goes into
        public int? Need { get; set; }          // for "checks": exact number that must be selected
        public List<UiOption>? Options { get; set; }
        public List<UiNode>? Children { get; set; }

        public string? Id { get; set; }          // for "select": the field id a button can gather
        public bool OnChange { get; set; }        // for "select"/"check": dispatch Action immediately on change
        public bool Checked { get; set; }         // for "check"
        public string? Confirm { get; set; }      // for "button": window.confirm(text) before dispatching
        public List<string>? Gather { get; set; } // for "button": select ids to read into args[id]

        // for "image": small images layered ON TOP of the base image (markers/badges), positioned
        // in PERCENT of the base image so they stay glued to the right spot at any render size.
        public List<UiOverlay>? Overlays { get; set; }

        /// <summary>
        /// For Type == "item3d": a REAL 3D item standing in the panel, as opposed to "model" which
        /// renders a model into a flat thumbnail picture. The item is described exactly like a board
        /// item (asset + rotation + attributes), but it belongs to the PANEL: it is laid out by the
        /// panel's flexbox like any other node, and it scrolls and clips with it.
        ///
        /// This is what lets a hand of cards be a panel instead of a `player.Hand` zone — and the
        /// card-back rule comes free, because the renderer already draws the BACK face to anyone who
        /// is not the item's "owner" (see the TOKEN branch of mg.game.ts).
        ///
        /// Asset types honoured in a panel: TOKEN (a card/tile) and OBJECT (a model). Anything else
        /// renders as an empty slot rather than throwing.
        /// </summary>
        public ItemData? Item { get; set; }

        // ---- panel placement (for Type == "panel") ---------------------------------------
        /// <summary>
        /// WHERE this panel lives. "screen" (default) pins it to an edge of the viewer's own view,
        /// exactly as before. "world" puts it at a fixed spot in the scene: it does NOT follow the
        /// camera, so the player can orbit around it — and it is the only anchor another player can
        /// possibly see, because a screen-docked panel exists only in its owner's view space.
        ///
        /// The player may override their OWN panels' anchor from the UI (placement has always been
        /// the client's business — see the note above); the server names the default and supplies
        /// the world transform.
        /// </summary>
        public string? Anchor { get; set; }        // "screen" (default) | "world"

        /// <summary>
        /// WHO may see this panel. "own" (default) = only the seat it belongs to. "public" = every
        /// viewer of the game, which is what lets a hand of cards sit in front of a player where
        /// the table can see it (the owner sees faces, everyone else sees backs — the game decides
        /// that per item, see PlayerData.Screen redaction).
        ///
        /// Only meaningful together with Anchor="world": there is no way to show one player's
        /// screen-space HUD to anybody else, so the client ignores "public" on a screen panel.
        /// </summary>
        public string? Visibility { get; set; }    // "own" (default) | "public"

        /// <summary>World position for Anchor="world". Ignored otherwise.</summary>
        public V3? At { get; set; }

        /// <summary>World rotation in DEGREES for Anchor="world" (e.g. face the table centre).</summary>
        public V3? Rot { get; set; }

        /// <summary>
        /// How wide the panel should be in WORLD UNITS when Anchor="world". A screen panel derives
        /// its size from the view (so it reads the same on every monitor); a world panel cannot —
        /// it has a physical size on the table, and only the game knows what that should be next
        /// to its own board. Defaults to a sensible hand-sized panel if omitted.
        /// </summary>
        public double? WorldWidth { get; set; }

        // ---- panels -------------------------------------------------------------------
        // A seat's Screen can be split into SEVERAL panels, each pinned to an edge of the player's
        // view: "right" | "left" | "top" | "bottom" (anything else falls back to right). Panels
        // sharing an edge stack along it, in the order the game lists them.
        //
        // This is deliberately just another node type, so nothing else changes: a Screen with no
        // Panel node is drawn as ONE panel docked right, which is what every existing game already
        // gets. A game opts in only when it wants the split.
        //
        // Where a panel physically ends up is the CLIENT's business (it docks to the edges of the
        // view on screen, and rides the player's hand in VR). The server only names the edge.
        //
        // A Panel node may sit anywhere in the tree, not just at the top level — the renderer
        // hoists it out of whatever container holds it. (It used to inspect only the top level,
        // so a nested Panel rendered as one empty line and its whole subtree vanished.)
        public static UiNode Panel(string dock, params UiNode[] kids) =>
            new UiNode { Type = "panel", Style = dock, Children = new(kids) };

        /// <summary>
        /// A panel that lives IN THE SCENE instead of on the viewer's screen: it stays where it is
        /// put while the camera orbits, and — unlike a screen-docked panel — other players can see
        /// it, so it is how a game gives a seat a visible presence on the table (a hand of cards in
        /// front of a player, a shared scoreboard, a rules card beside the board).
        ///
        /// <param name="at">Where, in world units.</param>
        /// <param name="rot">Facing, in degrees; null = unrotated (flat, facing +Z).</param>
        /// <param name="worldWidth">Physical width in world units. Pick it against your own board.</param>
        /// <param name="visibility">"public" (the point of a world panel) or "own" to keep it private.</param>
        /// </summary>
        public static UiNode PanelAt(V3 at, V3? rot = null, double worldWidth = 1.2,
                                     string visibility = "public", params UiNode[] kids)
            => new UiNode
            {
                Type = "panel",
                Anchor = "world",
                Visibility = visibility,
                At = at,
                Rot = rot,
                WorldWidth = worldWidth,
                Children = new(kids),
            };

        // ---- tiny fluent helpers so game flows read cleanly ----
        public static UiNode Col(params UiNode[] kids) => new UiNode { Type = "col", Children = new(kids) };
        public static UiNode Row(params UiNode[] kids) => new UiNode { Type = "row", Children = new(kids) };
        public static UiNode Title(string t, string? icon = null)
            => new UiNode { Type = "title", Text = t, Icon = icon };
        public static UiNode Text_(string t, string? color = null, double? size = null, string? style = null)
            => new UiNode { Type = "text", Text = t, Color = color, Size = size, Style = style };
        public static UiNode Note(string t) => new UiNode { Type = "note", Text = t };
        public static UiNode Image(string url, double? h = null, string? style = null)
            => new UiNode { Type = "image", Url = url, Size = h, Style = style };
        public static UiNode Button(string text, string action, Dictionary<string, string>? args = null,
                                    string? url = null, string? style = null, string? confirm = null,
                                    List<string>? gather = null, string? icon = null)
            => new UiNode { Type = "button", Text = text, Action = action, Args = args, Url = url, Style = style, Confirm = confirm, Gather = gather, Icon = icon };
        public static UiNode Banner(string text, string style) => new UiNode { Type = "banner", Text = text, Style = style };
        public static UiNode Log(string text) => new UiNode { Type = "log", Text = text };
        public static UiNode Space(double px = 8) => new UiNode { Type = "space", Size = px };
        // A model rendered as a picture tile (the client turns the model URL into a thumbnail).
        public static UiNode Model(string url, double? h = null, string? style = null)
            => new UiNode { Type = "model", Url = url, Size = h, Style = style };

        /// <summary>
        /// A REAL 3D item inside the panel — actual geometry, not a thumbnail. Use it for a hand of
        /// cards, a token the player is holding, a die they are about to roll.
        /// <param name="item">The item, described exactly like a board item. Give it an "owner"
        /// attribute (a seat id) and a TOKEN asset with a BackURL to get faces-for-me /
        /// backs-for-everyone-else for free.</param>
        /// <param name="slotPx">Height of the panel slot it sits in, in px. The item is scaled to it.</param>
        /// </summary>
        public static UiNode Item3d(ItemData item, double? slotPx = null, string? style = null,
                                    string? action = null, Dictionary<string, string>? args = null)
            => new UiNode { Type = "item3d", Item = item, Size = slotPx, Style = style, Action = action, Args = args };
        // A dropdown. If onChange, it dispatches Action with args[argKey]=selectedValue immediately;
        // otherwise a Button with Gather=[id] reads its value.
        public static UiNode Select(string id, List<UiOption> options, string? action = null, string? argKey = null,
                                    bool onChange = false, Dictionary<string, string>? args = null)
            => new UiNode { Type = "select", Id = id, Options = options, Action = action, ArgKey = argKey, OnChange = onChange, Args = args };
        // A single checkbox that dispatches Action with args[argKey]="1"/"0" on toggle.
        public static UiNode Check(string text, string action, string argKey, bool chk, Dictionary<string, string>? args = null)
            => new UiNode { Type = "check", Text = text, Action = action, ArgKey = argKey, Checked = chk, OnChange = true, Args = args };

        // A small coloured chip (e.g. a gem count). bg = fill, color = text.
        public static UiNode Pill(string text, string bg, string color = "ffffff")
            => new UiNode { Type = "text", Text = text, Bg = bg, Color = color, Style = "pill" };

        // A compact rounded tag that hugs its text (unlike Pill it doesn't stretch to fill the
        // row) — for dense info like vote records ("✔ Misha") or tiny labels ("M1").
        public static UiNode Chip(string text, string bg, string color = "ffffff")
            => new UiNode { Type = "text", Text = text, Bg = bg, Color = color, Style = "chip" };

        public UiNode With(List<UiNode> kids) { Children = kids; return this; }
        public UiNode SetBg(string hex) { Bg = hex; return this; }
        public UiNode SetStyle(string s) { Style = s; return this; }

        // Layer a marker image on top of an "image" node. x/y = CENTER of the marker, w = its
        // width — all in percent of the base image, so the marker tracks the same map spot at
        // any panel size. E.g. Resistance stamps the current mission + past results on the map.
        public UiNode WithOverlay(string url, double xPct, double yPct, double wPct)
        {
            (Overlays ??= new()).Add(new UiOverlay { Url = url, X = xPct, Y = yPct, W = wPct });
            return this;
        }
    }

    // A positioned marker on top of a UiNode image (see UiNode.WithOverlay).
    public class UiOverlay
    {
        public string Url { get; set; } = "";
        public double X { get; set; }   // centre, % of base image width
        public double Y { get; set; }   // centre, % of base image height
        public double W { get; set; }   // width, % of base image width
    }

    public class UiOption
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Checked { get; set; }    // for "checks" (multi-select group)
        public bool Selected { get; set; }   // for "select" (current value of a dropdown)
        public UiOption() { }
        public UiOption(string label, string value) { Label = label; Value = value; }
    }
}
