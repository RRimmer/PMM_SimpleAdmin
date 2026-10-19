using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using MenuManager;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

// CS2-SimpleAdmin menus on an own PanoramaMenuManager panel (design "variant A - Classic+").
// SimpleAdmin is not rebuilt: it builds its menus through MenuManagerApi as always. A Harmony prefix on
// PMM's MenuInstance.Open forces PanoramaMenu for menus opened from SimpleAdmin code, PMM then hands them
// to the registered painters (pmm:paint) and this module draws them. Clicks call SimpleAdmin's own callbacks.
public partial class Plugin : BasePlugin, IPluginConfig<SimpleAdminPanelConfig>
{
    public override string ModuleName => "PMM_SimpleAdmin";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Rimmer";
    public override string ModuleDescription => "CS2-SimpleAdmin menus on a PanoramaMenuManager panel";

    internal const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public SimpleAdminPanelConfig Config { get; set; } = new();

    public void OnConfigParsed(SimpleAdminPanelConfig config)
    {
        config.SourceAssemblies = (config.SourceAssemblies ?? new()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        if (config.SourceAssemblies.Count == 0) config.SourceAssemblies.Add("CS2-SimpleAdmin");
        // Case-insensitive; a key that differs only in case does not break the load: the first one wins.
        var items = new Dictionary<string, ItemStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, style) in config.Items ?? new())
            if (!string.IsNullOrWhiteSpace(key) && style != null) items.TryAdd(key.Trim(), style);
        config.Items = items;
        if (config.OpenDelay < 0.05f) config.OpenDelay = 0.05f;
        if (config.InputDelay < 0f) config.InputDelay = 0f;
        Config = config;
        _debug = config.Debug;
        _sourceCache.Clear();
    }

    private static readonly PluginCapability<IMenuApi?> MenuCapability = new("menu:nfcore");
    internal static Plugin? Instance;

    private IMenuApi? _menu;
    private AdminTranslations? _tr;
    private string _why = "not started";

    internal bool Active => Config.Enabled && _menu != null && _registry != null;

    private const string HarmonyId = "rimmer.pmm.simpleadmin";
    private bool _harmonyReady;
    private readonly List<string> _patched = new();
    private FieldInfo? _forceType;

    // Menus opened by SimpleAdmin (tagged in the MenuInstance.Open prefix). TryPaint draws exactly these.
    private readonly ConditionalWeakTable<object, object> _sourceMenus = new();

    public override void Load(bool hotReload)
    {
        Instance = this;
        _harmonyReady = HarmonyLoader.Ensure(ModuleDirectory, Logger);
        _tr = new AdminTranslations(ModuleDirectory, Logger);
        _tr.EnsureSection();
        ConfigUpgrade.Run(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName, ModuleName + ".json"), Config, Logger);
        PanelLoad();
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        Attach();
    }

    public override void Unload(bool hotReload)
    {
        IntegrationsDetach();
        UnregisterPainter();
        if (_harmonyReady) HarmonyHost.UnpatchAll(HarmonyId);
        _patched.Clear();
        foreach (var p in CounterStrikeSharp.API.Utilities.GetPlayers())
            if (p.IsValid && IsOpen(p)) Hide(p);
        Instance = null;
    }

    private void Attach()
    {
        try { _menu = MenuCapability.Get(); } catch { _menu = null; }
        if (_menu == null)
        {
            _why = "MenuManager (menu:nfcore) not found";
            Logger.LogWarning("Admin panel is off: {Why}. SimpleAdmin keeps the usual PMM menus", _why);
            return;
        }
        RegisterPainter();
        Patch();
        IntegrationsAttach();
        _why = _registry == null ? "MenuManager has no pmm:paint (PMM core 1.2.04+ is needed)"
            : !_patched.Any(p => p.EndsWith(".CreateMenu")) ? "SimpleAdmin CreateMenu not patched: its menus are not recognised"
            : !_patched.Contains("MenuInstance.Open") && Config.ForcePanorama ? "MenuInstance.Open not patched: only PanoramaMenu players get the panel"
            : "";
    }

    // ---------------- Harmony: SimpleAdmin menus always as PanoramaMenu ----------------

    // PMM picks the menu type (player's !menu choice, SimpleAdmin's MenuType) inside MenuInstance.Open,
    // before the painters are asked. The prefix sets the forced type of a SimpleAdmin menu to PanoramaMenu,
    // so the panel opens for every admin, whatever is chosen in !menu or in CS2-SimpleAdmin.json.
    private void Patch()
    {
        if (!_harmonyReady)
        {
            Logger.LogError("0Harmony is not loaded: the admin panel is off");
            return;
        }
        HarmonyHost.UnpatchAll(HarmonyId);
        _patched.Clear();

        // 1. Tag SimpleAdmin menus where they are created. The call stack cannot be used for this:
        //    in Release builds .NET turns "menu.Open(player)" at the end of a method into a tail call,
        //    so SimpleAdmin's frames are already gone when MenuInstance.Open runs.
        var creator = typeof(Patches).GetMethod(nameof(Patches.CreatePostfix));
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = asm.GetName().Name ?? "";
            if (!Config.SourceAssemblies.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null) continue;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != "CreateMenu" || m.IsAbstract || m.ContainsGenericParameters) continue;
                    if (!typeof(IMenu).IsAssignableFrom(m.ReturnType)) continue;
                    try
                    {
                        HarmonyHost.Patch(HarmonyId, m, null, creator, null);
                        _patched.Add($"{t.Name}.CreateMenu");
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, "Harmony failed on {Asm} {Type}.CreateMenu", name, t.Name);
                    }
                }
            }
        }
        if (_patched.Count == 0)
            Logger.LogError("No CreateMenu found in [{Src}]: SimpleAdmin menus cannot be recognised", string.Join(", ", Config.SourceAssemblies));

        // 2. Force PanoramaMenu for tagged menus (players with WASD, CSGO Menu, chat ... get the panel too).
        var core = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MenuManagerCore");
        var type = core?.GetType("MenuManagerCore.MenuInstance");
        var open = type?.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(CCSPlayerController) }, null);
        _forceType = type?.GetField("_forcetype", Any);
        if (open == null || _forceType == null || _forceType.FieldType != typeof(MenuType))
        {
            Logger.LogError("MenuManagerCore.MenuInstance.Open / _forcetype not found: only PanoramaMenu players get the admin panel");
        }
        else
        {
            try
            {
                HarmonyHost.Patch(HarmonyId, open, typeof(Patches).GetMethod(nameof(Patches.OpenPrefix)), null, null);
                _patched.Add("MenuInstance.Open");
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Harmony failed on MenuInstance.Open");
            }
        }
        // 3. GG1MapChooser's !maps (css_maps) opens the "Maps" section of the panel (Config.MapChooser.RedirectMapsCommand).
        var gg = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == "GG1MapChooser")?.GetType("MapChooser.MapChooser");
        var maps = gg?.GetMethod("MapMenuCommand", Any);
        if (maps != null)
        {
            try
            {
                HarmonyHost.Patch(HarmonyId, maps, typeof(Patches).GetMethod(nameof(Patches.MapsPrefix)), null, null);
                _patched.Add("GG1.MapMenuCommand");
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Harmony failed on GG1MapChooser MapMenuCommand: !maps keeps GG1's own menu");
            }
        }
        Logger.LogInformation("Patched: {List}", _patched.Count == 0 ? "-" : string.Join(", ", _patched));
    }

    // Postfix of SimpleAdmin's CreateMenu: remember the menu object.
    internal void TagMenu(object? menu)
    {
        if (menu == null) return;
        _sourceMenus.AddOrUpdate(menu, menu);
        if (_debug) Logger.LogInformation("[debug] CreateMenu \"{Title}\" tagged", (menu as IMenu)?.Title);
    }

    // Prefix of MenuInstance.Open: tag the SimpleAdmin menu and force PanoramaMenu.
    internal void BeforeOpen(object menu)
    {
        if (!Config.Enabled || _registry == null)
        {
            if (_debug) Logger.LogInformation("[debug] Open \"{Title}\": panel off (enabled={En}, painter={P})", (menu as IMenu)?.Title, Config.Enabled, _registry != null);
            return;
        }
        bool source = IsSourceMenu(menu) || OpenedBySource();
        if (_debug)
        {
            Logger.LogInformation("[debug] Open \"{Title}\" simpleadmin={Source} type={Type}",
                (menu as IMenu)?.Title, source, _forceType?.GetValue(menu));
            if (!source) Logger.LogInformation("[debug]   stack assemblies: {List}", StackAssemblies());
        }
        if (!source) return;
        _sourceMenus.AddOrUpdate(menu, menu);
        if (Config.ForcePanorama && _forceType != null)
        {
            _forceType.SetValue(menu, MenuType.PanoramaMenu);
            if (_debug) Logger.LogInformation("[debug]   forced type -> {Type}", _forceType.GetValue(menu));
        }
    }

    // Debug: which assemblies opened the menu (to fill SourceAssemblies).
    private static string StackAssemblies()
    {
        try
        {
            var names = new List<string>();
            foreach (var frame in new StackTrace(1, false).GetFrames())
            {
                var m = frame.GetMethod();
                string name = m?.DeclaringType?.Assembly.GetName().Name ?? "(dynamic)";
                string entry = name + ":" + (m?.DeclaringType?.Name ?? "?") + "." + (m?.Name ?? "?");
                if (names.Count == 0 || names[^1] != entry) names.Add(entry);
            }
            return string.Join(" <- ", names.Take(14));
        }
        catch (Exception e) { return "error " + e.Message; }
    }

    internal bool IsSourceMenu(object menu) => _sourceMenus.TryGetValue(menu, out _);

    // ---------------- is this a SimpleAdmin menu ----------------

    private readonly Dictionary<Assembly, bool> _sourceCache = new();

    // TryPaint runs inside IMenu.Open, so the code that opened the menu is on the stack:
    // SimpleAdmin's MenuBuilder.OpenMenu, its API (modules) or a callback of one of its menus.
    private bool OpenedBySource()
    {
        var own = typeof(Plugin).Assembly;
        StackFrame[] frames;
        try { frames = new StackTrace(1, false).GetFrames(); }
        catch { return false; }

        foreach (var frame in frames)
        {
            var asm = frame.GetMethod()?.DeclaringType?.Assembly;
            if (asm == null || asm == own) continue;
            if (!_sourceCache.TryGetValue(asm, out bool hit))
            {
                string name = asm.GetName().Name ?? "";
                hit = Config.SourceAssemblies.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
                _sourceCache[asm] = hit;
            }
            if (hit) return true;
        }
        return false;
    }

    // ---------------- commands ----------------

    private bool _debug;

    [ConsoleCommand("css_pmm_sa", "PMM_SimpleAdmin status | reattach | debug | integrations")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnStatus(CCSPlayerController? caller, CommandInfo info)
    {
        if (info.ArgCount > 1 && info.GetArg(1) == "reattach")
        {
            UnregisterPainter();
            Attach();
        }
        if (info.ArgCount > 1 && info.GetArg(1) == "debug")
        {
            _debug = !_debug;
            info.ReplyToCommand($"[PMM_SA] debug={_debug}: every PMM menu open is written to the server log");
        }
        if (info.ArgCount > 1 && info.GetArg(1) == "integrations") RegisterIntegrations();
        info.ReplyToCommand($"[PMM_SA] active={Active} enabled={Config.Enabled} menuApi={_menu != null} painter={_registry != null} layout={(_layout is { IsValid: true } ? "ok" : "none")} open={_slots.Count(s => s.Open)}");
        if (_why != "") info.ReplyToCommand($"[PMM_SA] off: {_why}");
        info.ReplyToCommand($"[PMM_SA] harmony={_harmonyReady} patched={(_patched.Count == 0 ? "-" : string.Join(", ", _patched))} forcePanorama={Config.ForcePanorama}");
        info.ReplyToCommand($"[PMM_SA] sources=[{string.Join(", ", Config.SourceAssemblies)}] layoutPath={Config.LayoutPath}");
        info.ReplyToCommand($"[PMM_SA] translations: {_tr?.FilePath ?? "-"} ({_tr?.Languages ?? 0} languages)");
        info.ReplyToCommand($"[PMM_SA] menu styles: {_store?.Describe ?? "-"}, {_styles.Count} players in memory, default {Config.DefaultStyle}");
        info.ReplyToCommand($"[PMM_SA] integrations: {_integrations}");
    }

    // ---------------- reflection ----------------

    internal static object? Get(object? o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        PropertyInfo? p = null;
        try { p = t.GetProperty(name, Any); }
        catch (AmbiguousMatchException) { p = t.GetProperties(Any).FirstOrDefault(x => x.Name == name && x.DeclaringType == t); }
        if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o);
        return t.GetField(name, Any)?.GetValue(o);
    }
}

internal static class Patches
{
    // SimpleAdmin Helper.CreateMenu / AdminMenu.CreateMenu (returns IMenu)
    public static void CreatePostfix(object? __result)
    {
        try { Plugin.Instance?.TagMenu(__result); }
        catch (Exception e) { Plugin.Instance?.Logger.LogError(e, "Admin panel: CreateMenu postfix failed"); }
    }

    // GG1MapChooser MapChooser.MapMenuCommand(caller, command): false = GG1's own !maps menu is skipped.
    public static bool MapsPrefix(CCSPlayerController caller)
    {
        try { return Plugin.Instance?.OnMapsCommand(caller) != true; }
        catch (Exception e)
        {
            Plugin.Instance?.Logger.LogError(e, "Admin panel: !maps prefix failed");
            return true;
        }
    }

    // MenuManagerCore.MenuInstance.Open(player)
    public static void OpenPrefix(object __instance)
    {
        try { Plugin.Instance?.BeforeOpen(__instance); }
        catch (Exception e) { Plugin.Instance?.Logger.LogError(e, "Admin panel: Open prefix failed"); }
    }
}

// 0Harmony must live in the default (non-collectible) load context: MonoMod builds a proxy
// in a non-collectible dynamic assembly that references 0Harmony by name, and CSS loads plugins
// into collectible contexts. So 0Harmony.dll ships in plugins/PMM_SimpleAdmin/harmony/ and is loaded here.
// If PMM_VIPCore / PMM_StoreCore / PMM_GG1MapChooser already loaded it, the same copy is reused.
internal static class HarmonyLoader
{
    public static bool Ensure(string pluginDir, ILogger log)
    {
        try
        {
            var asm = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "0Harmony");
            if (asm == null)
            {
                string path = Path.Combine(pluginDir, "harmony", "0Harmony.dll");
                if (!File.Exists(path))
                {
                    log.LogError("Not found: {Path}", path);
                    return false;
                }
                asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }

            var own = AssemblyLoadContext.GetLoadContext(typeof(HarmonyLoader).Assembly);
            if (own != null && own != AssemblyLoadContext.Default)
            {
                var found = asm;
                own.Resolving += (_, name) => name.Name == "0Harmony" ? found : null;
            }

            log.LogInformation("0Harmony {Ver} in default context: {Loc}", asm.GetName().Version, asm.Location);
            return true;
        }
        catch (Exception e)
        {
            log.LogError(e, "Failed to load 0Harmony into the default context");
            return false;
        }
    }
}

// The only place that touches HarmonyLib types; it is JIT-compiled after HarmonyLoader.Ensure.
internal static class HarmonyHost
{
    private static readonly Dictionary<string, HarmonyLib.Harmony> Instances = new();

    private static HarmonyLib.Harmony Get(string id)
    {
        if (!Instances.TryGetValue(id, out var h)) Instances[id] = h = new HarmonyLib.Harmony(id);
        return h;
    }

    public static void Patch(string id, MethodBase original, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer)
    {
        Get(id).Patch(original,
            prefix: prefix == null ? null : new HarmonyLib.HarmonyMethod(prefix),
            postfix: postfix == null ? null : new HarmonyLib.HarmonyMethod(postfix),
            finalizer: finalizer == null ? null : new HarmonyLib.HarmonyMethod(finalizer));
    }

    public static void UnpatchAll(string id)
    {
        if (Instances.TryGetValue(id, out var h)) h.UnpatchAll(id);
    }
}
