using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

// How one SimpleAdmin menu entry looks. The key is the text exactly as SimpleAdmin shows it
// (any language: "Забанить" and "Ban" are two keys). The new name and the grey hint on the right
// are in the translations file, because they depend on the player's language.
public class ItemStyle
{
    // Icon on the left (names: tools/gen_layout.py ICONS). Empty = no icon.
    public string Icon { get; set; } = "";

    // Red text: bans, removing an admin, restart.
    public bool Danger { get; set; }

    // Show the "›" arrow before anyone clicked the entry. After the first click the module
    // remembers by itself whether the entry opens another menu.
    public bool Arrow { get; set; }
}

public class SimpleAdminPanelConfig : BasePluginConfig
{
    // false: SimpleAdmin menus stay the usual PanoramaMenuManager menus.
    public bool Enabled { get; set; } = true;

    // true: SimpleAdmin menus always open on the panel, whatever the player chose in !menu
    // and whatever MenuConfigs.MenuType says in CS2-SimpleAdmin.json (Harmony prefix on PMM's MenuInstance.Open).
    // false: only players with PanoramaMenu (mouse) in !menu get the panel.
    public bool ForcePanorama { get; set; } = true;

    // Server log lines "[debug]" for every PMM menu open (same as css_pmm_sa debug).
    public bool Debug { get; set; }

    public string LayoutPath { get; set; } = "panorama/layout/custom_game/pmm_admin.xml";

    // Left, Center, Right.
    public string Position { get; set; } = "Left";

    // Header colour: Red, Bright, Dark, Coral.
    public string Accent { get; set; } = "Red";

    // Pause before the panel is drawn. Drawing in the frame of the chat command (!admin) kicks with NETWORK_DISCONNECT.
    public float OpenDelay { get; set; } = 0.2f;

    // Pause before the mouse is captured.
    public float InputDelay { get; set; } = 0.2f;

    // Menu style for players who did not choose one in the panel settings: Scroll (mouse wheel, no numbers),
    // Pages (pages, no numbers) or Keys (keys 1-7 pick, 8 back, 9 next, 0 close; needs the css_N binds).
    public string DefaultStyle { get; set; } = "Scroll";

    // Main menu: "Panel settings" entry (style, number keys test). The choice is saved per player in the database of CS2-SimpleAdmin (MySQL) or in pmm_simpleadmin.sqlite (Storage.cs).
    public bool ShowSettings { get; set; } = true;

    // Icon of the settings entry (names: tools/gen_layout.py ICONS).
    public string SettingsIcon { get; set; } = "colorwheel";

    // Number keys test: back to the settings after this many seconds without a key.
    public float KeyTestTimeout { get; set; } = 40f;

    // Main menu: number of menus in every category on the right.
    public bool ShowCategoryCounts { get; set; } = true;

    // Main menu: line "map · players / slots" under the title.
    public bool ShowServerLine { get; set; } = true;

    // Player rows: "#userid" on the right.
    public bool ShowUserId { get; set; } = true;

    // Menus opened from code of these assemblies are drawn by the panel. SimpleAdmin modules
    // (FunCommands, your own) build their menus through the SimpleAdmin API, so they are caught too.
    public List<string> SourceAssemblies { get; set; } = new() { "CS2-SimpleAdmin" };

    // Sections of other plugins in the admin menu. Every block is in the config from the first start, so it is
    // set once: a block of a plugin that is not on the server simply does nothing.
    public VipIntegration VipCore { get; set; } = new();
    public MapsIntegration MapChooser { get; set; } = new();
    public StoreIntegration Store { get; set; } = new();

    public Dictionary<string, ItemStyle> Items { get; set; } = new()
    {
        // main menu (ru / en)
        ["Управление игроками"] = new() { Icon = "teamplayer", Arrow = true },
        ["Players Manage"] = new() { Icon = "teamplayer", Arrow = true },
        ["Управление сервером"] = new() { Icon = "settings", Arrow = true },
        ["Server Manage"] = new() { Icon = "settings", Arrow = true },
        ["Управление администраторами"] = new() { Icon = "shield", Arrow = true },
        ["Admins Manage"] = new() { Icon = "shield", Arrow = true },
        ["Развлекательные Команды"] = new() { Icon = "star", Arrow = true },
        ["Fun Commands"] = new() { Icon = "star", Arrow = true },
        // dangerous actions
        ["Забанить"] = new() { Danger = true, Arrow = true },
        ["Ban"] = new() { Danger = true, Arrow = true },
        ["Удалить администратора"] = new() { Danger = true, Arrow = true },
        ["Remove Admin"] = new() { Danger = true, Arrow = true },
        ["Перезапустить игру"] = new() { Danger = true, Arrow = true },
        ["Restart Game"] = new() { Danger = true, Arrow = true },
        ["Restart Round"] = new() { Danger = true },
        // integrations (Integrations.cs)
        ["Управление VIP"] = new() { Icon = "trophy", Arrow = true },
        ["VIP Manage"] = new() { Icon = "trophy", Arrow = true },
        ["Управление картами"] = new() { Icon = "random", Arrow = true },
        ["Maps Manage"] = new() { Icon = "random", Arrow = true },
        ["Управление магазином"] = new() { Icon = "shoppingcart", Arrow = true },
        ["Store Manage"] = new() { Icon = "shoppingcart", Arrow = true },
        ["Снять VIP"] = new() { Danger = true, Arrow = true },
        ["Remove VIP"] = new() { Danger = true, Arrow = true },
        ["Забрать кредиты"] = new() { Danger = true, Arrow = true },
        ["Take credits"] = new() { Danger = true, Arrow = true },
    };
}

// One entry of an integration section. Permission empty = the section's permission is enough.
// One flag ("@css/ban") or one CounterStrikeSharp group ("#css/admin"), the same as SimpleAdmin's own entries.
public class IntegrationMenu
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = "";
}

// cs2-VIPCore (partiusfabaa / thesamefabius), capability "vipcore:core".
public class VipIntegration
{
    // false: no "Manage VIP" section even when VIPCore is on the server.
    public bool Enabled { get; set; } = true;

    // Who sees the section (VIPCore's own css_vip_* commands need @css/root).
    public string Permission { get; set; } = "@css/root";

    // give - give / change VIP (players -> group -> time); remove - remove VIP;
    // list - VIP players online (click = change the group / time); reload - css_vip_reload.
    public Dictionary<string, IntegrationMenu> Menus { get; set; } = new()
    {
        ["give"] = new(),
        ["remove"] = new(),
        ["list"] = new(),
        ["reload"] = new(),
    };

    // Terms in the "give" menu, in MINUTES (0 = forever). Converted to VIPCore's TimeMode by the module.
    public List<int> Durations { get; set; } = new() { 60, 1440, 4320, 10080, 43200, 0 };

    // Bots in the player lists.
    public bool ShowBots { get; set; }
}

// GG1MapChooser (ssypchenko), assembly GG1MapChooser.
public class MapsIntegration
{
    public bool Enabled { get; set; } = true;

    // Same flag as GG1MapChooser's own !maps / ggmap.
    public string Permission { get; set; } = "@css/changemap";

    // change - change the map now (+ random map); nextmap - set the next map;
    // votes - map vote with change / for the next map, "change the map or not"; pools - map pools and pool vote.
    public Dictionary<string, IntegrationMenu> Menus { get; set; } = new()
    {
        ["change"] = new(),
        ["nextmap"] = new(),
        ["votes"] = new(),
        ["pools"] = new(),
    };

    // !maps (css_maps of GG1MapChooser) opens this section of the admin panel instead of GG1's own menu.
    public bool RedirectMapsCommand { get; set; } = true;

    // SimpleAdmin "Manage server -> Change map" shows GG1MapChooser's maps (its pools and display names)
    // instead of SimpleAdmin's DefaultMaps / WorkshopMaps lists.
    public bool RedirectSimpleAdminChangeMap { get; set; } = true;
}

// cs2-store (schwarper), capability "cs2-store:api".
public class StoreIntegration
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = "@css/root";

    // give_credits / take_credits - players -> amount; give_item - players -> item type -> item.
    public Dictionary<string, IntegrationMenu> Menus { get; set; } = new()
    {
        ["give_credits"] = new(),
        ["take_credits"] = new(),
        ["give_item"] = new(),
    };

    // Amounts in the credits menus.
    public List<int> Credits { get; set; } = new() { 100, 500, 1000, 5000, 10000, 50000 };

    public bool ShowBots { get; set; }
}

// CounterStrikeSharp writes the config only when the file does not exist. Keys added in later builds
// (the integration blocks, their entries, new Items) are written into an existing PMM_SimpleAdmin.json here,
// so the admin sees every option once and can set it. Values already in the file are never changed.
internal static class ConfigUpgrade
{
    // Blocks merged key by key (also nested), so new options inside them appear too.
    private static readonly string[] Deep = { "VipCore", "MapChooser", "Store" };

    // Items entries added by the integrations: added once if the file has an Items block without them.
    private static readonly string[] NewItems =
    {
        "Управление VIP", "VIP Manage", "Управление картами", "Maps Manage", "Управление магазином", "Store Manage",
        "Снять VIP", "Remove VIP", "Забрать кредиты", "Take credits",
    };

    public static void Run(string path, SimpleAdminPanelConfig config, ILogger log)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) return;
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }) is not JsonObject file) return;
            if (JsonSerializer.SerializeToNode(config, config.GetType()) is not JsonObject current) return;

            var defaults = new SimpleAdminPanelConfig();
            int added = 0;
            foreach (var (key, value) in current)
            {
                var existing = Find(file, key);
                if (existing == null)
                {
                    if (FindKey(file, key) != null) continue;      // "key": null - left as the admin wrote it
                    file[key] = value?.DeepClone();
                    added++;
                }
                else if (Deep.Contains(key) && existing is JsonObject fo && value is JsonObject vo)
                {
                    added += Merge(fo, vo);
                }
            }

            if (Find(file, "Items") is JsonObject items)
            {
                var ser = JsonSerializer.SerializeToNode(defaults.Items) as JsonObject;
                foreach (var name in NewItems)
                {
                    if (FindKey(items, name) != null || ser?[name] is not { } v) continue;
                    items[name] = v.DeepClone();
                    added++;
                }
            }

            if (added == 0) return;
            // The rewritten file has no // comments any more: keep the commented one next to it.
            string text = File.ReadAllText(path);
            if (text.Contains("//"))
            {
                File.Copy(path, path + ".bak", true);
                log.LogInformation("Config: comments are not kept when new options are written, the old file is {Bak}", path + ".bak");
            }
            File.WriteAllText(path, file.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            log.LogInformation("Config: {Count} new options written to {Path}", added, path);
        }
        catch (Exception e)
        {
            log.LogWarning("Config: new options not written to {Path}: {Error}", path, e.Message);
        }
    }

    private static int Merge(JsonObject target, JsonObject source)
    {
        int added = 0;
        foreach (var (key, value) in source)
        {
            var existing = Find(target, key);
            if (existing == null)
            {
                if (FindKey(target, key) != null) continue;
                target[key] = value?.DeepClone();
                added++;
            }
            else if (existing is JsonObject eo && value is JsonObject vo)
            {
                added += Merge(eo, vo);
            }
        }
        return added;
    }

    // CounterStrikeSharp reads the config case-insensitively.
    private static string? FindKey(JsonObject o, string key)
        => o.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Find(JsonObject o, string key) => FindKey(o, key) is { } k ? o[k] : null;
}
