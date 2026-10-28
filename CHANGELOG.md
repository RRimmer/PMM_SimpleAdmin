# PMM_SimpleAdmin

## 0.1.1

### RU

- VIPCore 2.0.0 (partiusfabaa): «Управление VIP» снова выдаёт, меняет и снимает VIP. В VIPCore 2 методы API переименованы (`IsClientVip` → `IsPlayerVip`, `GiveClientVip` → `GivePlayerVip`, `SetClientVip` → `SetPlayerVip`, `RemoveClientVip` → `RemovePlayerVip`), срок VIP и `TimeMode` лежат в других местах.
- Модуль сам определяет VIPCore 1.x или 2.x, со старым VIPCore всё работает как раньше. В VIPCore 2 смена группы / срока применяется к игроку сразу, без `css_reload_vip_player`.
- `css_pmm_sa` показывает в строке `integrations`, какой VIPCore найден (`VIPCore 2.x` / `1.x`).
- Сборка под CounterStrikeSharp.API 1.0.378 (MenuManagerApi требует ≥ 1.0.377; раньше сборка падала с NU1605).
- `PMM_SimpleAdmin.json` в архиве с комментариями (`//`, RU + EN) к каждому ключу; исходник — `PMM_SimpleAdmin.example.jsonc`. Когда модуль дописывает новые ключи, прежний файл с комментариями сохраняется как `PMM_SimpleAdmin.json.bak`.

### EN

- VIPCore 2.0.0 (partiusfabaa): "VIP Manage" gives, changes and removes VIP again. VIPCore 2 renamed the API methods (`IsClientVip` → `IsPlayerVip`, `GiveClientVip` → `GivePlayerVip`, `SetClientVip` → `SetPlayerVip`, `RemoveClientVip` → `RemovePlayerVip`) and keeps the VIP expiry and `TimeMode` elsewhere.
- VIPCore 1.x or 2.x is detected by itself; the old VIPCore works as before. With VIPCore 2 a new group / term applies to the player at once, without `css_reload_vip_player`.
- `css_pmm_sa` shows the VIPCore generation in the `integrations` line.
- Built against CounterStrikeSharp.API 1.0.378 (MenuManagerApi needs ≥ 1.0.377; the build failed with NU1605 before).
- `PMM_SimpleAdmin.json` in the archive has comments (`//`, RU + EN) for every key; the source is `PMM_SimpleAdmin.example.jsonc`. When the module writes new keys, the old commented file is kept as `PMM_SimpleAdmin.json.bak`.

## 0.1.0

### RU

- Первая версия. Меню CS2-SimpleAdmin 1.9.x на своей панели PMM (вариант «Классика+», красная тема).
- Без пересборки SimpleAdmin: модуль — художник PMM (`pmm:paint`), меню SimpleAdmin помечаются при создании (Harmony-постфикс на его `CreateMenu`).
- Панель открывается всегда (`ForcePanorama`): Harmony-перехват `MenuInstance.Open` ядра PMM ставит меню SimpleAdmin тип PanoramaMenu — неважно, что выбрано в `!menu` и в `MenuType` SimpleAdmin. `0Harmony.dll` — в `plugins/PMM_SimpleAdmin/harmony/`.
- Без страниц: список до 64 пунктов крутится колёсиком мыши (видно 8, короткие меню — по размеру). Кнопки «Назад» / «Закрыть», путь по меню («Главное › Игроки ›»).
- Заголовки «Действие Продолжительность: ник» → «Срок» + строка «Цель: ник». Плагины SimpleAdmin — плашки ВКЛ / ВЫКЛ, «Назад» возвращает в меню сервера.
- «Настройки панели» в главном меню — стиль меню у каждого игрока свой (база SimpleAdmin: MySQL — таблица `pmm_sa_players`, SQLite — свой файл `pmm_simpleadmin.sqlite`): прокрутка колёсиком (по умолчанию, без цифр), страницы (без цифр), цифры 1–0 (1–7 выбор, 8 назад, 9 далее, 0 закрыть, мышь свободна).
- Проверка цифр перед включением: 10 клавиш загораются по `css_1`…`css_0`, строка биндов уходит в чат и консоль, выход по таймеру.
- Числа справа у разделов главного меню — сколько пунктов админу доступно (те же проверки прав, что у SimpleAdmin).
- Подсказки справа больше не заезжают под полосу прокрутки.
- Меню команды: цвета команд и «сейчас» у текущей команды цели. «Сменить карту» — текущая карта справа.
- Главное меню: значки разделов и строка «карта · игроки / слоты».
- Игроки: цвет команды (CT / T / наблюдатели) и `#userid`. Игрок берётся из самого пункта, а не по нику.
- «›» у пунктов с подменю (запоминается после первого клика), опасные действия красным, текущая карта помечена «сейчас», workshop-карты — «workshop».
- Переименования пунктов и подсказки справа — секция `PMM_SimpleAdmin` в `MenuManager_Modules_Translation.json`.
- Модули SimpleAdmin (FunCommands и др.) рисуются на той же панели.
- Разделы других плагинов в главном меню (появляются сами, если плагин на сервере; блоки в конфиге — сразу, с флагами раздела и каждого пункта):
  - «Управление VIP» (cs2-VIPCore): выдать / изменить VIP (группа → срок), снять VIP, VIP онлайн, перезагрузка конфига. Сроки — в минутах, переводятся в `TimeMode` VIPCore.
  - «Управление картами» (GG1MapChooser): сменить карту (+ случайная), следующая карта, голосования, пулы карт. `!maps` и «Управление сервером → Сменить карту» ведут сюда.
  - «Управление магазином» (cs2-store): выдать / забрать кредиты, выдать предмет.
- Новые ключи конфига дописываются в уже существующий `PMM_SimpleAdmin.json` (значения не меняются).
- Исправлено: на английском «Back» показывалось как «Сзади» — клиент переводил одиночное английское слово своим переводом игры.

### EN

- First version: CS2-SimpleAdmin 1.9.x menus on an own PMM panel ("Classic+", red), no SimpleAdmin rebuild; Harmony prefix on PMM `MenuInstance.Open` makes the panel open for every admin (`ForcePanorama`).
- Sections for cs2-VIPCore (VIP give / change / remove / list / reload), GG1MapChooser (change map, next map, votes, pools; `!maps` and SimpleAdmin "Change map" redirected) and cs2-store (credits, items). Config blocks exist from the first start; new keys are added to an existing config.
- Fixed: "Back" showed as "Сзади" on a Russian game client (one-word values went through the game's localization).
