using System.Collections;
using System.Globalization;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

// Sections of other plugins in SimpleAdmin's main menu, registered through SimpleAdmin's own API
// ("simpleadmin:api": RegisterMenuCategory / RegisterMenu / CreateMenuWithBack ...). The menus are SimpleAdmin
// MenuBuilders, so the panel draws them like every other SimpleAdmin menu, with permissions, "back" and logs.
//   VIP   - cs2-VIPCore (capability "vipcore:core")
//   Maps  - GG1MapChooser (its map list, pools, votes); !maps and SimpleAdmin "Change map" lead here
//   Store - cs2-store (capability "cs2-store:api")
// No compile-time reference to any of these plugins: everything goes through reflection, so a missing plugin
// only means a missing section.
public partial class Plugin
{
    // "Label<HintSep>hint": the panel shows "hint" grey on the right (Panel.OptionRow); a plain PMM menu shows "Label · hint".
    internal const string HintSep = " · ";

    private const string CatVip = "pmm_vip";
    private const string CatMaps = "pmm_maps";
    private const string CatStore = "pmm_store";
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private SaApi? _sa;
    private ModuleLocalizer? _loc;
    private Action? _readyHandler;
    private readonly List<(string Cat, string Id)> _ourMenus = new();
    private string _integrations = "not started";

    private object? _vip;
    private Type? _vipType;
    private object? _gg;
    private object? _shop;
    private Type? _shopType;
    private SavedMenu? _saChangeMap;

    // ---------------- attach / detach ----------------

    private void IntegrationsAttach()
    {
        IntegrationsDetach();
        var api = Capability("CS2-SimpleAdminApi", "CS2_SimpleAdminApi.ICS2_SimpleAdminApi", "simpleadmin:api", out var type);
        if (api == null || type == null)
        {
            _integrations = "SimpleAdmin API (simpleadmin:api) not found";
            Logger.LogWarning("Integrations: {Why}", _integrations);
            return;
        }
        _sa = new SaApi(api, type);
        _loc ??= new ModuleLocalizer(this);

        // SimpleAdmin fires OnSimpleAdminReady when its server data is loaded (also after a hot reload of SimpleAdmin).
        try
        {
            _readyHandler = () => Server.NextFrame(RegisterIntegrations);
            _sa.Ready(_readyHandler, true);
        }
        catch (Exception e)
        {
            Logger.LogWarning("Integrations: OnSimpleAdminReady not available: {Error}", e.Message);
            _readyHandler = null;
        }
        RegisterIntegrations();
    }

    private void IntegrationsDetach()
    {
        if (_sa == null) return;
        try
        {
            if (_readyHandler != null) _sa.Ready(_readyHandler, false);
        }
        catch { /* SimpleAdmin already gone */ }
        _readyHandler = null;
        UnregisterOurMenus();
        RestoreSaChangeMap();
        _sa = null;
    }

    private void UnregisterOurMenus()
    {
        if (_sa == null) return;
        foreach (var (cat, id) in _ourMenus)
        {
            try { _sa.UnregisterMenu(cat, id); }
            catch { /* ignore */ }
        }
        _ourMenus.Clear();
    }

    private void RegisterIntegrations()
    {
        if (_sa == null) return;
        var notes = new List<string>();
        UnregisterOurMenus();
        Run(notes, "VIP", RegisterVip);
        Run(notes, "Maps", RegisterMaps);
        Run(notes, "Store", RegisterStore);
        _integrations = string.Join("; ", notes);
        Logger.LogInformation("Integrations: {List}", _integrations);
    }

    private void Run(List<string> notes, string name, Func<string> register)
    {
        try { notes.Add(name + ": " + register()); }
        catch (Exception e)
        {
            notes.Add(name + ": error " + (e.InnerException ?? e).Message);
            Logger.LogError(e, "Integrations: {Name} failed", name);
        }
    }

    private void AddCategory(string cat, string nameKey, string permission)
        => _sa!.RegisterCategory(cat, nameKey, string.IsNullOrWhiteSpace(permission) ? "@css/generic" : permission.Trim(), _loc!);

    private void AddMenu(string cat, string id, string nameKey, Dictionary<string, IntegrationMenu>? menus, Func<CCSPlayerController, object> factory)
    {
        IntegrationMenu? m = null;
        if (menus != null && menus.TryGetValue(id, out m) && m is { Enabled: false }) return;
        string? permission = m != null && !string.IsNullOrWhiteSpace(m.Permission) ? m.Permission.Trim() : null;
        _sa!.RegisterMenu(cat, id, nameKey, p => Safe(p, cat, factory), permission, _loc!);
        _ourMenus.Add((cat, id));
    }

    // A failing factory must not break SimpleAdmin's menu: show the error instead.
    private object Safe(CCSPlayerController player, string cat, Func<CCSPlayerController, object> factory)
    {
        try { return factory(player); }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            Logger.LogError(inner, "Integrations: menu of {Cat} failed", cat);
            var c = AdminTranslations.Culture(player);
            var menu = NewMenu(TX(c, "pmm_sa.error_title"), cat, player);
            Option(menu, Fmt(TX(c, "pmm_sa.error"), inner.Message), "", _ => { }, true);
            return menu;
        }
    }

    // ---------------- menu helpers ----------------

    private object NewMenu(string title, string cat, CCSPlayerController player) => _sa!.CreateMenuWithBack(title, cat, player);

    private void Option(object menu, string label, string hint, Action<CCSPlayerController> action, bool disabled = false)
        => _sa!.AddMenuOption(menu, hint == "" ? label : label + HintSep + hint, action, disabled);

    private void Sub(object menu, string label, string hint, Func<CCSPlayerController, object> factory, bool disabled = false)
        => _sa!.AddSubMenu(menu, hint == "" ? label : label + HintSep + hint, factory, disabled);

    private void Empty(object menu, CultureInfo c) => Option(menu, TX(c, "pmm_sa.empty"), "", _ => { }, true);

    private void Reopen(CCSPlayerController admin, Func<CCSPlayerController, object> factory)
    {
        if (_sa == null || !admin.IsValid) return;
        try { _sa.OpenMenu(factory(admin), admin); }
        catch (Exception e) { Logger.LogError(e, "Integrations: reopen failed"); }
    }

    private void Tell(CCSPlayerController admin, string text)
    {
        if (admin.IsValid) admin.PrintToChat($" {ChatColors.Red}[Admin]{ChatColors.Default} {text}");
    }

    private void Log(CCSPlayerController admin, string command)
    {
        try { _sa?.LogCommand(admin, command); }
        catch { /* logging is optional */ }
    }

    // Close the panel (map change, a vote menu opens for everybody).
    private void ClosePanel(CCSPlayerController admin)
    {
        Server.NextFrame(() =>
        {
            if (admin.IsValid && IsOpen(admin)) Hide(admin, keepTrail: false);
        });
    }

    private string TX(CultureInfo c, string key) => _tr?.Get(c, key) ?? AdminDefaults.Text(c, key);

    internal string? Localize(CultureInfo c, string key)
        => _tr?.Get(c, key) ?? (AdminDefaults.Has(key) ? AdminDefaults.Text(c, key) : null);

    private static string Name(CCSPlayerController p)
    {
        string n = p.PlayerName ?? "";
        return n.Length > 26 ? n[..26] : n;
    }

    private static IEnumerable<CCSPlayerController> Humans(bool bots)
        => Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsHLTV && p.Connected == PlayerConnectedState.Connected && (bots || !p.IsBot))
            .OrderBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase);

    private static bool HasPermission(CCSPlayerController player, string flag)
        => string.IsNullOrWhiteSpace(flag) || AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), flag.Trim());

    private static object? Call(object? target, Type? type, string name, params object?[] args)
    {
        if (target == null) return null;
        var m = (type ?? target.GetType()).GetMethods().FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length)
                ?? target.GetType().GetMethods(Any).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length)
                ?? throw new MissingMethodException(target.GetType().Name, name);
        return m.Invoke(target, args);
    }

    private static string Err(Exception e) => (e is TargetInvocationException { InnerException: { } ie } ? ie : e).Message;

    // PluginCapability<T> of a plugin whose API assembly is not referenced: T is found by name.
    internal static object? Capability(string assemblyName, string typeName, string capability, out Type? type)
    {
        type = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != assemblyName) continue;
            var t = asm.GetType(typeName);
            if (t == null) continue;
            try
            {
                var capType = typeof(PluginCapability<>).MakeGenericType(t);
                var cap = Activator.CreateInstance(capType, capability);
                var value = capType.GetMethod("Get")!.Invoke(cap, null);
                if (value == null) continue;
                type = t;
                return value;
            }
            catch
            {
                // no provider in this copy of the API assembly
            }
        }
        return null;
    }

    private string DurationText(CultureInfo c, int minutes)
    {
        if (minutes <= 0) return TX(c, "pmm_sa.t_forever");
        if (minutes % 1440 == 0) return Fmt(TX(c, "pmm_sa.t_day"), minutes / 1440);
        if (minutes % 60 == 0) return Fmt(TX(c, "pmm_sa.t_hour"), minutes / 60);
        return Fmt(TX(c, "pmm_sa.t_min"), minutes);
    }

    // ================= VIP (cs2-VIPCore) =================

    private string RegisterVip()
    {
        var cfg = Config.VipCore;
        _vip = null;
        if (!cfg.Enabled) return "off in the config";
        _vip = Capability("VipCoreApi", "VipCoreApi.IVipCoreApi", "vipcore:core", out _vipType);
        if (_vip == null) return "VIPCore not on the server";

        AddCategory(CatVip, "pmm_sa.cat_vip", cfg.Permission);
        AddMenu(CatVip, "give", "pmm_sa.vip_give", cfg.Menus, p => VipPlayers(p, false));
        AddMenu(CatVip, "remove", "pmm_sa.vip_remove", cfg.Menus, p => VipPlayers(p, true));
        AddMenu(CatVip, "list", "pmm_sa.vip_list", cfg.Menus, VipOnline);
        AddMenu(CatVip, "reload", "pmm_sa.vip_reload", cfg.Menus, VipReloadMenu);
        return $"on (TimeMode {VipTimeMode()})";
    }

    private (bool Vip, string Group, long Expires) VipInfo(CCSPlayerController p)
    {
        try
        {
            if (Call(_vip, _vipType, "IsClientVip", p) is not true) return (false, "", 0);
            string group = Call(_vip, _vipType, "GetClientVipGroup", p) as string ?? "";
            long expires = -1;
            if (Get(Get(_vip, "_vipCore"), "Users") is IDictionary users && users.Contains(p.SteamID))
                expires = Convert.ToInt64(Get(users[p.SteamID], "expires") ?? -1L);
            return (true, group, expires);
        }
        catch
        {
            return (false, "", 0);
        }
    }

    private string VipHint(CultureInfo c, (bool Vip, string Group, long Expires) info)
    {
        if (!info.Vip) return TX(c, "pmm_sa.vip_none");
        if (info.Expires == 0) return info.Group + " · " + TX(c, "pmm_sa.vip_forever");
        if (info.Expires > 0)
            return info.Group + " · " + Fmt(TX(c, "pmm_sa.vip_until"),
                DateTimeOffset.FromUnixTimeSeconds(info.Expires).ToLocalTime().ToString("dd.MM.yy", CultureInfo.InvariantCulture));
        return info.Group;
    }

    // VIPCore's TimeMode (vip_core.json): 0 seconds, 1 minutes, 2 hours, 3 days.
    private int VipTimeMode()
    {
        try { return Convert.ToInt32(Get(Get(Get(_vip, "_vipCore"), "CoreConfig"), "TimeMode") ?? 0); }
        catch { return 0; }
    }

    private int VipUnits(int minutes)
    {
        if (minutes <= 0) return 0;
        return VipTimeMode() switch
        {
            1 => minutes,
            2 => Math.Max(1, (int)Math.Round(minutes / 60.0)),
            3 => Math.Max(1, (int)Math.Round(minutes / 1440.0)),
            _ => minutes * 60,
        };
    }

    private object VipPlayers(CCSPlayerController admin, bool remove)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, remove ? "pmm_sa.vip_remove" : "pmm_sa.vip_give"), CatVip, admin);
        int n = 0;
        foreach (var p in Humans(Config.VipCore.ShowBots))
        {
            var info = VipInfo(p);
            if (remove && !info.Vip) continue;
            var target = p;
            if (remove) Sub(menu, Name(p), VipHint(c, info), a => VipRemoveConfirm(a, target));
            else Sub(menu, Name(p), VipHint(c, info), a => VipGroups(a, target));
            n++;
        }
        if (n == 0) Empty(menu, c);
        return menu;
    }

    private object VipOnline(CCSPlayerController admin)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, "pmm_sa.vip_list"), CatVip, admin);
        int n = 0;
        foreach (var p in Humans(true))
        {
            var info = VipInfo(p);
            if (!info.Vip) continue;
            var target = p;
            Sub(menu, Name(p), VipHint(c, info), a => VipGroups(a, target));
            n++;
        }
        if (n == 0) Option(menu, TX(c, "pmm_sa.vip_nobody"), "", _ => { }, true);
        return menu;
    }

    private object VipGroups(CCSPlayerController admin, CCSPlayerController target)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(Fmt(TX(c, "pmm_sa.vip_group_title"), Name(target)), CatVip, admin);
        string current = target.IsValid ? VipInfo(target).Group : "";
        var groups = Call(_vip, _vipType, "GetVipGroups") as string[] ?? Array.Empty<string>();
        foreach (var g in groups)
        {
            string group = g;
            Sub(menu, group, group == current ? TX(c, "pmm_sa.now") : "", a => VipTimes(a, target, group));
        }
        if (groups.Length == 0) Empty(menu, c);
        return menu;
    }

    private object VipTimes(CCSPlayerController admin, CCSPlayerController target, string group)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(Fmt(TX(c, "pmm_sa.vip_time_title"), Name(target)), CatVip, admin);
        foreach (int minutes in Config.VipCore.Durations.Distinct())
        {
            int m = minutes;
            Option(menu, DurationText(c, m), group, a => VipGive(a, target, group, m));
        }
        if (Config.VipCore.Durations.Count == 0) Empty(menu, c);
        return menu;
    }

    private void VipGive(CCSPlayerController admin, CCSPlayerController target, string group, int minutes)
    {
        var c = AdminTranslations.Culture(admin);
        if (!target.IsValid)
        {
            Tell(admin, TX(c, "pmm_sa.gone"));
            return;
        }
        if (target.AuthorizedSteamID == null)
        {
            Tell(admin, Fmt(TX(c, "pmm_sa.vip_noauth"), Name(target)));
            return;
        }
        ulong sid = target.AuthorizedSteamID.SteamId64;
        int units = VipUnits(minutes);
        try
        {
            if (VipInfo(target).Vip)
            {
                // Existing VIP: new group / term in the database, then VIPCore reloads the player from it.
                Call(_vip, _vipType, "SetClientVip", target, group, units);
                AddTimer(1.5f, () => Server.ExecuteCommand($"css_reload_vip_player {sid}"));
                Log(admin, $"css_vip_updateuser {sid} {group} {units}");
            }
            else
            {
                Call(_vip, _vipType, "GiveClientVip", target, group, units);
                Log(admin, $"css_vip_adduser {sid} {group} {units}");
            }
            Tell(admin, Fmt(TX(c, "pmm_sa.vip_given"), Name(target), group, DurationText(c, minutes)));
        }
        catch (Exception e)
        {
            Tell(admin, Fmt(TX(c, "pmm_sa.error"), Err(e)));
            Logger.LogError(e, "VIP give failed");
        }
        AddTimer(0.3f, () => Reopen(admin, a => VipPlayers(a, false)));
    }

    private object VipRemoveConfirm(CCSPlayerController admin, CCSPlayerController target)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(Fmt(TX(c, "pmm_sa.vip_remove_title"), Name(target)), CatVip, admin);
        Option(menu, TX(c, "pmm_sa.vip_remove"), target.IsValid ? VipInfo(target).Group : "", a =>
        {
            if (!target.IsValid)
            {
                Tell(a, TX(c, "pmm_sa.gone"));
                return;
            }
            try
            {
                ulong sid = target.AuthorizedSteamID?.SteamId64 ?? target.SteamID;
                Call(_vip, _vipType, "RemoveClientVip", target);
                Log(a, $"css_vip_deleteuser {sid}");
                Tell(a, Fmt(TX(c, "pmm_sa.vip_removed"), Name(target)));
            }
            catch (Exception e)
            {
                Tell(a, Fmt(TX(c, "pmm_sa.error"), Err(e)));
            }
            AddTimer(0.3f, () => Reopen(a, x => VipPlayers(x, true)));
        });
        return menu;
    }

    private object VipReloadMenu(CCSPlayerController admin)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, "pmm_sa.vip_reload"), CatVip, admin);
        Option(menu, TX(c, "pmm_sa.vip_reload_do"), "css_vip_reload", a =>
        {
            Server.ExecuteCommand("css_vip_reload");
            Log(a, "css_vip_reload");
            Tell(a, TX(c, "pmm_sa.vip_reloaded"));
        });
        return menu;
    }

    // ================= Maps (GG1MapChooser) =================

    private static object? FindMapChooser()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == "GG1MapChooser");
            var t = asm?.GetType("MapChooser.MapChooser");
            var cap = t?.GetProperty("MCAPICapability", Static)?.GetValue(null);
            var api = cap?.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(cap, null);
            return Get(api, "_mapChooser");
        }
        catch
        {
            return null;
        }
    }

    private string RegisterMaps()
    {
        var cfg = Config.MapChooser;
        _gg = null;
        if (!cfg.Enabled)
        {
            RestoreSaChangeMap();
            return "off in the config";
        }
        _gg = FindMapChooser();
        if (_gg == null)
        {
            RestoreSaChangeMap();
            return "GG1MapChooser not on the server";
        }

        AddCategory(CatMaps, "pmm_sa.cat_maps", cfg.Permission);
        AddMenu(CatMaps, "change", "pmm_sa.maps_change", cfg.Menus, p => MapList(p, CatMaps, false));
        AddMenu(CatMaps, "nextmap", "pmm_sa.maps_next", cfg.Menus, p => MapList(p, CatMaps, true));
        AddMenu(CatMaps, "votes", "pmm_sa.maps_votes", cfg.Menus, MapVotes);
        AddMenu(CatMaps, "pools", "pmm_sa.maps_pools", cfg.Menus, MapPools);

        if (cfg.RedirectSimpleAdminChangeMap) ReplaceSaChangeMap();
        else RestoreSaChangeMap();
        return $"on ({(Get(_gg, "Maps_from_List") as IDictionary)?.Count ?? 0} maps, !maps {(cfg.RedirectMapsCommand ? "->panel" : "GG1")}, SA change map {(_saChangeMap != null ? "->GG1 list" : "SA")})";
    }

    private bool MapVoteRunning() => Get(_gg, "IsVoteInProgress") is true || Get(_gg, "IsPoolVoteInProgress") is true;

    private object MapList(CCSPlayerController admin, string cat, bool next)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, next ? "pmm_sa.maps_next" : "pmm_sa.maps_change"), cat, admin);
        if (_gg == null)
        {
            Empty(menu, c);
            return menu;
        }
        if (!next)
        {
            Option(menu, TX(c, "pmm_sa.maps_random"), TX(c, "pmm_sa.maps_random_hint"), a =>
            {
                if (MapVoteRunning())
                {
                    Tell(a, TX(c, "pmm_sa.maps_vote_busy"));
                    return;
                }
                Server.ExecuteCommand("ggmc_auto_mapchange");
                Log(a, "ggmc_auto_mapchange");
                Tell(a, Fmt(TX(c, "pmm_sa.maps_changing"), TX(c, "pmm_sa.maps_random")));
                ClosePanel(a);
            });
        }

        string current = Server.MapName ?? "";
        var maps = new List<(string Key, string Label, bool Ws)>();
        if (Get(_gg, "Maps_from_List") is IDictionary dict)
            foreach (DictionaryEntry e in dict)
            {
                string key = e.Key as string ?? "";
                if (key == "") continue;
                string display = Get(e.Value, "Display") as string ?? "";
                maps.Add((key, display != "" ? display : key, Get(e.Value, "WS") is true));
            }

        foreach (var (key, label, ws) in maps.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase))
        {
            string k = key, l = label;
            string hint = key.Equals(current, StringComparison.OrdinalIgnoreCase) ? TX(c, "pmm_sa.now")
                : ws ? TX(c, "pmm_sa.workshop")
                : label != key ? key : "";
            Option(menu, label, hint, a =>
            {
                if (next)
                {
                    Server.ExecuteCommand($"setnextmap {k}");
                    Log(a, $"setnextmap {k}");
                    Tell(a, Fmt(TX(c, "pmm_sa.maps_next_set"), l));
                    return;
                }
                if (MapVoteRunning())
                {
                    Tell(a, TX(c, "pmm_sa.maps_vote_busy"));
                    return;
                }
                Server.ExecuteCommand($"ggmap {k}");
                Log(a, $"ggmap {k}");
                Tell(a, Fmt(TX(c, "pmm_sa.maps_changing"), l));
                ClosePanel(a);
            });
        }
        if (maps.Count == 0) Empty(menu, c);
        return menu;
    }

    private object MapVotes(CCSPlayerController admin)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, "pmm_sa.maps_votes"), CatMaps, admin);
        Option(menu, TX(c, "pmm_sa.maps_vote_change"), TX(c, "pmm_sa.maps_vote_change_hint"), a => StartVote(a, c, () => Server.ExecuteCommand("ggmc_mapvote_with_change"), "ggmc_mapvote_with_change"));
        Option(menu, TX(c, "pmm_sa.maps_vote_next"), TX(c, "pmm_sa.maps_vote_next_hint"), a => StartVote(a, c, () => Server.ExecuteCommand("ggmc_mapvote_start"), "ggmc_mapvote_start"));
        Option(menu, TX(c, "pmm_sa.maps_vote_yesno"), TX(c, "pmm_sa.maps_vote_yesno_hint"), a => StartVote(a, c, () => Call(_gg, null, "VotesForChangeMapHandle", a, null), "VotesForChangeMapHandle"));
        return menu;
    }

    private void StartVote(CCSPlayerController admin, CultureInfo c, Action start, string what)
    {
        if (MapVoteRunning())
        {
            Tell(admin, TX(c, "pmm_sa.maps_vote_busy"));
            return;
        }
        ClosePanel(admin);
        try
        {
            start();
            Log(admin, what);
            Tell(admin, TX(c, "pmm_sa.maps_vote_started"));
        }
        catch (Exception e)
        {
            Tell(admin, Fmt(TX(c, "pmm_sa.error"), Err(e)));
            Logger.LogError(e, "GG1MapChooser {What} failed", what);
        }
    }

    private object MapPools(CCSPlayerController admin)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, "pmm_sa.maps_pools"), CatMaps, admin);
        string active = Get(_gg, "ActivePoolName") as string ?? "";
        int n = 0;
        if (Get(_gg, "MapPools") is IDictionary pools)
            foreach (DictionaryEntry e in pools)
            {
                string pool = e.Key as string ?? "";
                if (pool == "") continue;
                int count = (e.Value as IDictionary)?.Count ?? 0;
                string hint = Fmt(TX(c, "pmm_sa.maps_count"), count);
                if (pool.Equals(active, StringComparison.OrdinalIgnoreCase)) hint = TX(c, "pmm_sa.now") + " · " + hint;
                Option(menu, pool, hint, a =>
                {
                    if (MapVoteRunning())
                    {
                        Tell(a, TX(c, "pmm_sa.maps_vote_busy"));
                        return;
                    }
                    try
                    {
                        bool ok = Call(_gg, null, "SetActivePool", pool, true, true) is true;
                        Log(a, $"ggmc pool {pool}");
                        Tell(a, Fmt(TX(c, ok ? "pmm_sa.maps_pool_set" : "pmm_sa.maps_pool_failed"), pool));
                    }
                    catch (Exception ex)
                    {
                        Tell(a, Fmt(TX(c, "pmm_sa.error"), Err(ex)));
                    }
                    AddTimer(0.3f, () => Reopen(a, MapPools));
                });
                n++;
            }
        if (n == 0) Option(menu, TX(c, "pmm_sa.maps_no_pools"), "", _ => { }, true);
        if (n > 1)
            Option(menu, TX(c, "pmm_sa.maps_pool_vote"), "", a => StartVote(a, c, () => Call(_gg, null, "AdminPoolVoteHandle", a, null), "AdminPoolVoteHandle"));
        return menu;
    }

    // Harmony prefix of GG1MapChooser's css_maps (MapMenuCommand). true = handled, GG1's own menu is skipped.
    internal bool OnMapsCommand(CCSPlayerController? player)
    {
        var cfg = Config.MapChooser;
        if (!Config.Enabled || !cfg.Enabled || !cfg.RedirectMapsCommand || _sa == null || _gg == null) return false;
        if (player == null || !player.IsValid || !_ourMenus.Any(m => m.Cat == CatMaps)) return false;
        if (!HasPermission(player, cfg.Permission)) return false;
        return OpenSaCategory(player, CatMaps);
    }

    private bool OpenSaCategory(CCSPlayerController player, string categoryId)
    {
        try
        {
            FindSimpleAdminTypes();
            if (_saManagerType == null) return false;
            var instance = _saManagerType.GetProperty("Instance", Static)?.GetValue(null);
            if (_saManagerType.GetMethod("GetMenuCategories")?.Invoke(instance, null) is not IDictionary categories) return false;
            if (!categories.Contains(categoryId)) return false;
            var builder = _saManagerType.GetMethod("CreateCategoryMenuPublic")?.Invoke(instance, new[] { categories[categoryId], player });
            var open = builder?.GetType().GetMethod("OpenMenu", new[] { typeof(CCSPlayerController) });
            if (open == null) return false;
            open.Invoke(builder, new object[] { player });
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "!maps: SimpleAdmin category {Cat} did not open", categoryId);
            return false;
        }
    }

    // SimpleAdmin "Manage server -> Change map": its factory is swapped for GG1MapChooser's map list
    // and put back on unload or when the option is turned off.
    private sealed class SavedMenu
    {
        public object? Factory;
        public object? Name;
        public object? Permission;
        public bool HadPermission;
        public object? Command;
        public bool HadCommand;
    }

    private object? SaCategory(string id)
    {
        FindSimpleAdminTypes();
        if (_saManagerType == null) return null;
        var instance = _saManagerType.GetProperty("Instance", Static)?.GetValue(null);
        return _saManagerType.GetMethod("GetMenuCategories")?.Invoke(instance, null) is IDictionary categories && categories.Contains(id)
            ? categories[id]
            : null;
    }

    private void ReplaceSaChangeMap()
    {
        if (_sa == null) return;
        var cat = SaCategory("server");
        if (Get(cat, "MenuFactories") is not IDictionary factories || !factories.Contains("changemap")) return;
        var permissions = Get(cat, "MenuPermissions") as IDictionary;
        var commands = Get(cat, "MenuCommandNames") as IDictionary;
        if (_saChangeMap == null)
        {
            _saChangeMap = new SavedMenu
            {
                Factory = factories["changemap"],
                Name = (Get(cat, "MenuNames") as IDictionary)?["changemap"],
                HadPermission = permissions?.Contains("changemap") == true,
                Permission = permissions?.Contains("changemap") == true ? permissions["changemap"] : null,
                HadCommand = commands?.Contains("changemap") == true,
                Command = commands?.Contains("changemap") == true ? commands["changemap"] : null,
            };
        }
        string permission = _saChangeMap.Permission as string ?? "@css/changemap";
        string name = _saChangeMap.Name as string ?? "sa_changemap";
        _sa.RegisterMenuPlain("server", "changemap", name, p => Safe(p, "server", a => MapList(a, "server", false)), permission, _saChangeMap.Command as string);
    }

    private void RestoreSaChangeMap()
    {
        var saved = _saChangeMap;
        if (saved == null) return;
        _saChangeMap = null;
        try
        {
            var cat = SaCategory("server");
            if (cat == null || saved.Factory == null) return;
            if (Get(cat, "MenuFactories") is IDictionary factories) factories["changemap"] = saved.Factory;
            if (Get(cat, "MenuNames") is IDictionary names && saved.Name != null) names["changemap"] = saved.Name;
            if (Get(cat, "MenuPermissions") is IDictionary permissions)
            {
                if (saved.HadPermission) permissions["changemap"] = saved.Permission;
                else permissions.Remove("changemap");
            }
            if (Get(cat, "MenuCommandNames") is IDictionary commands)
            {
                if (saved.HadCommand) commands["changemap"] = saved.Command;
                else commands.Remove("changemap");
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("SimpleAdmin change map menu not restored: {Error}", e.Message);
        }
    }

    // ================= Store (cs2-store) =================

    private string RegisterStore()
    {
        var cfg = Config.Store;
        _shop = null;
        if (!cfg.Enabled) return "off in the config";
        _shop = Capability("StoreApi", "StoreApi.IStoreApi", "cs2-store:api", out _shopType);
        if (_shop == null) return "cs2-store not on the server";

        AddCategory(CatStore, "pmm_sa.cat_store", cfg.Permission);
        AddMenu(CatStore, "give_credits", "pmm_sa.store_give", cfg.Menus, p => StorePlayers(p, StoreAction.Give));
        AddMenu(CatStore, "take_credits", "pmm_sa.store_take", cfg.Menus, p => StorePlayers(p, StoreAction.Take));
        AddMenu(CatStore, "give_item", "pmm_sa.store_item", cfg.Menus, p => StorePlayers(p, StoreAction.Item));
        return $"on ({StoreItems()?.Count ?? 0} items)";
    }

    private enum StoreAction { Give, Take, Item }

    private int Credits(CCSPlayerController p)
    {
        try { return Convert.ToInt32(Call(_shop, _shopType, "GetPlayerCredits", p) ?? 0); }
        catch { return 0; }
    }

    // Store.Store.Instance.Items: uniqueid -> item fields (type, name, price ...).
    private static Dictionary<string, Dictionary<string, string>>? StoreItems()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == "cs2-store");
            var instance = asm?.GetType("Store.Store")?.GetProperty("Instance", Static)?.GetValue(null);
            return Get(instance, "Items") as Dictionary<string, Dictionary<string, string>>;
        }
        catch
        {
            return null;
        }
    }

    private object StorePlayers(CCSPlayerController admin, StoreAction action)
    {
        var c = AdminTranslations.Culture(admin);
        string title = TX(c, action switch { StoreAction.Give => "pmm_sa.store_give", StoreAction.Take => "pmm_sa.store_take", _ => "pmm_sa.store_item" });
        var menu = NewMenu(title, CatStore, admin);
        int n = 0;
        foreach (var p in Humans(Config.Store.ShowBots))
        {
            var target = p;
            string hint = Fmt(TX(c, "pmm_sa.store_credits"), Credits(p));
            if (action == StoreAction.Item) Sub(menu, Name(p), hint, a => StoreTypes(a, target));
            else Sub(menu, Name(p), hint, a => StoreAmounts(a, target, action == StoreAction.Take));
            n++;
        }
        if (n == 0) Empty(menu, c);
        return menu;
    }

    private object StoreAmounts(CCSPlayerController admin, CCSPlayerController target, bool take)
    {
        var c = AdminTranslations.Culture(admin);
        string head = TX(c, take ? "pmm_sa.store_take" : "pmm_sa.store_give");
        var menu = NewMenu(head + ": " + Name(target), CatStore, admin);
        int now = target.IsValid ? Credits(target) : 0;
        foreach (int amount in Config.Store.Credits.Where(v => v > 0).Distinct())
        {
            int a = amount;
            string label = (take ? "−" : "+") + a.ToString("N0", c);
            string hint = Fmt(TX(c, "pmm_sa.store_after"), Math.Max(0, take ? now - a : now + a).ToString("N0", c));
            Option(menu, label, hint, x =>
            {
                if (!target.IsValid)
                {
                    Tell(x, TX(c, "pmm_sa.gone"));
                    return;
                }
                try
                {
                    int before = Credits(target);
                    if (take) Call(_shop, _shopType, "SetPlayerCredits", target, Math.Max(0, before - a));
                    else Call(_shop, _shopType, "GivePlayerCredits", target, a);
                    int after = Credits(target);
                    Log(x, $"{(take ? "store_takecredits" : "css_givecredits")} #{target.UserId} {a}");
                    Tell(x, Fmt(TX(c, take ? "pmm_sa.store_taken" : "pmm_sa.store_given"), Name(target), a, after));
                }
                catch (Exception e)
                {
                    Tell(x, Fmt(TX(c, "pmm_sa.error"), Err(e)));
                }
                AddTimer(0.2f, () => Reopen(x, y => StoreAmounts(y, target, take)));
            });
        }
        if (!Config.Store.Credits.Any(v => v > 0)) Empty(menu, c);
        return menu;
    }

    private object StoreTypes(CCSPlayerController admin, CCSPlayerController target)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(TX(c, "pmm_sa.store_item") + ": " + Name(target), CatStore, admin);
        var items = StoreItems();
        if (items == null || items.Count == 0)
        {
            Empty(menu, c);
            return menu;
        }
        foreach (var group in items
                     .Where(i => i.Value.TryGetValue("type", out var t) && !string.IsNullOrEmpty(t))
                     .GroupBy(i => i.Value["type"], StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string type = group.Key;
            Sub(menu, type, Fmt(TX(c, "pmm_sa.store_count"), group.Count()), a => StoreTypeItems(a, target, type));
        }
        return menu;
    }

    private object StoreTypeItems(CCSPlayerController admin, CCSPlayerController target, string type)
    {
        var c = AdminTranslations.Culture(admin);
        var menu = NewMenu(type + ": " + Name(target), CatStore, admin);
        var items = StoreItems() ?? new();
        int n = 0;
        foreach (var (id, fields) in items
                     .Where(i => i.Value.TryGetValue("type", out var t) && t.Equals(type, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(i => i.Value.GetValueOrDefault("name") ?? i.Key, StringComparer.OrdinalIgnoreCase))
        {
            string uid = fields.GetValueOrDefault("uniqueid") ?? id;
            string name = fields.GetValueOrDefault("name") ?? uid;
            bool has = false;
            try { has = target.IsValid && Call(_shop, _shopType, "Item_PlayerHas", target, type, uid, false) is true; }
            catch { /* treat as not owned */ }
            string price = fields.GetValueOrDefault("price") ?? "0";
            var item = new Dictionary<string, string>(fields);
            item.TryAdd("uniqueid", uid);
            item.TryAdd("price", "0");
            Option(menu, name, has ? TX(c, "pmm_sa.store_has") : Fmt(TX(c, "pmm_sa.store_credits"), price), a =>
            {
                if (!target.IsValid)
                {
                    Tell(a, TX(c, "pmm_sa.gone"));
                    return;
                }
                try
                {
                    bool ok = Call(_shop, _shopType, "Item_Give", target, item) is true;
                    Log(a, $"store give item #{target.UserId} {uid}");
                    Tell(a, Fmt(TX(c, ok ? "pmm_sa.store_item_given" : "pmm_sa.store_item_failed"), Name(target), name));
                }
                catch (Exception e)
                {
                    Tell(a, Fmt(TX(c, "pmm_sa.error"), Err(e)));
                }
                AddTimer(0.2f, () => Reopen(a, x => StoreTypeItems(x, target, type)));
            }, has);
            n++;
        }
        if (n == 0) Empty(menu, c);
        return menu;
    }
}

// SimpleAdmin's ICS2_SimpleAdminApi through reflection (no reference to CS2-SimpleAdminApi.dll).
internal sealed class SaApi
{
    private readonly object _api;
    private readonly Type _type;

    public SaApi(object api, Type type)
    {
        _api = api;
        _type = type;
    }

    private MethodInfo Method(string name, Func<ParameterInfo[], bool> match)
        => _type.GetMethods().FirstOrDefault(m => m.Name == name && match(m.GetParameters()))
           ?? throw new MissingMethodException(_type.Name, name);

    public void Ready(Action handler, bool add)
    {
        var ev = _type.GetEvent("OnSimpleAdminReady") ?? throw new MissingMemberException(_type.Name, "OnSimpleAdminReady");
        if (add) ev.AddEventHandler(_api, handler);
        else ev.RemoveEventHandler(_api, handler);
    }

    public void RegisterCategory(string id, string nameKey, string permission, object localizer)
        => Method("RegisterMenuCategory", p => p.Length == 4).Invoke(_api, new[] { id, nameKey, permission, localizer });

    // RegisterMenu(categoryId, menuId, menuNameKey, Func<CCSPlayerController, MenuContext, object>, permission, commandName, moduleLocalizer)
    public void RegisterMenu(string cat, string id, string nameKey, Func<CCSPlayerController, object> factory, string? permission, object localizer)
    {
        var m = Method("RegisterMenu", p => p.Length == 7);
        var funcType = m.GetParameters()[3].ParameterType;
        var holder = new FactoryHolder(factory);
        // Func<CCSPlayerController, MenuContext, object> bound to Invoke(CCSPlayerController, object): parameter contravariance.
        var del = Delegate.CreateDelegate(funcType, holder, typeof(FactoryHolder).GetMethod(nameof(FactoryHolder.Invoke))!);
        m.Invoke(_api, new object?[] { cat, id, nameKey, del, permission, null, localizer });
    }

    // RegisterMenu(categoryId, menuId, menuName, Func<CCSPlayerController, object>, permission, commandName): the name goes
    // through SimpleAdmin's own translations (used for its "sa_changemap").
    public void RegisterMenuPlain(string cat, string id, string name, Func<CCSPlayerController, object> factory, string? permission, string? command)
        => Method("RegisterMenu", p => p.Length == 6 && p[3].ParameterType == typeof(Func<CCSPlayerController, object>))
            .Invoke(_api, new object?[] { cat, id, name, factory, permission, command });

    public void UnregisterMenu(string cat, string id)
        => Method("UnregisterMenu", p => p.Length == 2).Invoke(_api, new object[] { cat, id });

    public object CreateMenuWithBack(string title, string cat, CCSPlayerController player)
        => Method("CreateMenuWithBack", p => p.Length == 3 && p[0].ParameterType == typeof(string)).Invoke(_api, new object[] { title, cat, player })!;

    public void AddMenuOption(object menu, string name, Action<CCSPlayerController> action, bool disabled)
        => Method("AddMenuOption", p => p.Length == 5).Invoke(_api, new object?[] { menu, name, action, disabled, null });

    public void AddSubMenu(object menu, string name, Func<CCSPlayerController, object> factory, bool disabled)
        => Method("AddSubMenu", p => p.Length == 5).Invoke(_api, new object?[] { menu, name, factory, disabled, null });

    public void OpenMenu(object menu, CCSPlayerController player)
        => Method("OpenMenu", p => p.Length == 2).Invoke(_api, new[] { menu, player });

    public void LogCommand(CCSPlayerController? caller, string command)
        => Method("LogCommand", p => p.Length == 2 && p[1].ParameterType == typeof(string)).Invoke(_api, new object?[] { caller, command });

    private sealed class FactoryHolder
    {
        private readonly Func<CCSPlayerController, object> _factory;
        public FactoryHolder(Func<CCSPlayerController, object> factory) => _factory = factory;
        public object Invoke(CCSPlayerController player, object context) => _factory(player);
    }
}

// IStringLocalizer for SimpleAdmin's per-player names of our categories and entries. SimpleAdmin asks it inside
// WithTemporaryCulture(player language), so CurrentCulture is the player's language.
internal sealed class ModuleLocalizer : IStringLocalizer
{
    private readonly Plugin _plugin;

    public ModuleLocalizer(Plugin plugin) => _plugin = plugin;

    public LocalizedString this[string name]
    {
        get
        {
            string? value = _plugin.Localize(CultureInfo.CurrentCulture, name);
            return new LocalizedString(name, value ?? name, value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var s = this[name];
            try { return new LocalizedString(name, string.Format(s.Value, arguments), s.ResourceNotFound); }
            catch (FormatException) { return s; }
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
}
