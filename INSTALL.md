# PMM_SimpleAdmin 0.1.1

## RU

Меню [CS2-SimpleAdmin](https://github.com/daffyyyy/CS2-SimpleAdmin) (1.9.x) на своей панели PanoramaMenuManager
(вариант дизайна «Классика+»): красная панель слева, список без страниц (крутится колёсиком мыши), путь по меню, значки разделов,
цвет команды и `#userid` у игроков, «›» у пунктов с подменю, опасные действия красным.

SimpleAdmin **не пересобирается**. Он строит меню через MenuManagerApi как обычно. Harmony-постфикс на его `CreateMenu`
помечает каждое созданное меню, перехват `MenuInstance.Open` ядра PMM ставит помеченному меню тип PanoramaMenu, ядро отдаёт его модулю-художнику
(`pmm:paint`), и PMM_SimpleAdmin рисует его на своей панели. Клик вызывает обработчик самого SimpleAdmin — права,
логи, баны в БД работают как раньше.
Модули SimpleAdmin (FunCommands и свои, через API `RegisterMenuCategory` / `RegisterMenu`) попадают на панель сами.

### Что нужно

- CounterStrikeSharp 1.0.378+
- PanoramaMenuManager (MenuManagerCore) **1.2.04+**, лучше **1.2.05** (заглушки PlayerSettings / AnyBaseLib для SimpleAdmin) и `shared/MenuManagerApi` той же версии
- CS2-SimpleAdmin 1.9.x. `MenuConfigs.MenuType` в его конфиге может быть любым — при `ForcePanorama: true` панель открывается всегда.

### Сервер

Скопируй `Server-plugins/counterstrikesharp` в `game/csgo/addons/`. Получится:

- `addons/counterstrikesharp/plugins/PMM_SimpleAdmin/PMM_SimpleAdmin.dll`
- `addons/counterstrikesharp/plugins/PMM_SimpleAdmin/harmony/0Harmony.dll` — в папке `harmony`, не рядом с dll
- `addons/counterstrikesharp/configs/plugins/PMM_SimpleAdmin/PMM_SimpleAdmin.json`

`MenuManagerApi.dll` в папку плагина не кладётся — только в `shared/`. После замены dll перезапусти сервер.

### Клиентский аддон

Скопируй `Content-addonmanager/panorama` в корень аддона MultiAddonManager (туда же, где `menu_ui.xml` ядра),
собери аддон в Workshop Tools и залей. Файлы:

- `panorama/layout/custom_game/pmm_admin.xml`
- `panorama/styles/custom_game/pmm_admin.css`

Пока у игрока нет нового аддона, `!admin` у него открывает пустую панель — нужно перезайти после обновления аддона.

### Кому показывается панель

При `ForcePanorama: true` (по умолчанию) — всем админам, что бы ни было выбрано в `!menu` (WASD, CSGO Menu, чат)
и в `MenuConfigs.MenuType` SimpleAdmin. При `false` — только игрокам с PanoramaMenu (мышь) в `!menu`.

### Конфиг `PMM_SimpleAdmin.json`

Файл — JSON с комментариями (`//`, RU + EN): каждый ключ описан прямо в нём. CounterStrikeSharp читает только `.json`,
поэтому имя остаётся `PMM_SimpleAdmin.json`. После последнего элемента блока запятую не ставить. Если новая версия
дописывает в файл недостающие ключи, комментарии пропадают — старый файл сохраняется рядом как `PMM_SimpleAdmin.json.bak`.

| Ключ | Что делает |
|---|---|
| `Enabled` | `false` — SimpleAdmin снова в обычных меню PMM |
| `ForcePanorama` | `true` — панель всегда; `false` — только у игроков с PanoramaMenu в `!menu` |
| `Debug` | строки `[debug]` в логе сервера при каждом открытии меню PMM (то же, что `css_pmm_sa debug`) |
| `LayoutPath` | путь к layout панели |
| `Position` | `Left` / `Center` / `Right` |
| `Accent` | цвет шапки: `Red`, `Bright`, `Dark`, `Coral` |
| `OpenDelay`, `InputDelay` | пауза перед показом и перед захватом мыши (секунды) |
| `DefaultStyle` | стиль меню, пока игрок не выбрал свой: `Scroll` (колёсико, без цифр), `Pages` (страницы, без цифр), `Keys` (цифры: 1–7 выбор, 8 назад, 9 далее, 0 закрыть) |
| `ShowSettings` | пункт «Настройки панели» в главном меню (стиль меню, проверка цифр). Выбор игрока хранится в базе (см. «Где хранится стиль меню») |
| `SettingsIcon` | значок пункта настроек |
| `KeyTestTimeout` | проверка цифр: через столько секунд без нажатий — назад в настройки |
| `ShowCategoryCounts` | числа справа у разделов главного меню (сколько пунктов доступно админу) |
| `ShowServerLine` | строка «карта · игроки / слоты» в главном меню |
| `ShowUserId` | `#userid` справа у игроков |
| `SourceAssemblies` | сборки, чьи меню рисует панель (по умолчанию `CS2-SimpleAdmin`) |
| `VipCore`, `MapChooser`, `Store` | разделы других плагинов в админке (см. «Разделы VIP / Карты / Магазин») |
| `Items` | вид пунктов по тексту SimpleAdmin: `Icon` (значок), `Danger` (красный), `Arrow` («›» сразу) |

Значки: teamplayer settings shield star key leader shoppingcart random gift kill clock warning lock refresh trophy
crosshair health fast parachute dollar_sign coin_stack bullet inventory timer check clantag sound_3 loadout colorwheel.

«›» модуль запоминает сам: после первого клика по пункту он знает, открывает ли тот подменю.

### Где хранится стиль меню

Подключение берётся **только из конфига SimpleAdmin** (`configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json`, блок `DatabaseConfig`), своих настроек базы у модуля нет:

- `DatabaseType: MySQL` — таблица `pmm_sa_players` (steamid, style, updated_at) в той же базе MySQL. Если на эту базу смотрят несколько серверов, стиль у админа общий. При входе игрока стиль перечитывается.
- `DatabaseType: SQLite` — свой файл `configs/plugins/PMM_SimpleAdmin/pmm_simpleadmin.sqlite`. Файл SimpleAdmin не трогается: SimpleAdmin работает с SQLite через System.Data.SQLite, модуль — через e_sqlite3, две копии SQLite на одном файле в одном процессе ломают друг другу блокировки.

Рядом с dll лежат `MySqlConnector.dll`, `Microsoft.Data.Sqlite.dll`, `SQLitePCLRaw.*.dll` и `runtimes/<платформа>/native/` — копировать вместе с плагином.
Linux-библиотека SQLite та же, что у ядра PMM (до GLIBC_2.28). Старый `players.json` один раз переносится в базу и переименовывается в `players.json.imported`.
`css_pmm_sa` показывает, какая база используется.

### Меню по цифрам

В «Настройки панели» → «Цифры 1–0» сначала идёт проверка: мышь отпускается, на панели 10 клавиш, каждая загорается,
когда сервер получил `css_N`. Строка с биндами сразу приходит в чат и в консоль — её один раз вставляют в консоль:

```
spec_usenumberkeys_nobinds 0;bind "1" "css_1; slot1";bind "2" "css_2; slot2";bind "3" "css_3; slot3";bind "4" "css_4; slot4";bind "5" "css_5; slot5";bind "6" "css_6; slot6";bind "7" "css_7; slot7";bind "8" "css_8; slot8";bind "9" "css_9; slot9";bind "0" "css_0; slot10"
```

Когда загорелись все 10: 1 — включить меню по цифрам, 2 — проверить ещё раз, 8 — назад, 0 — закрыть.
В этом стиле мышь не захватывается: 1–7 выбор, 8 назад / пред. страница, 9 далее, 0 закрыть.
Команды `css_0`…`css_9` регистрирует ядро PMM, модуль только слушает их.

### Разделы VIP / Карты / Магазин

Если на сервере стоит плагин, в главном меню SimpleAdmin сам появляется его раздел (через API SimpleAdmin, с его
проверкой прав, «Назад» и логом команд). Нет плагина — нет раздела, ничего не ломается. Все три блока пишутся в
`PMM_SimpleAdmin.json` с первого запуска (в уже существующий файл модуль дописывает только недостающие ключи) —
настроить один раз, даже если плагина пока нет.

У каждого блока: `Enabled` — раздел вкл/выкл, `Permission` — кто видит раздел (один флаг `@css/...` или группа `#css/...`),
`Menus` — пункты раздела, у каждого `Enabled` и свой `Permission` (пусто = хватает флага раздела).

**`VipCore`** — [cs2-VIPCore](https://github.com/partiusfabaa/cs2-VIPCore) 1.x и 2.x, раздел «Управление VIP» (по умолчанию `@css/root`):

| Пункт | Что делает |
|---|---|
| `give` | игрок (справа его группа и срок) → группа VIP → срок. Нет VIP — выдаёт (`css_vip_adduser`), есть — меняет группу/срок (`css_vip_updateuser`) и перечитывает игрока (`css_reload_vip_player`) |
| `remove` | только игроки с VIP → «Снять VIP» (`css_vip_deleteuser`) |
| `list` | VIP онлайн с группой и сроком, клик — сменить группу/срок |
| `reload` | `css_vip_reload` — перечитать конфиг VIPCore |

`Durations` — сроки в **минутах** (`0` = навсегда); модуль сам переводит их в `TimeMode` из `vip_core.json`
(секунды / минуты / часы / дни). `ShowBots` — показывать ботов в списках.

**`MapChooser`** — [GG1MapChooser](https://github.com/ssypchenko/GG1MapChooser), раздел «Управление картами» (по умолчанию `@css/changemap`):

| Пункт | Что делает |
|---|---|
| `change` | «Случайная карта» (`ggmc_auto_mapchange`) и все карты активного пула GG1 (имена `display`, workshop, «сейчас») → `ggmap <карта>` |
| `nextmap` | те же карты → `setnextmap <карта>` (смена в конце карты) |
| `votes` | голосование со сменой сразу (`ggmc_mapvote_with_change`), за следующую карту (`ggmc_mapvote_start`), «Менять ли карту?» (да / нет) |
| `pools` | пулы карт GG1 (число карт, текущий) → сделать активным; «Голосование за пул» |

`RedirectMapsCommand: true` — `!maps` GG1MapChooser открывает этот раздел панели вместо меню GG1 (Harmony на `MapMenuCommand`;
у кого нет флага раздела — остаётся меню GG1). `RedirectSimpleAdminChangeMap: true` — «Управление сервером → Сменить карту»
SimpleAdmin показывает карты GG1MapChooser вместо списков `DefaultMaps` / `WorkshopMaps`; при выгрузке модуля пункт SimpleAdmin возвращается.

**`Store`** — [cs2-store](https://github.com/schwarper/cs2-store), раздел «Управление магазином» (по умолчанию `@css/root`):

| Пункт | Что делает |
|---|---|
| `give_credits` | игрок (справа его кредиты) → сумма из `Credits` (справа «станет N») |
| `take_credits` | то же, но забирает (не ниже 0) |
| `give_item` | игрок → тип предметов → предмет (справа цена; «уже есть» — серым) |

Имена разделов и пунктов — ключи `pmm_sa.cat_*`, `pmm_sa.vip_*`, `pmm_sa.maps_*`, `pmm_sa.store_*` в файле переводов;
типы предметов магазина переименовываются как пункты (`"playerskin": "Скины игрока"`).
`css_pmm_sa` показывает, какие разделы подключены; `css_pmm_sa integrations` — подключить заново (например, после загрузки плагина).

### Переводы и переименования

Секция `PMM_SimpleAdmin` в `configs/plugins/MenuManagerCore/MenuManager_Modules_Translation.json` (ru / en).
Добавляется при первом запуске, потом файл твой: модуль только дописывает недостающие ключи.

- `pmm_sa.*` — тексты панели (Назад, Закрыть, строка сервера, ВКЛ / ВЫКЛ …)
- `"<текст SimpleAdmin>|crumb": "<коротко>"` — короткое имя в строке пути, например `"Управление игроками|crumb": "Игроки"`.
- В подсказке `{map}` заменяется на текущую карту (`"Сменить карту|hint": "{map}"`).
- `"<текст SimpleAdmin>": "<новое имя>"` — переименование пункта или заголовка, например `"Выгнать": "Кикнуть"`.
  Ищется только на языке игрока.
- `"<текст SimpleAdmin>|hint": "<подсказка>"` — серый текст справа, например `"Заглушить|hint": "гэг"`.

### Команды

`css_pmm_sa` (консоль сервера) — состояние; `css_pmm_sa reattach` — заново найти PMM; `css_pmm_sa debug` — писать в лог сервера каждое открытие меню PMM (видно, узнано ли меню SimpleAdmin);
`css_pmm_sa integrations` — заново подключить разделы VIP / Карты / Магазин.

## EN

CS2-SimpleAdmin 1.9.x menus on an own PanoramaMenuManager panel (design "Classic+"): red panel on the left, 7 entries
per page, menu path, section icons, team colour and `#userid` on player rows, "›" on entries with a submenu,
dangerous actions in red. SimpleAdmin is not rebuilt: a Harmony prefix on PMM's `MenuInstance.Open` forces PanoramaMenu
for menus opened by SimpleAdmin code, PMM hands them to the painter (`pmm:paint`) and this module draws them; clicks call
SimpleAdmin's own callbacks.

Needs PanoramaMenuManager 1.2.04+ (1.2.05 recommended). Server: copy `Server-plugins/counterstrikesharp` into
`game/csgo/addons/` (keep `harmony/0Harmony.dll` in its folder) and restart. Client: add `Content-addonmanager/panorama`
to the MultiAddonManager addon and rebuild it. With `ForcePanorama: true` (default) every admin gets the panel,
whatever the `!menu` choice or SimpleAdmin's `MenuType`. Texts and renames: section `PMM_SimpleAdmin` of `configs/plugins/MenuManagerCore/MenuManager_Modules_Translation.json`.
Integrations (all blocks are in the config from the first start; a missing plugin just means no section):
`VipCore` (cs2-VIPCore 1.x and 2.x: give / change / remove VIP, VIP online, reload; terms in minutes converted to VIPCore's `TimeMode`),
`MapChooser` (GG1MapChooser: change map, next map, votes, map pools; `!maps` and SimpleAdmin's "Change map" lead to it),
`Store` (cs2-store: give / take credits, give items). Each has `Enabled`, `Permission` and per-entry `Menus`.
Console: `css_pmm_sa`, `css_pmm_sa reattach`, `css_pmm_sa debug`, `css_pmm_sa integrations`.
Config: `configs/plugins/PMM_SimpleAdmin/PMM_SimpleAdmin.json` is JSON with comments (`//`, RU + EN) for every key; CounterStrikeSharp
reads only `.json`, so the name stays. When a new version writes missing keys, the comments are lost and the old file is kept as `PMM_SimpleAdmin.json.bak`.
