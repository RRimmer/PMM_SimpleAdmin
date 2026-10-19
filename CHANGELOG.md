# PMM_SimpleAdmin

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
