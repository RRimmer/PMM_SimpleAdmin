using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using MenuManager;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

// Admin panel (pmm_admin.xml). Server -> client: dialog variables on #sa-root + classes per player.
// Ids and class names: tools/gen_layout.py.
// Every screen (a SimpleAdmin menu, the settings, the key test) is a list of Row; Draw shows it
// in the player's style: Scroll (wheel, no numbers), Pages (no numbers) or Keys (1-7, 8 back, 9 next, 0 close).
public partial class Plugin : IPmmMenuPainter
{
    private const int Rows = 64;        // pool of pmm_admin.xml
    private const int Visible = 8;      // Scroll: rows shown before the list scrolls
    private const int PageSize = 7;     // Pages / Keys: rows per page (keys 1-7)
    private const string RootId = "sa-root";
    private const string ListId = "sa-list";

    // Must match tools/gen_layout.py ICONS
    private static readonly string[] IconNames =
    {
        "teamplayer", "settings", "shield", "star", "key", "leader", "shoppingcart", "random", "gift", "kill",
        "clock", "warning", "lock", "refresh", "trophy", "crosshair", "health", "fast", "parachute", "dollar_sign",
        "coin_stack", "bullet", "inventory", "timer", "check", "clantag", "sound_3", "loadout", "colorwheel",
    };
    private static readonly HashSet<string> IconSet = new(IconNames, StringComparer.OrdinalIgnoreCase);

    // "(ON) [PMM_StoreCore 0.1.0]" - SimpleAdmin's plugin manager entries.
    private static readonly Regex PluginRx = new(@"^\((ON|OFF)\)\s*\[(.*)\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // SimpleAdmin's "force team" menu (ru / en titles) and its entries.
    private static readonly HashSet<string> TeamMenus = new(StringComparer.OrdinalIgnoreCase) { "Принудить к команде", "Force Team" };

    private enum View
    {
        Menu,
        Settings,
        Calibrate,
    }

    private sealed class Row
    {
        public string Label = "";
        public string Hint = "";
        public string Pill = "";
        public bool PillOn;
        public bool Danger;
        public bool Dim;
        public bool Disabled;
        public bool Arrow;
        public string Team = "";
        public string Icon = "";
        public Action<CCSPlayerController>? Click;
    }

    private sealed class Step
    {
        public string Title = "";
        public IMenu? Menu;
    }

    private sealed class Slot
    {
        public bool Open;
        public int Token;
        public bool Captured;
        public View View = View.Menu;
        public IMenu? Menu;
        public Action<CCSPlayerController>? Back;
        public Action<CCSPlayerController>? ResetAction;
        public int Page;
        public List<Row> Rows = new();                                     // what is on the panel now
        public readonly List<Step> Trail = new();                          // menus from the root to the open one
        public readonly Dictionary<string, int> Pages = new(StringComparer.Ordinal);
        public string? ClickKey;                                           // last clicked entry (for the "›" memory)
        public float ClickTime = -100f;                                    // last click / key on the panel
        public readonly bool[] Keys = new bool[10];                        // key test: keys 1..9, 0
        public float KeyTime;                                              // key test: last key
        public int KeyToken;
        public readonly Dictionary<string, bool> Cls = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Vars = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> OneHot = new(StringComparer.Ordinal);

        public void Forget()
        {
            Cls.Clear();
            Vars.Clear();
            OneHot.Clear();
            Captured = false;
        }

        public void Reset()
        {
            Open = false;
            Token++;
            KeyToken++;
            View = View.Menu;
            Menu = null;
            Back = null;
            ResetAction = null;
            Page = 0;
            Rows = new();
            Trail.Clear();
            Pages.Clear();
            ClickKey = null;
            ClickTime = -100f;
        }
    }

    private sealed class PlayerRef
    {
        public CCSPlayerController? Player;
    }

    private readonly Slot[] _slots = Enumerable.Range(0, 65).Select(_ => new Slot()).ToArray();
    private static readonly PluginCapability<IPmmPaintRegistry?> PaintCapability = new("pmm:paint");
    private IPmmPaintRegistry? _registry;
    private CCSCustomHudLayout? _layout;
    private bool _layoutWarned;

    // "title|entry" -> the entry opened another SimpleAdmin menu (true) or did something at once (false).
    private readonly Dictionary<string, bool> _arrows = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<ChatMenuOption, PlayerRef> _optionPlayers = new();

    private static readonly Regex TagRx = new(@"\{[A-Za-z_]+\}|<[^>]{1,40}>", RegexOptions.Compiled);

    // ---------------- lifecycle ----------------

    private void PanelLoad()
    {
        RegisterListener<Listeners.OnCustomHudClicked>(OnClick);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _layout = null;
            foreach (var s in _slots)
            {
                s.Reset();
                s.Forget();
            }
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            if (slot is < 0 or >= 65) return;
            _slots[slot].Reset();
        });
        // Reconnect into the same slot keeps the previous player's per-player state: hide it again.
        RegisterListener<Listeners.OnClientPutInServer>(slot =>
        {
            if (slot is < 0 or >= 65) return;
            _slots[slot].Forget();
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p != null && p.IsValid && _layout is { IsValid: true })
            {
                Cls(p, RootId, "sa-hidden", true);
                _layout.SetInputCaptureEnabled(p, false);
            }
        });
        // A round restart may remove the entity: forget open panels so MenuManager does not think they are shown.
        AddTimer(0.5f, () =>
        {
            if (_layout is { IsValid: true }) return;
            if (_layout != null) _layout = null;
            foreach (var s in _slots)
                if (s.Open)
                {
                    s.Reset();
                    s.Forget();
                }
        }, TimerFlags.REPEAT);

        SettingsLoad();
    }

    private void RegisterPainter()
    {
        if (_registry != null) return;
        try { _registry = PaintCapability.Get(); } catch { _registry = null; }
        if (_registry == null)
        {
            Logger.LogWarning("MenuManager has no pmm:paint (PMM core 1.2.04+ is needed): SimpleAdmin keeps the usual PMM menus");
            return;
        }
        _registry.Add(this);
        Logger.LogInformation("Admin panel ready ({Layout}), sources: {Src}", Config.LayoutPath, string.Join(", ", Config.SourceAssemblies));
    }

    private void UnregisterPainter()
    {
        try { _registry?.Remove(this); } catch { }
        _registry = null;
    }

    // ---------------- IPmmMenuPainter ----------------

    // PMM calls this for every mouse-panorama menu. true = we draw it, PMM draws nothing.
    public bool TryPaint(CCSPlayerController player, IMenu menu)
    {
        if (!player.IsValid || player.IsBot || player.Slot is < 0 or >= 65) return false;
        var s = _slots[player.Slot];

        bool ours = false;
        bool tagged = false;
        try
        {
            tagged = IsSourceMenu(menu);
            ours = Config.Enabled && (tagged || OpenedBySource());
        }
        catch (Exception e) { Logger.LogError(e, "Admin panel: source check failed"); }
        if (_debug) Logger.LogInformation("[debug] TryPaint \"{Title}\" tagged={Tagged} ours={Ours} open={Open}", menu.Title, tagged, ours, s.Open);

        if (!ours)
        {
            // Another PMM menu replaces the admin panel.
            if (s.Open) Hide(player, keepTrail: false);
            return false;
        }

        if (!EnsureLayout())
        {
            if (_debug) Logger.LogInformation("[debug]   custom_hud_layout {Path} could not be created", Config.LayoutPath);
            if (!_layoutWarned) Logger.LogWarning("custom_hud_layout {Path} was not created, SimpleAdmin keeps the usual PMM menu", Config.LayoutPath);
            _layoutWarned = true;
            if (s.Open) Hide(player, keepTrail: false);
            return false;
        }

        bool fromClick = Server.CurrentTime - s.ClickTime < 1.0f;

        // The click before this menu opened a submenu: remember the "›" for that entry.
        if (s.ClickKey != null && fromClick)
        {
            _arrows[s.ClickKey] = true;
            s.ClickKey = null;
        }

        string title = menu.Title ?? "";
        var back = Get(menu, "BackAction") as Action<CCSPlayerController>;

        // Path for the crumb line and for "Back". A title already on the path = the admin went back.
        // A menu without SimpleAdmin's "back" starts a new path, unless it was opened by a click on the panel
        // (plugin manager, custom commands ...): then it is a child and "Back" reopens the previous menu.
        int at = s.Trail.FindLastIndex(x => x.Title == title);
        bool returning = at >= 0;
        if (at >= 0)
        {
            s.Trail.RemoveRange(at + 1, s.Trail.Count - at - 1);
            s.Trail[at].Menu = menu;
        }
        else if (back == null && !(s.Open && fromClick))
        {
            s.Trail.Clear();
            s.Trail.Add(new Step { Title = title, Menu = menu });
        }
        else s.Trail.Add(new Step { Title = title, Menu = menu });
        if (s.Trail.Count > 12) s.Trail.RemoveAt(0);

        s.View = View.Menu;
        s.KeyToken++;
        s.Menu = menu;
        s.Back = back;
        s.ResetAction = Get(menu, "ResetAction") as Action<CCSPlayerController>;
        s.Page = returning ? s.Pages.GetValueOrDefault(title) : 0;

        bool wasOpen = s.Open;
        s.Open = true;
        int token = ++s.Token;

        if (wasOpen)
        {
            Draw(player);
            ApplyCapture(player);
            return true;
        }

        // Never draw in the frame of the chat command (!admin): CS2 then kicks with NETWORK_DISCONNECT.
        Later(Config.OpenDelay, () =>
        {
            if (s.Token != token || !s.Open || !player.IsValid) return;
            Draw(player);
            Later(Config.InputDelay, () =>
            {
                if (s.Token == token && s.Open && player.IsValid) ApplyCapture(player);
            });
        });
        return true;
    }

    public void Close(CCSPlayerController player)
    {
        if (player.IsValid && player.Slot is >= 0 and < 65 && _slots[player.Slot].Open) Hide(player, keepTrail: false);
    }

    public bool IsOpen(CCSPlayerController player)
        => player.IsValid && player.Slot is >= 0 and < 65 && _slots[player.Slot].Open;

    // ---------------- hide / layout / mouse ----------------

    private void Hide(CCSPlayerController player, bool keepTrail = true)
    {
        if (player.Slot is < 0 or >= 65) return;
        var s = _slots[player.Slot];
        s.Open = false;
        s.Token++;
        s.KeyToken++;
        s.Menu = null;
        s.View = View.Menu;
        if (!keepTrail) s.Trail.Clear();
        if (!player.IsValid || _layout is not { IsValid: true }) return;
        Capture(player, false);
        Cls(player, RootId, "sa-hidden", true);
    }

    private bool EnsureLayout()
    {
        if (_layout is { IsValid: true }) return true;
        _layout = null;
        var created = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
        if (created == null || created.Handle == IntPtr.Zero) return false;
        created.StrLayout = Config.LayoutPath;
        created.DispatchSpawn();
        if (!created.IsValid) return false;
        _layout = created;
        _layoutWarned = false;
        foreach (var s in _slots) s.Forget();
        return true;
    }

    private void Capture(CCSPlayerController player, bool on)
    {
        var s = _slots[player.Slot];
        if (_layout is not { IsValid: true } || !player.IsValid) return;
        if (s.Captured == on) return;
        s.Captured = on;
        _layout.SetInputCaptureEnabled(player, on);
    }

    // Mouse styles take the mouse. The number keys style and the key test leave it to the game,
    // so the keys reach the player's binds (css_1 ... css_0).
    private void ApplyCapture(CCSPlayerController player)
    {
        if (!player.IsValid || player.Slot is < 0 or >= 65) return;
        var s = _slots[player.Slot];
        bool want = s.Open && s.View != View.Calibrate && StyleOf(player) != MenuStyle.Keys;
        Capture(player, want);
    }

    // ---------------- input ----------------

    private void OnClick(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (_layout == null || layout.Handle != _layout.Handle || !player.IsValid || player.Slot is < 0 or >= 65) return;
        var s = _slots[player.Slot];
        if (!s.Open) return;

        try
        {
            switch (buttonId)
            {
                case "sa-close":
                    Hide(player, keepTrail: false);
                    return;
                case "sa-back":
                    GoBack(player, s);
                    return;
                case "sa-next":
                    NextPage(player, s);
                    return;
            }
            if (!TryIndex(buttonId, "sa-r-", out int row) || row >= Rows) return;
            ActivateRow(player, s, s.Page * PerPage(player, s) + row);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Admin panel click {Button} failed", buttonId);
        }
    }

    private int PerPage(CCSPlayerController player, Slot s)
        => s.View != View.Calibrate && StyleOf(player) == MenuStyle.Scroll ? Rows : PageSize;

    private void ActivateRow(CCSPlayerController player, Slot s, int index)
    {
        if (index < 0 || index >= s.Rows.Count) return;
        var row = s.Rows[index];
        if (row.Disabled || row.Click == null) return;
        s.ClickTime = Server.CurrentTime;
        row.Click(player);
    }

    private void NextPage(CCSPlayerController player, Slot s)
    {
        int per = PerPage(player, s);
        int pages = Math.Max(1, (s.Rows.Count + per - 1) / per);
        if (s.Page >= pages - 1) return;
        s.Page++;
        Draw(player);
    }

    private void GoBack(CCSPlayerController player, Slot s)
    {
        s.ClickTime = Server.CurrentTime;
        s.ClickKey = null;

        if (s.View == View.Calibrate)
        {
            OpenSettings(player, s);
            return;
        }
        if (s.Page > 0 && PerPage(player, s) == PageSize)
        {
            s.Page--;
            Draw(player);
            return;
        }
        if (s.View == View.Settings)
        {
            s.View = View.Menu;
            s.Page = s.Menu != null ? s.Pages.GetValueOrDefault(s.Menu.Title ?? "") : 0;
            Draw(player);
            ApplyCapture(player);
            return;
        }
        if (s.Back != null)
        {
            var back = s.Back;
            back(player);      // SimpleAdmin opens the parent menu -> TryPaint
            return;
        }
        // No SimpleAdmin "back" (plugin manager, custom commands ...): reopen the previous menu.
        if (s.Trail.Count > 1 && s.Trail[^2].Menu is { } prev)
        {
            prev.Open(player);
            return;
        }
        Hide(player, keepTrail: false);
    }

    private bool CanGoBack(Slot s) => s.View != View.Menu || s.Back != null || s.Trail.Count > 1;

    private void ClickOption(CCSPlayerController player, Slot s, IMenu menu, ChatMenuOption option)
    {
        string key = ArrowKey(menu.Title ?? "", option);
        s.ClickKey = key;
        s.Pages[menu.Title ?? ""] = s.Page;

        if (menu.PostSelectAction != PostSelectAction.Nothing) Hide(player);

        option.OnSelect?.Invoke(player, option);

        // Nothing opened during the callback: the entry acts at once (no "›").
        if (s.ClickKey == key)
        {
            if (!_arrows.ContainsKey(key)) _arrows[key] = false;
        }

        if (menu.PostSelectAction == PostSelectAction.Reset && s.ResetAction is { } reset)
            Server.NextFrame(() => { if (player.IsValid) reset(player); });
        else if (s.Open && s.View == View.Menu && ReferenceEquals(s.Menu, menu))
            Draw(player);      // the same menu stays: plugin states etc. may have changed
    }

    // ---------------- draw ----------------

    private void Draw(CCSPlayerController player)
    {
        var s = _slots[player.Slot];
        if (!s.Open || _layout is not { IsValid: true }) return;
        if (s.View == View.Menu && s.Menu == null) return;

        try
        {
            var culture = AdminTranslations.Culture(player);
            string crumb, title, sub;
            switch (s.View)
            {
                case View.Settings:
                    s.Rows = SettingsRows(player, culture, s);
                    crumb = T(culture, "home") + " ›";
                    title = T(culture, "settings_title");
                    sub = Fmt(T(culture, "style_now"), StyleName(culture, StyleOf(player)));
                    break;
                case View.Calibrate:
                    s.Rows = KeyTestRows(player, culture, s);
                    crumb = T(culture, "home") + " › " + T(culture, "settings") + " ›";
                    title = T(culture, "keys_title");
                    int got = s.Keys.Count(k => k);
                    string left = string.Join(" ", Enumerable.Range(0, 10).Where(k => !s.Keys[k]).Select(k => ((k + 1) % 10).ToString(CultureInfo.InvariantCulture)));
                    sub = got == 10 ? T(culture, "keys_done") : Fmt(T(culture, "keys_progress"), got, left);
                    break;
                default:
                {
                    var menu = s.Menu!;
                    var (head, target) = SplitTitle(Clean(menu.Title));
                    bool root = s.Back == null && s.Trail.Count <= 1;
                    s.Rows = MenuRows(player, culture, s, menu, head, target, root);
                    crumb = Crumb(culture, s);
                    title = Heading(culture, head);
                    sub = root && Config.ShowServerLine ? ServerLine(culture, player)
                        : target != "" ? Fmt(T(culture, "target"), target)
                        : "";
                    break;
                }
            }

            // keys test: number keys look (1-7 / 8 / 9 / 0) whatever the player's style
            var style = s.View == View.Calibrate ? MenuStyle.Keys : StyleOf(player);
            int count = s.Rows.Count;
            int per = style == MenuStyle.Scroll ? Rows : PageSize;
            int pages = Math.Max(1, (count + per - 1) / per);
            s.Page = Math.Clamp(s.Page, 0, pages - 1);
            int start = s.Page * per;
            int shown = Math.Max(0, Math.Min(per, count - start));

            Var(player, RootId, "sa_brand", T(culture, "brand"));
            Var(player, RootId, "sa_count", style == MenuStyle.Scroll
                ? (count > Visible ? Fmt(T(culture, "count"), count) : "")
                : (pages > 1 ? Fmt(T(culture, "page"), s.Page + 1, pages) : ""));
            Var(player, RootId, "sa_crumb", crumb);
            Var(player, RootId, "sa_title", title);
            Var(player, RootId, "sa_sub", sub);
            Var(player, RootId, "sa_back", T(culture, "back"));
            Var(player, RootId, "sa_next", T(culture, "next"));
            Var(player, RootId, "sa_close", T(culture, "close"));
            Cls(player, RootId, "has-sub", sub != "");
            Cls(player, RootId, "has-next", style != MenuStyle.Scroll && s.Page < pages - 1);
            Cls(player, RootId, "no-back", !(style != MenuStyle.Scroll && s.Page > 0) && !CanGoBack(s));
            Cls(player, RootId, "view-cal", s.View == View.Calibrate);

            OneHot(player, RootId, "mode", style switch { MenuStyle.Pages => "mode-pages", MenuStyle.Keys => "mode-keys", _ => "mode-scroll" });
            string pos = Config.Position.ToLowerInvariant() switch { "center" => "pos-center", "right" => "pos-right", _ => "pos-left" };
            OneHot(player, RootId, "pos", pos);
            string acc = Config.Accent.ToLowerInvariant() switch { "bright" => "acc-bright", "dark" => "acc-dark", "coral" => "acc-coral", _ => "acc-red" };
            OneHot(player, RootId, "acc", acc);
            OneHot(player, ListId, "h", style == MenuStyle.Scroll && count > Visible ? "h-scroll" : "h-" + Math.Clamp(shown, 1, Visible));

            if (s.View == View.Calibrate)
            {
                int got = s.Keys.Count(k => k);
                for (int k = 0; k < 10; k++) Cls(player, "sa-ck-" + k, "ck-ok", s.Keys[k]);
                OneHot(player, "sa-pfill", "prog", "p-" + got);
                Cls(player, RootId, "cal-done", got == 10);
            }

            for (int i = 0; i < Rows; i++)
            {
                string rowId = "sa-r-" + i;
                if (i >= shown)
                {
                    Cls(player, rowId, "slot-off", true);
                    continue;
                }
                RenderRow(player, i, s.Rows[start + i]);
                Cls(player, rowId, "slot-off", false);
            }
            if (count > Rows && _debug) Logger.LogInformation("[debug] \"{Title}\" has {Count} entries, the panel shows the first {Rows}", title, count, Rows);

            Cls(player, RootId, "sa-hidden", false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Admin panel draw failed");
            Hide(player, keepTrail: false);
        }
    }

    private void RenderRow(CCSPlayerController player, int i, Row row)
    {
        string rowId = "sa-r-" + i;
        Var(player, RootId, "sa_l" + i, row.Label);
        Var(player, RootId, "sa_h" + i, row.Hint);
        Var(player, RootId, "sa_p" + i, row.Pill);
        Cls(player, rowId, "r-hint", row.Hint != "");
        Cls(player, rowId, "r-pill", row.Pill != "");
        Cls(player, rowId, "pill-on", row.PillOn);
        Cls(player, rowId, "r-danger", row.Danger);
        Cls(player, rowId, "r-dim", row.Dim);
        Cls(player, rowId, "r-dis", row.Disabled);
        Cls(player, rowId, "r-arrow", row.Arrow && !row.Disabled);
        Cls(player, rowId, "r-team", row.Team != "");
        OneHot(player, rowId, "team", row.Team);
        Cls(player, rowId, "r-icon", row.Icon != "");
        if (row.Icon != "") OneHot(player, "sa-ic-" + i, "icon", row.Icon);
    }

    // ---------------- rows of a SimpleAdmin menu ----------------

    private List<Row> MenuRows(CCSPlayerController viewer, CultureInfo culture, Slot s, IMenu menu, string head, string target, bool root)
    {
        var map = Server.MapName ?? "";
        bool teamMenu = TeamMenus.Contains(head);
        string targetTeam = target != "" ? TeamOfName(target) : "";
        bool main = root && IsMainMenu(viewer, menu);
        var counts = main ? CategoryCounts(viewer) : null;
        if (counts != null && counts.Count != menu.MenuOptions.Count) counts = null;

        var rows = new List<Row>(menu.MenuOptions.Count + 1);
        for (int i = 0; i < menu.MenuOptions.Count; i++)
        {
            var option = menu.MenuOptions[i];
            var row = OptionRow(viewer, culture, menu, option, map, teamMenu, targetTeam);
            if (counts != null && row.Hint == "") row.Hint = counts[i].ToString(CultureInfo.InvariantCulture);
            var o = option;
            row.Click = p => ClickOption(p, s, menu, o);
            rows.Add(row);
        }

        // Main menu: the panel's own settings (menu style, number keys test).
        if (main && Config.ShowSettings)
        {
            rows.Add(new Row
            {
                Label = T(culture, "settings"),
                Hint = StyleName(culture, StyleOf(viewer)),
                Icon = IconSet.Contains(Config.SettingsIcon) ? "ic-" + Config.SettingsIcon.ToLowerInvariant() : "",
                Arrow = true,
                Click = p => OpenSettings(p, s),
            });
        }
        return rows;
    }

    private Row OptionRow(CCSPlayerController viewer, CultureInfo culture, IMenu menu, ChatMenuOption option, string map, bool teamMenu, string targetTeam)
    {
        string raw = Clean(option.Text);
        // Entries of the integration sections carry their hint after HintSep ("Ivan · Gold · до 12.11.26").
        string extra = "";
        int sep = raw.IndexOf(HintSep, StringComparison.Ordinal);
        if (sep > 0)
        {
            extra = raw[(sep + HintSep.Length)..].Trim();
            raw = raw[..sep].Trim();
        }
        Config.Items.TryGetValue(raw, out var style);

        var row = new Row
        {
            Label = Rename(culture, raw),
            Hint = (_tr?.GetExact(culture, raw + "|hint") ?? "").Replace("{map}", map),
            Danger = style?.Danger == true,
            Disabled = option.Disabled,
            Icon = style != null && IconSet.Contains(style.Icon) ? "ic-" + style.Icon.ToLowerInvariant() : "",
        };

        var target = OptionPlayer(option, viewer, raw);
        var plugin = PluginRx.Match(raw);
        if (target != null)
        {
            row.Label = raw;
            row.Hint = Config.ShowUserId && target.UserId is { } uid ? "#" + uid : "";
            row.Team = TeamClass(target);
            row.Icon = "";
        }
        else if (plugin.Success)
        {
            row.Label = plugin.Groups[2].Value.Trim();
            row.PillOn = plugin.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase);
            row.Pill = T(culture, row.PillOn ? "on" : "off");
            row.Hint = "";
        }
        else if (teamMenu && TeamOfOption(raw) is { } optionTeam && optionTeam != "")
        {
            row.Team = optionTeam;
            if (optionTeam == targetTeam)
            {
                row.Hint = T(culture, "now");
                row.Dim = true;
            }
        }
        else if (raw.EndsWith(" (WS)", StringComparison.Ordinal))
        {
            row.Label = raw[..^5];
            row.Hint = T(culture, "workshop");
            if (row.Label.Equals(map, StringComparison.OrdinalIgnoreCase))
            {
                row.Hint = T(culture, "now");
                row.Dim = true;
            }
        }
        else if (map != "" && raw.Equals(map, StringComparison.OrdinalIgnoreCase))
        {
            row.Hint = T(culture, "now");
            row.Dim = true;
        }

        if (extra != "") row.Hint = target != null && row.Hint != "" ? extra + " · " + row.Hint : extra;

        string key = ArrowKey(menu.Title ?? "", option);
        row.Arrow = row.Pill == "" && (_arrows.TryGetValue(key, out bool learned) ? learned : style?.Arrow == true);
        return row;
    }

    private static string TeamClass(CCSPlayerController p) => (CsTeam)p.TeamNum switch
    {
        CsTeam.CounterTerrorist => "tm-ct",
        CsTeam.Terrorist => "tm-t",
        _ => "tm-s",
    };

    // Entries of SimpleAdmin's force-team menu (sa_team_ct / sa_team_t / sa_team_spec, ru and en).
    private static string? TeamOfOption(string raw) => raw switch
    {
        "CT" => "tm-ct",
        "T" => "tm-t",
        "Спец" or "Spec" or "SPEC" => "tm-s",
        _ => null,
    };

    // Current team of the player named in the title ("Принудить к команде: Мишаня").
    private static string TeamOfName(string name)
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid) continue;
            string n = p.PlayerName ?? "";
            string cut = n.Length > 26 ? n[..26] : n;
            if (n == name || cut == name) return TeamClass(p);
        }
        return "";
    }

    // ---------------- texts ----------------

    private string T(CultureInfo culture, string key)
        => _tr?.Get(culture, "pmm_sa." + key) ?? AdminDefaults.Text(culture, "pmm_sa." + key);

    // New name from the translations file, only in the player's own language.
    private string Rename(CultureInfo culture, string raw)
    {
        string? v = _tr?.GetExact(culture, raw);
        return string.IsNullOrEmpty(v) ? raw : v;
    }

    // "Забанить Продолжительность: hellraiser" -> ("Забанить Продолжительность", "hellraiser").
    private static (string head, string target) SplitTitle(string title)
    {
        int colon = title.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0) return (title, "");
        return (title[..colon].Trim(), title[(colon + 2)..].Trim());
    }

    // Big title: an exact rename, else a rename of the last word ("Забанить Продолжительность" -> "Срок").
    private string Heading(CultureInfo culture, string head)
    {
        string? v = _tr?.GetExact(culture, head);
        if (!string.IsNullOrEmpty(v)) return v;
        int space = head.LastIndexOf(' ');
        if (space > 0)
        {
            v = _tr?.GetExact(culture, head[(space + 1)..]);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return head;
    }

    // Short name for the crumb line: "<text>|crumb", else the big title.
    private string CrumbStep(CultureInfo culture, string title)
    {
        var (head, target) = SplitTitle(Clean(title));
        string? v = _tr?.GetExact(culture, head + "|crumb");
        string step = string.IsNullOrEmpty(v) ? Heading(culture, head) : v;
        return target != "" ? step + " › " + target : step;
    }

    // "Главное › Игроки ›" - at most two steps before the open menu.
    private string Crumb(CultureInfo culture, Slot s)
    {
        if (s.Trail.Count <= 1) return Clean(s.Menu?.Title ?? "");
        var steps = new List<string>();
        for (int i = 0; i < s.Trail.Count - 1; i++)
            steps.Add(i == 0 ? T(culture, "home") : CrumbStep(culture, s.Trail[i].Title));
        if (steps.Count > 2) steps = steps.Skip(steps.Count - 2).ToList();
        return string.Join(" › ", steps) + " ›";
    }

    private string ServerLine(CultureInfo culture, CCSPlayerController viewer)
    {
        int players = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && !p.IsHLTV);
        string role = AdminManager.PlayerHasPermissions(viewer, "@css/root") ? "@css/root" : "@css/generic";
        return Fmt(T(culture, "server_line"), Server.MapName ?? "", players, Server.MaxPlayers, role);
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return TagRx.Replace(text, "").Replace('\n', ' ').Trim();
    }

    private static string ArrowKey(string title, ChatMenuOption option)
    {
        // "Забанить Продолжительность: hellraiser" -> "Забанить Продолжительность": the same for every target.
        var (head, _) = SplitTitle(Clean(title));
        return head + "\u0001" + Clean(option.Text);
    }

    // ---------------- player rows ----------------

    // SimpleAdmin player lists show the name only. The callback of the entry holds the player it acts on:
    // ChatMenuOption.OnSelect -> SimpleAdmin's MenuOption -> its Action -> closure with the controller.
    private CCSPlayerController? OptionPlayer(ChatMenuOption option, CCSPlayerController viewer, string text)
    {
        if (text == "") return null;
        if (!_optionPlayers.TryGetValue(option, out var found))
        {
            found = new PlayerRef { Player = FindPlayer(option.OnSelect, viewer, text) };
            _optionPlayers.AddOrUpdate(option, found);
        }
        return found.Player is { IsValid: true } p ? p : null;
    }

    private CCSPlayerController? FindPlayer(Delegate? start, CCSPlayerController viewer, string text)
    {
        if (start?.Target == null) return null;
        var found = new List<CCSPlayerController>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object obj, int depth)>();
        queue.Enqueue((start.Target, 0));
        int budget = 64;

        while (queue.Count > 0 && budget-- > 0)
        {
            var (obj, depth) = queue.Dequeue();
            if (!seen.Add(obj)) continue;
            if (obj is CCSPlayerController c)
            {
                found.Add(c);
                continue;
            }
            var type = obj.GetType();
            if (!Walkable(type)) continue;
            foreach (var field in type.GetFields(Any))
            {
                object? value;
                try { value = field.GetValue(obj); }
                catch { continue; }
                switch (value)
                {
                    case null:
                    case string:
                        continue;
                    case CCSPlayerController pc:
                        found.Add(pc);
                        continue;
                    case Delegate d:
                        if (d.Target != null && depth < 4) queue.Enqueue((d.Target, depth + 1));
                        continue;
                }
                if (depth < 4 && !value.GetType().IsValueType) queue.Enqueue((value, depth + 1));
            }
        }

        foreach (var c in found)
        {
            if (!c.IsValid) continue;
            string name = c.PlayerName ?? "";
            string cut = name.Length > 26 ? name[..26] : name;
            if (cut == "") continue;
            if (text == name || text == cut || text.StartsWith(cut, StringComparison.Ordinal)) return c;
        }
        return null;
    }

    // Only compiler closures and types of the menu's source plugin are opened (not the plugin instance, lists ...).
    private bool Walkable(Type type)
    {
        string name = type.Name;
        if (name.Contains("<>", StringComparison.Ordinal) || name.Contains("DisplayClass", StringComparison.Ordinal)) return true;
        string asm = type.Assembly.GetName().Name ?? "";
        if (!Config.SourceAssemblies.Any(s => s.Equals(asm, StringComparison.OrdinalIgnoreCase))) return false;
        // SimpleAdmin's own small types (MenuOption, MenuBuilder); never the plugin itself.
        return !typeof(BasePlugin).IsAssignableFrom(type);
    }

    // ---------------- sends (cached per player) ----------------

    private void Cls(CCSPlayerController player, string panel, string cls, bool on)
    {
        var layout = _layout;
        if (layout is not { IsValid: true } || !player.IsValid) return;
        var s = _slots[player.Slot];
        string key = panel + "\u0001" + cls;
        if (s.Cls.TryGetValue(key, out bool cur) && cur == on) return;
        s.Cls[key] = on;
        layout.SetHasClassForPlayer(player, panel, cls, on);
    }

    private void Var(CCSPlayerController player, string panel, string name, string value)
    {
        var layout = _layout;
        if (layout is not { IsValid: true } || !player.IsValid) return;
        var s = _slots[player.Slot];
        string key = panel + "\u0001" + name;
        if (s.Vars.TryGetValue(key, out var cur) && cur == value) return;
        s.Vars[key] = value;
        // The client looks a value that is one bare word up in its own localization: "Back" came out as
        // "Сзади" (a map callout token) on a Russian client. A trailing no-break space stops the lookup.
        if (value.Length > 0 && value.All(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch == '_'))) value += "\u00A0";
        layout.SetDialogVariableStringForPlayer(player, panel, name, value);
    }

    // One class out of a group (pos-*, acc-*, mode-*, h-*, tm-*, ic-*): the previous one is removed.
    private void OneHot(CCSPlayerController player, string panel, string group, string cls)
    {
        var s = _slots[player.Slot];
        string key = panel + "\u0001" + group;
        if (!s.OneHot.TryGetValue(key, out var prev))
        {
            // First send for this panel: clear the whole group (a reconnect into the slot keeps old classes).
            prev = Authored(group);
            if (GroupClasses(group) is { } all)
                foreach (var other in all)
                    if (other != cls) Cls(player, panel, other, false);
        }
        else if (prev == cls) return;
        if (!string.IsNullOrEmpty(prev) && prev != cls) Cls(player, panel, prev, false);
        if (!string.IsNullOrEmpty(cls)) Cls(player, panel, cls, true);
        s.OneHot[key] = cls;
    }

    private static readonly string[] IconClasses = IconNames.Select(n => "ic-" + n).ToArray();
    private static readonly string[] Progress = Enumerable.Range(0, 11).Select(n => "p-" + n).ToArray();
    private static readonly string[] ListHeights = Enumerable.Range(1, Visible).Select(n => "h-" + n).Append("h-scroll").ToArray();

    private static string[]? GroupClasses(string group) => group switch
    {
        "pos" => new[] { "pos-left", "pos-center", "pos-right" },
        "acc" => new[] { "acc-red", "acc-bright", "acc-dark", "acc-coral" },
        "mode" => new[] { "mode-scroll", "mode-pages", "mode-keys" },
        "team" => new[] { "tm-ct", "tm-t", "tm-s" },
        "icon" => IconClasses,
        "h" => ListHeights,
        "prog" => Progress,
        _ => null,
    };

    private static string Authored(string group) => group switch
    {
        "pos" => "pos-left",
        "acc" => "acc-red",
        "mode" => "mode-scroll",
        "icon" => "ic-star",
        "h" => "h-1",
        "prog" => "p-0",
        _ => "",
    };

    // ---------------- helpers ----------------

    private static bool TryIndex(string id, string prefix, out int index)
    {
        index = -1;
        return id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), out index) && index >= 0;
    }

    private static string Fmt(string format, params object[] args)
    {
        try { return string.Format(format, args); }
        catch (FormatException) { return format; }
    }

    private void Later(float delay, Action action)
    {
        if (delay <= 0f) Server.NextFrame(action);
        else AddTimer(delay, action);
    }
}
