using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;

namespace PMM_SimpleAdmin;

// Texts from MenuManager's file configs/plugins/MenuManagerCore/MenuManager_Modules_Translation.json
// (plugin -> language -> key -> text). The plugin adds its own "PMM_SimpleAdmin" section once and later only
// the keys it is missing; texts the admin changed stay. Keys:
//   pmm_sa.*            panel texts
//   <SimpleAdmin text>  new name of a menu entry or title, exactly as SimpleAdmin shows it ("Выгнать": "Кикнуть")
//   <text>|hint         grey hint on the right of the entry ("Заглушить|hint": "гэг")
internal sealed class AdminTranslations
{
    public const string Section = "PMM_SimpleAdmin";
    private const string FileName = "MenuManager_Modules_Translation.json";

    private readonly ILogger _log;
    private readonly string _path;
    private DateTime _writtenAt = DateTime.MinValue;
    private Dictionary<string, Dictionary<string, string>> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    public AdminTranslations(string moduleDirectory, ILogger log)
    {
        _log = log;
        _path = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "configs", "plugins", "MenuManagerCore", FileName));
    }

    public string FilePath => _path;

    public int Languages
    {
        get
        {
            ReloadIfChanged();
            return _byLanguage.Count;
        }
    }

    public void EnsureSection()
    {
        try
        {
            JsonObject root;
            if (File.Exists(_path))
            {
                var parsed = JsonNode.Parse(File.ReadAllText(_path), documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                if (parsed is not JsonObject obj)
                {
                    _log.LogWarning("Translations: {Path} is not a JSON object, section not added", _path);
                    return;
                }
                root = obj;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                root = new JsonObject();
            }

            bool created = root[Section] is not JsonObject;
            if (created) root[Section] = new JsonObject();
            var section = (JsonObject)root[Section]!;
            int added = 0;
            foreach (var (lang, map) in AdminDefaults.Texts)
            {
                if (section[lang] is not JsonObject o)
                {
                    o = new JsonObject();
                    section[lang] = o;
                }
                foreach (var (k, v) in map)
                {
                    if (o.ContainsKey(k)) continue;
                    o[k] = v;
                    added++;
                }
            }
            if (added == 0) return;

            File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            _log.LogInformation("Translations: {Count} keys {What} section {Section} of {Path}",
                added, created ? "written to the new" : "added to the", Section, _path);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Translations: could not add the {Section} section to {Path}", Section, _path);
        }
    }

    private void ReloadIfChanged()
    {
        if (!File.Exists(_path)) return;
        DateTime written = File.GetLastWriteTimeUtc(_path);
        if (written == _writtenAt) return;
        _writtenAt = written;

        var next = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var module in doc.RootElement.EnumerateObject())
            {
                if (!module.Name.Equals(Section, StringComparison.OrdinalIgnoreCase)) continue;
                if (module.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var language in module.Value.EnumerateObject())
                {
                    if (language.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!next.TryGetValue(language.Name, out var map))
                        next[language.Name] = map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var phrase in language.Value.EnumerateObject())
                    {
                        if (phrase.Value.ValueKind != JsonValueKind.String) continue;
                        string? value = phrase.Value.GetString();
                        if (value != null) map.TryAdd(phrase.Name, value);
                    }
                }
            }
            _byLanguage = next;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Translations: could not read {Path}", _path);
        }
    }

    public static CultureInfo Culture(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return CultureInfo.InvariantCulture;
        try { return player.GetLanguage(); }
        catch { return CultureInfo.InvariantCulture; }
    }

    // Exact key in the player's language (or its parent), without the English fallback.
    // Used for renames: a Russian entry must never get an English name.
    public string? GetExact(CultureInfo culture, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        ReloadIfChanged();
        foreach (string name in Candidates(culture, false))
            if (_byLanguage.TryGetValue(name, out var map) && map.TryGetValue(key, out var value))
                return value;
        return null;
    }

    // Exact key, English as the last resort, or null.
    public string? Get(CultureInfo culture, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        ReloadIfChanged();
        foreach (string name in Candidates(culture, true))
            if (_byLanguage.TryGetValue(name, out var map) && map.TryGetValue(key, out var value))
                return value;
        return null;
    }

    private static IEnumerable<string> Candidates(CultureInfo culture, bool english)
    {
        if (!string.IsNullOrEmpty(culture.Name)) yield return culture.Name;
        if (!string.IsNullOrEmpty(culture.Parent.Name) && culture.Parent.Name != culture.Name) yield return culture.Parent.Name;
        if (!string.IsNullOrEmpty(culture.TwoLetterISOLanguageName) && culture.TwoLetterISOLanguageName != "iv")
            yield return culture.TwoLetterISOLanguageName;
        if (english) yield return "en";
    }
}

// What the PMM_SimpleAdmin section starts with. Also the fallback when the file has no key.
internal static class AdminDefaults
{
    public static string Language(CultureInfo culture)
        => culture.TwoLetterISOLanguageName is "ru" or "uk" or "be" ? "ru" : "en";

    public static bool Has(string key) => Texts["en"].ContainsKey(key) || Texts["ru"].ContainsKey(key);

    public static string Text(CultureInfo culture, string key)
    {
        if (Texts[Language(culture)].TryGetValue(key, out var v)) return v;
        return Texts["en"].TryGetValue(key, out v) ? v : key;
    }

    public static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        ["ru"] = new()
        {
            ["pmm_sa.brand"] = "PMM · SIMPLEADMIN",
            ["pmm_sa.home"] = "Главное",
            ["pmm_sa.back"] = "Назад",
            ["pmm_sa.close"] = "Закрыть",
            ["pmm_sa.count"] = "всего {0}",
            ["pmm_sa.page"] = "{0} / {1}",
            ["pmm_sa.next"] = "Далее",
            ["pmm_sa.settings"] = "Настройки панели",
            ["pmm_sa.settings_title"] = "Настройки панели",
            ["pmm_sa.style_now"] = "Сейчас: {0}",
            ["pmm_sa.style_scroll"] = "Прокрутка колёсиком",
            ["pmm_sa.style_pages"] = "Страницы",
            ["pmm_sa.style_keys"] = "Цифры 1–0",
            ["pmm_sa.style_scroll_hint"] = "мышь",
            ["pmm_sa.style_pages_hint"] = "мышь",
            ["pmm_sa.style_keys_hint"] = "нужна проверка",
            ["pmm_sa.selected"] = "ВЫБРАНО",
            ["pmm_sa.keys_test"] = "Проверка цифр",
            ["pmm_sa.keys_title"] = "Проверка цифр",
            ["pmm_sa.keys_sub"] = "Нажимай 1 2 3 4 5 6 7 8 9 0 · получено {0} / 10",
            ["pmm_sa.keys_progress"] = "Нажато {0} / 10 · осталось: {1}",
            ["pmm_sa.keys_done"] = "Все 10 клавиш работают",
            ["pmm_sa.keys_enable"] = "Включить меню по цифрам",
            ["pmm_sa.keys_again"] = "Проверить ещё раз",
            ["pmm_sa.keys_help1"] = "Не загорается? Строка с биндами — в чате и в консоли",
            ["pmm_sa.keys_help2"] = "Скопируй её из консоли (~), вставь туда же, нажми Enter",
            ["pmm_sa.keys_help3"] = "Без нажатий {0} с — возврат в настройки",
            ["pmm_sa.binds"] = "Бинды для цифр",
            ["pmm_sa.binds_hint"] = "в чат и консоль",
            ["pmm_sa.binds_chat"] = "Меню по цифрам: скопируй строку из консоли (~), вставь в консоль и нажми Enter. Один раз.",
            ["pmm_sa.target"] = "Цель: {0}",
            ["pmm_sa.on"] = "ВКЛ",
            ["pmm_sa.off"] = "ВЫКЛ",
            ["pmm_sa.server_line"] = "{0} · {1} / {2} игроков · вы: {3}",
            ["pmm_sa.now"] = "сейчас",
            ["pmm_sa.workshop"] = "workshop",

            // titles
            ["SimpleAdmin"] = "Главное меню",
            ["Управление администраторами"] = "Администраторы",
            ["Развлекательные Команды"] = "Развлекательные команды",
            ["Продолжительность"] = "Срок",

            // short names for the crumb line ("Главное › Игроки ›")
            ["Управление игроками|crumb"] = "Игроки",
            ["Управление сервером|crumb"] = "Сервер",
            ["Управление администраторами|crumb"] = "Администраторы",
            ["Развлекательные Команды|crumb"] = "Развлечения",
            ["Принудить к команде|crumb"] = "Команда",

            // players
            ["Шлепнуть"] = "Шлепок",
            ["Шлепнуть|hint"] = "урон",
            ["Выгнать"] = "Кикнуть",
            ["Заглушить"] = "Запрет чата",
            ["Заглушить|hint"] = "гэг",
            ["Отключить звук"] = "Запрет голоса",
            ["Отключить звук|hint"] = "мут",
            ["Тишина|hint"] = "чат + голос",
            ["Принудить к команде"] = "Сменить команду",
            ["История"] = "История наказаний",
            ["CT"] = "Спецназ (CT)",
            ["T"] = "Террористы (T)",
            ["Спец"] = "Наблюдатели",
            ["Поменять"] = "Поменять местами",
            ["Поменять|hint"] = "CT ⇄ T",

            // server
            ["Сменить карту|hint"] = "{map}",
            ["Управление плагинами|hint"] = "@css/root",
            ["Управление плагинами"] = "Плагины",
            ["Пользовательские команды"] = "Свои команды",
            ["Open Plugins Manager"] = "Открыть менеджер плагинов",
            ["Restart Round"] = "Перезапустить раунд",

            // admins
            ["Удалить администратора"] = "Снять администратора",
            ["Перезагрузить администраторов"] = "Перезагрузить список",
            ["Перезагрузить администраторов|hint"] = "из БД",

            // fun commands (CS2-SimpleAdmin_FunCommands)
            ["Режим Бога"] = "Бессмертие",
            ["Режим Бога|hint"] = "god",
            ["Noclip"] = "Полёт сквозь стены",
            ["Noclip|hint"] = "noclip",
            ["Возрождение"] = "Возродить",
            ["Выдать Оружие"] = "Выдать оружие",
            ["Забрать Оружие"] = "Забрать оружие",
            ["Установить HP"] = "Здоровье",
            ["Установить HP|hint"] = "HP",
            ["Установить Скорость"] = "Скорость",
            ["Установить Гравитацию"] = "Гравитация",
            ["Установить Деньги"] = "Деньги",
            ["Установить Деньги|hint"] = "$",
            ["Изменить Размер"] = "Размер модели",
            // integrations (Integrations.cs): sections of VIPCore / GG1MapChooser / cs2-store
            ["pmm_sa.empty"] = "Пусто",
            ["pmm_sa.gone"] = "Игрок уже вышел",
            ["pmm_sa.error"] = "Ошибка: {0}",
            ["pmm_sa.error_title"] = "Ошибка",
            ["pmm_sa.t_forever"] = "Навсегда",
            ["pmm_sa.t_min"] = "{0} мин",
            ["pmm_sa.t_hour"] = "{0} ч",
            ["pmm_sa.t_day"] = "{0} дн.",
            ["pmm_sa.cat_vip"] = "Управление VIP",
            ["pmm_sa.vip_give"] = "Выдать / изменить VIP",
            ["pmm_sa.vip_remove"] = "Снять VIP",
            ["pmm_sa.vip_list"] = "VIP онлайн",
            ["pmm_sa.vip_reload"] = "Перезагрузить VIP",
            ["pmm_sa.vip_reload_do"] = "Перезагрузить конфиг VIPCore",
            ["pmm_sa.vip_none"] = "нет VIP",
            ["pmm_sa.vip_nobody"] = "Нет VIP онлайн",
            ["pmm_sa.vip_forever"] = "навсегда",
            ["pmm_sa.vip_until"] = "до {0}",
            ["pmm_sa.vip_group_title"] = "Группа VIP: {0}",
            ["pmm_sa.vip_time_title"] = "Срок VIP: {0}",
            ["pmm_sa.vip_remove_title"] = "Снять VIP: {0}",
            ["pmm_sa.vip_given"] = "{0}: VIP {1} на {2}",
            ["pmm_sa.vip_removed"] = "{0}: VIP снят",
            ["pmm_sa.vip_reloaded"] = "Конфиг VIPCore перезагружен",
            ["pmm_sa.vip_noauth"] = "{0}: Steam ещё не подтвердил игрока, попробуй через минуту",
            ["pmm_sa.cat_maps"] = "Управление картами",
            ["pmm_sa.maps_change"] = "Сменить карту",
            ["pmm_sa.maps_next"] = "Следующая карта",
            ["pmm_sa.maps_votes"] = "Голосования",
            ["pmm_sa.maps_pools"] = "Пулы карт",
            ["pmm_sa.maps_random"] = "Случайная карта",
            ["pmm_sa.maps_random_hint"] = "сразу",
            ["pmm_sa.maps_vote_change"] = "Голосование со сменой",
            ["pmm_sa.maps_vote_change_hint"] = "смена сразу",
            ["pmm_sa.maps_vote_next"] = "Голосование за следующую",
            ["pmm_sa.maps_vote_next_hint"] = "в конце карты",
            ["pmm_sa.maps_vote_yesno"] = "Менять ли карту?",
            ["pmm_sa.maps_vote_yesno_hint"] = "да / нет",
            ["pmm_sa.maps_pool_vote"] = "Голосование за пул",
            ["pmm_sa.maps_count"] = "{0} карт",
            ["pmm_sa.maps_no_pools"] = "Пулы не настроены",
            ["pmm_sa.maps_changing"] = "Смена карты: {0}",
            ["pmm_sa.maps_next_set"] = "Следующая карта: {0}",
            ["pmm_sa.maps_pool_set"] = "Пул карт: {0}",
            ["pmm_sa.maps_pool_failed"] = "Пул {0} пуст или не найден",
            ["pmm_sa.maps_vote_busy"] = "Уже идёт голосование",
            ["pmm_sa.maps_vote_started"] = "Голосование запущено",
            ["pmm_sa.cat_store"] = "Управление магазином",
            ["pmm_sa.store_give"] = "Выдать кредиты",
            ["pmm_sa.store_take"] = "Забрать кредиты",
            ["pmm_sa.store_item"] = "Выдать предмет",
            ["pmm_sa.store_credits"] = "{0} кр.",
            ["pmm_sa.store_after"] = "станет {0}",
            ["pmm_sa.store_count"] = "{0} шт.",
            ["pmm_sa.store_has"] = "уже есть",
            ["pmm_sa.store_given"] = "{0}: +{1} кредитов, теперь {2}",
            ["pmm_sa.store_taken"] = "{0}: −{1} кредитов, теперь {2}",
            ["pmm_sa.store_item_given"] = "{0}: выдан предмет {1}",
            ["pmm_sa.store_item_failed"] = "{0}: предмет {1} не выдан",
            ["Управление VIP|crumb"] = "VIP",
            ["Управление картами|crumb"] = "Карты",
            ["Управление магазином|crumb"] = "Магазин",
            ["Перезагрузить конфиг VIPCore|hint"] = "css_vip_reload",
            ["playerskin"] = "Скины игрока",
            ["customweapon"] = "Скины оружия",
            ["trail"] = "Следы",
            ["tracer"] = "Трассеры",
            ["grenadetrail"] = "Следы гранат",
            ["smoke"] = "Цветной дым",
            ["equipment"] = "Снаряжение",
            ["sound"] = "Звуки",
        },
        ["en"] = new()
        {
            ["pmm_sa.brand"] = "PMM · SIMPLEADMIN",
            ["pmm_sa.home"] = "Main",
            ["pmm_sa.back"] = "Back",
            ["pmm_sa.close"] = "Close",
            ["pmm_sa.count"] = "{0} total",
            ["pmm_sa.page"] = "{0} / {1}",
            ["pmm_sa.next"] = "Next",
            ["pmm_sa.settings"] = "Panel settings",
            ["pmm_sa.settings_title"] = "Panel settings",
            ["pmm_sa.style_now"] = "Now: {0}",
            ["pmm_sa.style_scroll"] = "Mouse wheel",
            ["pmm_sa.style_pages"] = "Pages",
            ["pmm_sa.style_keys"] = "Number keys 1–0",
            ["pmm_sa.style_scroll_hint"] = "mouse",
            ["pmm_sa.style_pages_hint"] = "mouse",
            ["pmm_sa.style_keys_hint"] = "test first",
            ["pmm_sa.selected"] = "ON",
            ["pmm_sa.keys_test"] = "Number keys test",
            ["pmm_sa.keys_title"] = "Number keys test",
            ["pmm_sa.keys_sub"] = "Press 1 2 3 4 5 6 7 8 9 0 · got {0} / 10",
            ["pmm_sa.keys_progress"] = "Pressed {0} / 10 · left: {1}",
            ["pmm_sa.keys_done"] = "All 10 keys work",
            ["pmm_sa.keys_enable"] = "Use the number keys menu",
            ["pmm_sa.keys_again"] = "Test again",
            ["pmm_sa.keys_help1"] = "Nothing lights up? The bind line is in chat and console",
            ["pmm_sa.keys_help2"] = "Copy it from the console (~), paste it there, press Enter",
            ["pmm_sa.keys_help3"] = "No key for {0} s - back to the settings",
            ["pmm_sa.binds"] = "Binds for the number keys",
            ["pmm_sa.binds_hint"] = "to chat and console",
            ["pmm_sa.binds_chat"] = "Number keys menu: copy the line from the console (~), paste it into the console, press Enter. Once.",
            ["pmm_sa.target"] = "Target: {0}",
            ["pmm_sa.on"] = "ON",
            ["pmm_sa.off"] = "OFF",
            ["pmm_sa.server_line"] = "{0} · {1} / {2} players · you: {3}",
            ["pmm_sa.now"] = "current",
            ["pmm_sa.workshop"] = "workshop",

            ["SimpleAdmin"] = "Main menu",
            ["Players Manage"] = "Manage players",
            ["Server Manage"] = "Manage server",
            ["Admins Manage"] = "Admins",
            ["Players Manage|crumb"] = "Players",
            ["Server Manage|crumb"] = "Server",
            ["Admins Manage|crumb"] = "Admins",
            ["Fun Commands|crumb"] = "Fun",
            ["Change Map|hint"] = "{map}",
            ["Plugins Manage|hint"] = "@css/root",
            ["Gag|hint"] = "chat",
            ["Mute|hint"] = "voice",
            ["Silence|hint"] = "chat + voice",
            ["Slap|hint"] = "damage",
            ["Swap|hint"] = "CT ⇄ T",
            ["Spec"] = "Spectators",
            ["Force Team"] = "Change team",
            ["History"] = "Penalty history",

            ["pmm_sa.empty"] = "Nothing here",
            ["pmm_sa.gone"] = "The player has left",
            ["pmm_sa.error"] = "Error: {0}",
            ["pmm_sa.error_title"] = "Error",
            ["pmm_sa.t_forever"] = "Forever",
            ["pmm_sa.t_min"] = "{0} min",
            ["pmm_sa.t_hour"] = "{0} h",
            ["pmm_sa.t_day"] = "{0} d",
            ["pmm_sa.cat_vip"] = "VIP Manage",
            ["pmm_sa.vip_give"] = "Give / change VIP",
            ["pmm_sa.vip_remove"] = "Remove VIP",
            ["pmm_sa.vip_list"] = "VIP online",
            ["pmm_sa.vip_reload"] = "Reload VIP",
            ["pmm_sa.vip_reload_do"] = "Reload the VIPCore config",
            ["pmm_sa.vip_none"] = "no VIP",
            ["pmm_sa.vip_nobody"] = "No VIP online",
            ["pmm_sa.vip_forever"] = "forever",
            ["pmm_sa.vip_until"] = "until {0}",
            ["pmm_sa.vip_group_title"] = "VIP group: {0}",
            ["pmm_sa.vip_time_title"] = "VIP term: {0}",
            ["pmm_sa.vip_remove_title"] = "Remove VIP: {0}",
            ["pmm_sa.vip_given"] = "{0}: VIP {1} for {2}",
            ["pmm_sa.vip_removed"] = "{0}: VIP removed",
            ["pmm_sa.vip_reloaded"] = "VIPCore config reloaded",
            ["pmm_sa.vip_noauth"] = "{0}: Steam has not confirmed the player yet, try again in a minute",
            ["pmm_sa.cat_maps"] = "Maps Manage",
            ["pmm_sa.maps_change"] = "Change map",
            ["pmm_sa.maps_next"] = "Next map",
            ["pmm_sa.maps_votes"] = "Votes",
            ["pmm_sa.maps_pools"] = "Map pools",
            ["pmm_sa.maps_random"] = "Random map",
            ["pmm_sa.maps_random_hint"] = "now",
            ["pmm_sa.maps_vote_change"] = "Vote and change",
            ["pmm_sa.maps_vote_change_hint"] = "change at once",
            ["pmm_sa.maps_vote_next"] = "Vote for the next map",
            ["pmm_sa.maps_vote_next_hint"] = "at map end",
            ["pmm_sa.maps_vote_yesno"] = "Change the map?",
            ["pmm_sa.maps_vote_yesno_hint"] = "yes / no",
            ["pmm_sa.maps_pool_vote"] = "Pool vote",
            ["pmm_sa.maps_count"] = "{0} maps",
            ["pmm_sa.maps_no_pools"] = "No map pools",
            ["pmm_sa.maps_changing"] = "Changing the map: {0}",
            ["pmm_sa.maps_next_set"] = "Next map: {0}",
            ["pmm_sa.maps_pool_set"] = "Map pool: {0}",
            ["pmm_sa.maps_pool_failed"] = "Pool {0} is empty or missing",
            ["pmm_sa.maps_vote_busy"] = "A vote is already running",
            ["pmm_sa.maps_vote_started"] = "Vote started",
            ["pmm_sa.cat_store"] = "Store Manage",
            ["pmm_sa.store_give"] = "Give credits",
            ["pmm_sa.store_take"] = "Take credits",
            ["pmm_sa.store_item"] = "Give item",
            ["pmm_sa.store_credits"] = "{0} cr.",
            ["pmm_sa.store_after"] = "becomes {0}",
            ["pmm_sa.store_count"] = "{0} items",
            ["pmm_sa.store_has"] = "owned",
            ["pmm_sa.store_given"] = "{0}: +{1} credits, now {2}",
            ["pmm_sa.store_taken"] = "{0}: −{1} credits, now {2}",
            ["pmm_sa.store_item_given"] = "{0}: got item {1}",
            ["pmm_sa.store_item_failed"] = "{0}: item {1} was not given",
            ["VIP Manage|crumb"] = "VIP",
            ["Maps Manage|crumb"] = "Maps",
            ["Store Manage|crumb"] = "Store",
            ["Reload the VIPCore config|hint"] = "css_vip_reload",
            ["playerskin"] = "Player skins",
            ["customweapon"] = "Weapon skins",
            ["trail"] = "Trails",
            ["tracer"] = "Tracers",
            ["grenadetrail"] = "Grenade trails",
            ["smoke"] = "Coloured smoke",
        },
    };
}
