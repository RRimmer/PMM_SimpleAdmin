using System.Collections;
using System.Globalization;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

public enum MenuStyle
{
    Scroll,     // mouse wheel, no numbers (default)
    Pages,      // pages, no numbers
    Keys,       // pages, keys 1-7 pick, 8 back, 9 next, 0 close (needs the css_N binds)
}

// Panel settings (menu style per player), the number keys test and the counts on the main menu.
public partial class Plugin
{
    // spec_usenumberkeys_nobinds 0 lets the number keys run their binds while dead or spectating.
    // The space after ";" inside the bind is required ("css_1; slot1"). Key 0 is slot10. Same line as PMM core 1.2.05.
    private const string BindLine =
        "spec_usenumberkeys_nobinds 0;bind \"1\" \"css_1; slot1\";bind \"2\" \"css_2; slot2\";bind \"3\" \"css_3; slot3\";bind \"4\" \"css_4; slot4\";bind \"5\" \"css_5; slot5\";bind \"6\" \"css_6; slot6\";bind \"7\" \"css_7; slot7\";bind \"8\" \"css_8; slot8\";bind \"9\" \"css_9; slot9\";bind \"0\" \"css_0; slot10\"";

    // Game-thread cache of the saved styles. The database (StyleStore) is only touched on worker threads.
    private readonly Dictionary<ulong, MenuStyle> _styles = new();
    private StyleStore? _store;

    private void SettingsLoad()
    {
        _store = new StyleStore(Logger, ModuleDirectory);
        _store.Start(all => Server.NextFrame(() =>
        {
            foreach (var (id, value) in all)
                if (Enum.TryParse<MenuStyle>(value, true, out var style)) _styles[id] = style;
        }));

        // A player who joins gets his style again from the database (another server on the same MySQL may have changed it).
        RegisterListener<Listeners.OnClientAuthorized>((slot, steamId) =>
        {
            ulong id = steamId.SteamId64;
            if (id == 0) return;
            _store?.LoadOne(id, value => Server.NextFrame(() =>
            {
                if (value != null && Enum.TryParse<MenuStyle>(value, true, out var style)) _styles[id] = style;
            }));
        });

        // css_1 ... css_0 are registered by PMM core (its CSGO menu). A listener sees them without taking them away.
        for (int d = 0; d <= 9; d++)
        {
            int digit = d;
            AddCommandListener($"css_{digit}", (player, _) =>
            {
                try { OnDigit(player, digit); }
                catch (Exception e) { Logger.LogError(e, "Admin panel: key {Digit} failed", digit); }
                return HookResult.Continue;
            });
        }
    }

    // ---------------- style per player ----------------

    private MenuStyle StyleOf(CCSPlayerController player)
    {
        if (player.IsValid && _styles.TryGetValue(player.SteamID, out var style)) return style;
        return Enum.TryParse<MenuStyle>(Config.DefaultStyle, true, out var def) ? def : MenuStyle.Scroll;
    }

    private void SetStyle(CCSPlayerController player, MenuStyle style)
    {
        if (!player.IsValid || player.SteamID == 0) return;
        _styles[player.SteamID] = style;
        _store?.Save(player.SteamID, style.ToString());
    }

    private string StyleName(CultureInfo culture, MenuStyle style) => style switch
    {
        MenuStyle.Pages => T(culture, "style_pages"),
        MenuStyle.Keys => T(culture, "style_keys"),
        _ => T(culture, "style_scroll"),
    };

    // ---------------- settings screen ----------------

    private void OpenSettings(CCSPlayerController player, Slot s)
    {
        s.KeyToken++;
        s.View = View.Settings;
        s.Page = 0;
        Draw(player);
        ApplyCapture(player);
    }

    private List<Row> SettingsRows(CCSPlayerController player, CultureInfo culture, Slot s)
    {
        var current = StyleOf(player);
        Row StyleRow(MenuStyle style, string hintKey, Action<CCSPlayerController> click) => new()
        {
            Label = StyleName(culture, style),
            Hint = current == style ? "" : T(culture, hintKey),
            Pill = current == style ? T(culture, "selected") : "",
            PillOn = current == style,
            Click = click,
        };

        return new List<Row>
        {
            StyleRow(MenuStyle.Scroll, "style_scroll_hint", p => ChooseStyle(p, s, MenuStyle.Scroll)),
            StyleRow(MenuStyle.Pages, "style_pages_hint", p => ChooseStyle(p, s, MenuStyle.Pages)),
            new()
            {
                Label = StyleName(culture, MenuStyle.Keys),
                Hint = current == MenuStyle.Keys ? "" : T(culture, "style_keys_hint"),
                Pill = current == MenuStyle.Keys ? T(culture, "selected") : "",
                PillOn = current == MenuStyle.Keys,
                Arrow = current != MenuStyle.Keys,
                Click = p => StartKeyTest(p, s),
            },
            new()
            {
                Label = T(culture, "keys_test"),
                Arrow = true,
                Click = p => StartKeyTest(p, s),
            },
            new()
            {
                Label = T(culture, "binds"),
                Hint = T(culture, "binds_hint"),
                Click = PrintBinds,
            },
        };
    }

    private void ChooseStyle(CCSPlayerController player, Slot s, MenuStyle style)
    {
        SetStyle(player, style);
        s.Page = 0;
        Draw(player);
        ApplyCapture(player);
    }

    // ---------------- number keys test ----------------

    // The mouse is released so the keys reach the player's binds. The panel shows which of 1..0 the server got.
    // When all ten came in: 1 = turn the number keys menu on, 2 = test again, 8 = back, 0 = close.
    // Without any key for Config.KeyTestTimeout seconds the panel goes back to the settings.
    private void StartKeyTest(CCSPlayerController player, Slot s)
    {
        Array.Clear(s.Keys);
        s.View = View.Calibrate;
        s.Page = 0;
        s.KeyTime = Server.CurrentTime;
        int token = ++s.KeyToken;
        Capture(player, false);
        Draw(player);
        PrintBinds(player);
        WatchKeyTest(player, s, token);
    }

    private void WatchKeyTest(CCSPlayerController player, Slot s, int token)
    {
        float timeout = Math.Max(10f, Config.KeyTestTimeout);
        float left = timeout - (Server.CurrentTime - s.KeyTime);
        AddTimer(Math.Max(1f, left), () =>
        {
            if (!player.IsValid || s.KeyToken != token || !s.Open || s.View != View.Calibrate) return;
            if (Server.CurrentTime - s.KeyTime >= timeout - 0.05f) OpenSettings(player, s);
            else WatchKeyTest(player, s, token);
        });
    }

    private List<Row> KeyTestRows(CCSPlayerController player, CultureInfo culture, Slot s)
    {
        if (s.Keys.All(k => k))
        {
            return new List<Row>
            {
                new() { Label = T(culture, "keys_enable"), Click = p => { SetStyle(p, MenuStyle.Keys); OpenSettings(p, s); } },
                new() { Label = T(culture, "keys_again"), Click = p => StartKeyTest(p, s) },
            };
        }
        return new List<Row>
        {
            new() { Label = T(culture, "keys_help1"), Dim = true },
            new() { Label = T(culture, "keys_help2"), Dim = true },
            new() { Label = Fmt(T(culture, "keys_help3"), (int)Math.Max(10f, Config.KeyTestTimeout)), Dim = true },
        };
    }

    // css_1 ... css_0 from the player's binds.
    private void OnDigit(CCSPlayerController? player, int digit)
    {
        if (player == null || !player.IsValid || player.IsBot || player.Slot is < 0 or >= 65) return;
        var s = _slots[player.Slot];
        if (!s.Open) return;
        int key = digit == 0 ? 9 : digit - 1;       // index in s.Keys: keys 1..9, 0

        if (s.View == View.Calibrate)
        {
            if (!s.Keys.All(k => k))
            {
                s.Keys[key] = true;
                s.KeyTime = Server.CurrentTime;
                Draw(player);
                return;
            }
            // all keys work: the usual number keys from here on
        }
        else if (StyleOf(player) != MenuStyle.Keys) return;

        s.ClickTime = Server.CurrentTime;
        switch (digit)
        {
            case 0:
                Hide(player, keepTrail: false);
                return;
            case 8:
                GoBack(player, s);
                return;
            case 9:
                NextPage(player, s);
                return;
            default:
                ActivateRow(player, s, s.Page * PageSize + digit - 1);
                return;
        }
    }

    private void PrintBinds(CCSPlayerController player)
    {
        var culture = AdminTranslations.Culture(player);
        string chat = T(culture, "binds_chat");
        Server.NextFrame(() =>
        {
            if (!player.IsValid) return;
            player.PrintToChat($" {chat}");
            player.PrintToChat($" {BindLine}");
            player.PrintToConsole(chat);
            player.PrintToConsole(BindLine);
        });
    }

    // ---------------- main menu: is it, and how many menus each category has ----------------

    private Type? _saManagerType;
    private FieldInfo? _saLocalizer;
    private bool _saLooked;

    private void FindSimpleAdminTypes()
    {
        if (_saLooked) return;
        _saLooked = true;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = asm.GetName().Name ?? "";
            if (!Config.SourceAssemblies.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            _saManagerType ??= asm.GetType("CS2_SimpleAdmin.Menus.MenuManager");
            _saLocalizer ??= asm.GetType("CS2_SimpleAdmin.CS2_SimpleAdmin")?.GetField("_localizer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }
    }

    // SimpleAdmin's main menu: no back, first on the path, title = its "sa_title" in the player's language.
    private bool IsMainMenu(CCSPlayerController player, IMenu menu)
    {
        string title = Clean(menu.Title);
        if (title.Equals("SimpleAdmin", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            FindSimpleAdminTypes();
            if (_saLocalizer?.GetValue(null) is IStringLocalizer loc)
            {
                using (new WithTemporaryCulture(player.GetLanguage()))
                    return title == Clean(loc["sa_title"].Value);
            }
        }
        catch (Exception e)
        {
            if (_debug) Logger.LogInformation("[debug] sa_title lookup failed: {Error}", e.Message);
        }
        return false;
    }

    // Same order and the same permission checks as SimpleAdmin's CreateMainMenu / CreateCategoryMenu.
    private List<int>? CategoryCounts(CCSPlayerController player)
    {
        if (!Config.ShowCategoryCounts) return null;
        try
        {
            FindSimpleAdminTypes();
            if (_saManagerType == null) return null;
            var instance = _saManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (_saManagerType.GetMethod("GetMenuCategories")?.Invoke(instance, null) is not IDictionary categories) return null;

            var steamId = new SteamID(player.SteamID);
            var counts = new List<int>();
            foreach (var category in categories.Values)
            {
                if (Get(category, "MenuFactories") is not IDictionary factories || factories.Count <= 0) continue;
                string permission = Get(category, "Permission") as string ?? "";
                if (!AdminManager.PlayerHasPermissions(steamId, permission)) continue;

                var permissions = Get(category, "MenuPermissions") as IDictionary;
                var commands = Get(category, "MenuCommandNames") as IDictionary;
                int count = 0;
                foreach (var menuId in factories.Keys)
                {
                    string? perm = permissions != null && permissions.Contains(menuId) ? permissions[menuId] as string : null;
                    string? cmd = commands != null && commands.Contains(menuId) ? commands[menuId] as string : null;
                    bool allowed;
                    if (!string.IsNullOrEmpty(cmd) && AdminManager.CommandIsOverriden(cmd))
                        allowed = AdminManager.PlayerHasPermissions(steamId, AdminManager.GetPermissionOverrides(cmd));
                    else
                        allowed = string.IsNullOrEmpty(perm) || AdminManager.PlayerHasPermissions(steamId, perm);
                    if (allowed) count++;
                }
                counts.Add(count);
            }
            return counts;
        }
        catch (Exception e)
        {
            if (_debug) Logger.LogInformation("[debug] category counts failed: {Error}", e.Message);
            return null;
        }
    }
}
