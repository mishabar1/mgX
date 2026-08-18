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
    // Node types the generic renderer understands:
    //   "col"    - vertical container   (Children, Style, Size=gap)
    //   "row"    - horizontal container (Children, Style, Size=gap)
    //   "title"  - big heading          (Text)
    //   "text"   - a line of text       (Text, Color=hex, Size=px, Style)
    //   "note"   - muted/italic hint    (Text)
    //   "image"  - an <img>             (Url, Size=height px, Style)
    //   "button" - a button            (Text, Url=optional icon, Action, Args, Style: ok|no|big|primary)
    //   "checks" - checkbox group + submit (Options, Need, Action, ArgKey, Text=submit label)
    //   "banner" - big highlighted result (Text, Style: win|lose)
    //   "log"    - monospace multi-line block (Text)
    //   "space"  - vertical gap         (Size)
    // Anything unknown renders as plain text, so adding a game can't crash the client.
    // =====================================================================================
    public class UiNode
    {
        public string Type { get; set; } = "text";
        public string? Text { get; set; }
        public string? Color { get; set; }   // text colour, hex without '#', e.g. "ffd166"
        public string? Bg { get; set; }       // background colour, hex without '#' (col/row/text/button)
        public double? Size { get; set; }     // px (text/image) or gap (containers)
        public string? Style { get; set; }    // free-form keyword(s): ok|no|big|primary|pill|s|f|cur|win|lose|team
        public string? Url { get; set; }       // image src / button icon
        public string? Action { get; set; }    // [GameAction] name for interactive nodes
        public Dictionary<string, string>? Args { get; set; }
        public string? ArgKey { get; set; }     // for "checks"/"select"/"check": which arg key the value goes into
        public int? Need { get; set; }          // for "checks": exact number that must be selected
        public List<UiOption>? Options { get; set; }
        public List<UiNode>? Children { get; set; }

        public string? Id { get; set; }          // for "input"/"select": the field id a button can gather
        public string? Placeholder { get; set; } // for "input"
        public bool OnChange { get; set; }        // for "select"/"check": dispatch Action immediately on change
        public bool Checked { get; set; }         // for "check"
        public string? Confirm { get; set; }      // for "button": window.confirm(text) before dispatching
        public List<string>? Gather { get; set; } // for "button": input/select ids to read into args[id]

        // for "image": small images layered ON TOP of the base image (markers/badges), positioned
        // in PERCENT of the base image so they stay glued to the right spot at any render size.
        public List<UiOverlay>? Overlays { get; set; }

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
        public static UiNode Panel(string dock, params UiNode[] kids) =>
            new UiNode { Type = "panel", Style = dock, Children = new(kids) };

        // ---- tiny fluent helpers so game flows read cleanly ----
        public static UiNode Col(params UiNode[] kids) => new UiNode { Type = "col", Children = new(kids) };
        public static UiNode Row(params UiNode[] kids) => new UiNode { Type = "row", Children = new(kids) };
        public static UiNode Title(string t) => new UiNode { Type = "title", Text = t };
        public static UiNode Text_(string t, string? color = null, double? size = null, string? style = null)
            => new UiNode { Type = "text", Text = t, Color = color, Size = size, Style = style };
        public static UiNode Note(string t) => new UiNode { Type = "note", Text = t };
        public static UiNode Image(string url, double? h = null, string? style = null)
            => new UiNode { Type = "image", Url = url, Size = h, Style = style };
        public static UiNode Button(string text, string action, Dictionary<string, string>? args = null,
                                    string? url = null, string? style = null, string? confirm = null, List<string>? gather = null)
            => new UiNode { Type = "button", Text = text, Action = action, Args = args, Url = url, Style = style, Confirm = confirm, Gather = gather };
        public static UiNode Banner(string text, string style) => new UiNode { Type = "banner", Text = text, Style = style };
        public static UiNode Log(string text) => new UiNode { Type = "log", Text = text };
        public static UiNode Space(double px = 8) => new UiNode { Type = "space", Size = px };
        // A model rendered as a picture tile (the client turns the model URL into a thumbnail).
        public static UiNode Model(string url, double? h = null, string? style = null)
            => new UiNode { Type = "model", Url = url, Size = h, Style = style };
        public static UiNode Input(string id, string? placeholder = null)
            => new UiNode { Type = "input", Id = id, Placeholder = placeholder };
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
