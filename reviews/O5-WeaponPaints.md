# O5 — WeaponPaints

Дата:2026-11-10. Функции: accepted (владелец); проверки кода/SQL/native: passed; полный тик под реальными клиентами: awaiting_live. Готовность PR означает готовность к ревью, а не подтверждённый общий FPS/CPU-выигрыш.

Repo/PR: [koiie111/cs2-WeaponPaints#3](https://github.com/koiie111/cs2-WeaponPaints/pull/3), codex/weaponpaints-optimization → main. Baseline ba00df35; исходная DLL metadata72815ac, деревья идентичны d070870689ff81a005489704e19659aff71c42c2. Финальный runtime3220421; CI/test workflow46576b6. Release metadata46576b655b1035349f247536e992c7b8c6a0768e, SHA256cceeee922f1209a9a9d77c813a54ada3203c2a640fbf3337246197f1484f10af. Mapping: mapped.

## Что исправлено и сохранено
- OnTick: готовый HTML/срок/получатель только для активных превью, SteamID/serial-защита, очистка disconnect/map/unload; частота и2с сохранены. Выключенная функция не подписывает OnTick.
- Каталоги/меню: индексы legacy/paint/image/weapon/knife, исходный порядок и первый результат/fallback; один random paint на применение. Native give и entity callback остаются отдельными; атрибуты и deferred knife paint сохранены.
- БД: async отдельный снимок с проверкой request/session/lifecycle перед публикацией, копия перед сохранением, FIFO одного SteamID для загрузок/сохранений/StatTrak; явные ошибки/ограниченные повторы. Фон не читает актуальный slot нового владельца и не мутирует WeaponInfo перчаток.
- Lifecycle: serial/pawn/generation guards, очистка one-shot таймеров, независимые spawn feature flags и правильный ItemIDHigh. Повторный OnAllPluginsLoaded не дублирует команды.
- Курицы: индекс актуальных entity handles вместо полного мира на каждого владельца, владелец читается заново; приоритет native, ограниченные появления/retry, шляпа удаляется сразу при смерти. Постоянного сканирования мира по тикам нет.
- Paint refresh: объединённые запросы; старый serial должен исчезнуть до штатной выдачи замены; патроны/режимы/attack timing/active-last сохранены. Гранаты/бомба не пересоздаются. Смена типа активного ножа передостаёт его через уже имеющееся оружие; dummy-предметов нет. История unsafe raw diagnostic DispatchSpawn отдельно разобрана в предыдущем отчёте.

Команды, конфигурация, MenuManager capability, таблицы/сайт, .NET10 и зависимости сохранены. CSS API package1.0.376, фактический серверCSS1.0.378; runtime/core не обновлялись.

## Проверки и приёмка
Release --no-restore -warnaserror;22pet tests;103реальных MySQL assertions (включая64комбинации флагов, поздние загрузки, ошибки/locks/FIFO);28каталогов/57876записей: passed, CI тоже. Pure preview0/1/8/32, replacement/expiry/SteamID/controller проверены. Native cached recipient: alive, неверный serial, disconnect/reconnect, expiry, empty-image passed. Native reconnect использовал другой slot; same-slot предикаты проверены моделью, не объявляются доказанным принудительным native-reuse.

Владелец подтвердил все перечисленные ему игровые сценарии на perf.11, включая исправления шляпы, двойного !wp, немедленного paint и типа ножа в руках. Дополнительное изменение perf.12 сохраняет HTML/частоту/TTL и проверено автоматикой/native-вызовами.

На пустом#7, de_mirage_fps,8парковочных ботов/8превью/32курицы: baseline→candidate→baseline,3×30с на серию; отдельные control/refresh окна;623с наблюдения,4992запроса обновления,0fixture errors,2стабильных timer references. Исправленный отдельный инспектор очередей:8world/ownedAK, queues0, chicken handles32; после stop состояние0. Managed heap114.02–117.25MB (+2.77MB по краям), RSS-медиана первой/последней минуты1657.74/1509.17MiB; это ограниченное наблюдение, не доказательство отсутствия всех утечек.

| Метрика | Baseline до | perf.12 | Baseline после |
|---|---:|---:|---:|
| OnTick mean / p99, мкс |179.18 /306.15|185.77 /306.15|182.92 /332.59|
| Paint mean / p99, мкс |805.60 /1781.54|76.71 /182.05|865.00 /1761.02|
| NativePets mean / p99, мкс |308.36 /636.12|35.33 /79.61|348.22 /684.87|
| OnTick bytes/call |1600|512|1600|
| Paint bytes/call |53496|3440|53496|
| NativePets bytes/call |118368|13912|118368|
| Объединение instrumented scopes, ms/s |20.842|13.187|21.859|

Аллокации inclusive, не суммировать вложенные метки. Процентили пересчитаны из raw calls, не усреднены. Разница выбранных instrumented scopes наблюдается в этой синтетике; не является процентом ускорения сервера. Контроль частоты считает HTML API-вызовы ботам, не реальную сеть/клиентский рендер. CPU всего процесса около31% одного logicalCPU, причинный глобальный выигрыш не установлен. SQL/неактивные pet-emote/lifecycle timers внутри этих окон не профилировались; их correctness проверена отдельно.

FrameTotal после холодного старта только с ботами не собирается; entity-think span и wall tick interval не заменяют полный тик. p95/p99 полного тика и150мс остаются неподтверждёнными. Изменился hash SimpleAdmin data/admins.json вне действий теста; текущие данные сохранены, timing/содержимое изменения не захвачены. Это ограничение сравнений whole-process; остальные DLL/shared/configs/gamedata неизменны.

Решение: функции accepted; наблюдаемый выигрыш выбранной синтетической работы/аллокаций; **общий live perf-target не заявлен**. Код готов к ревью с этими границами. Для утверждения об ускорении всего сервера нужен отдельный сопоставимый профиль с реальными клиентами/full-frame telemetry.

## Завершение и артефакты
После испытаний#7 возвращён на исходную3.3a-db6, SHA256640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610. Проверены exactLOADED,0humans/0bots, runtime flags/config hash; диагностических модулей/команд нет,24remote exports выгружены/удалены,0новых WP warnings/errors. Другие серверы не перезапускались, merge/fleet/site/schema rollout не выполнялись.

Сборка и rollback: C:/Users/koiie/.codex/tasks/wp-release-perf12-20261110/WeaponPaints.zip и README.txt; проверенный DLL встроен в полный пакет CI data/lang/dependencies/gamedata. [Raw, manifests, probes, analysis, release SHA256](../docs/measurements/active-20261110/REPORT.md). Существующая зависимость MySqlConnector2.5.0 даёт NuGet advisory при fresh restore; обновление не смешано с оптимизацией, условия и ссылка указаны в полном отчёте.
