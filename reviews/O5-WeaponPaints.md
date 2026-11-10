# O5 — WeaponPaints

## Повторное ревью по полному плану — 10.11.2026

Проверен head `e42b219` и полный пакет `4ill.ru/main` на `de808a8e`:
README, STAGES, VERIFICATION, MEASUREMENTS, SOURCES, PROMPTS, PROGRESS,
оба source-lock, FLEET-AUDIT/COVERAGE/STAGES и все опубликованные reviews.
Прочитаны исторические отчёты 20.10 и план 28.10; архив повторно проверен:
9 профилей, 154758 вызовов, max 4.790674 мс. Текущий diff сравнивается с
`ba00df35`, а не с историческими db3/db5. Равенство деревьев `72815ac` и
`ba00df35` (`d070870689ff81a005489704e19659aff71c42c2`) проверено через Git.

Результат: кодовое требование O5 выполнено. Feature gate сохранён, готовый HTML
строится при выборе, OnTick обходит только активные превью, cadence и двухсекундный
срок сохраняются; новая картинка заменяет старую без гонки старого таймера.
Disconnect/map/unload очищают состояние. Изменений конфигурации, схемы, сайта,
gamedata, shared API и package versions относительно baseline нет.

PR шире исходного узкого задания ShowSkinImage: включает асинхронные снимки/FIFO,
каталоги, питомцев и перевыдачу оружия. Последняя особенно рискованна и не нужна
для кэширования HTML; она исправляет отдельно обнаруженный дефект видимого paint
и уже прошла записанную владельцем игровую приёмку. Эти изменения не объявляются
необходимыми условиями O5 и не маскируются процентом OnTick. При повторном ревью
проверены serial/pawn/generation guards, ограниченные повторы, сохранение
патронов/режимов/слотов, cleanup и неизменность SQL-контрактов. Нового блокирующего
дефекта не обнаружено.

Закрыты два пробела проверки:

- Старые portable preview tests не вызывали production OnTick. Он без изменения
  поведения выделен в `SkinPreviews.cs` вместе с gate/cleanup; новая suite напрямую
  компилирует этот partial и `PreviewState.cs`. 47 проверок: gate, 0/1/8/32 active,
  sends/reference reuse, alive/dead, replacement/expiry, empty image, SteamID/serial,
  disconnect/same-slot replacement, cleanup, managed allocations. Внешняя граница
  CSS заменена адаптерами; это не повторная native/визуальная приёмка.
- Проверка evidence теперь независимо пересчитывает все 33 pooled distributions
  и три парных interval unions/эффекта из raw calls. SHA256 78 файлов и 9 lossless
  Speedscope exports (62617 calls) также проверены; старые raw/evidence не изменены.

Повторно пройдены .NET SDK 10.0.400, Release `--no-restore -warnaserror` (0 warnings),
22 pet checks, optimization suite с 28 реальными каталогами/57876 строками и
103 assertions на отдельной localhost MySQL 8.4.13. Случайная тестовая schema
удалена в finally, только созданный для ревью контейнер удалён. Baseline собран
тем же SDK; fresh restore сообщает прежний NU1902 MySqlConnector 2.5.0 и поэтому
не проходит `-warnaserror`, обычная Release build проходит с этим предупреждением.
Уязвимость не скрыта, dependencies не обновлялись в perf-патче.

Команды: `dotnet build WeaponPaints.csproj -c Release --no-restore -warnaserror`;
`dotnet run --project tests/PetStateTests/PetStateTests.csproj -c Release --no-restore`;
`dotnet run --project tests/OptimizationTests/OptimizationTests.csproj -c Release --no-restore -- --catalog-directory website/data`;
`dotnet run --project tests/PreviewRuntimeTests/PreviewRuntimeTests.csproj -c Release`;
`WP_TEST_CONNECTION=<isolated localhost> dotnet run --project tests/MySqlIntegration/MySqlIntegration.csproj -c Release --no-restore`;
`python tests/verify_measurements.py docs/measurements/active-20261110`;
`git diff --check`. Новая suite и расширенный verifier включены в CI.

Числа ниже воспроизведены из сохранённых raw. OnTick allocations 1600 → 512
bytes/call (−68%); время OnTick практически прежнее. Paint и NativePets быстрее
в контролируемой bot-нагрузке, но её interval union включает fixture driver.
Изменение SimpleAdmin data/admins.json и отсутствие full-frame/реальных клиентов
исключают общий CPU/FPS causal claim. Цель O5 для WeaponPaints — устранить
повторную подготовку строки — доказана. O9 full-tick/performance acceptance и
причина 150 мс остаются открытыми.

Решение: реализация и доступное ревью завершены; после успешного CI PR можно
слить по текущему прямому поручению владельца. Серверные действия при этом ревью
не выполнялись. Последний refactor проверен offline; прежние native measurements
относятся к perf.12 до выделения partial. Новый пакет/манифест ревью описывается
отдельно от старой DLL, проверенной на #7. Статус выпуска — `awaiting_live` для
canary точного артефакта и full-tick под реальными клиентами.

## Исходный отчёт и серверная история

### Проверка перед merge по новому поручению владельца

После повторного ревью владелец отдельно поручил проверить функциональность на
пустом сервере и слить PR при успехе. Проверена **точная новая DLL** из code commit
`e0c1b889d7bb32b8714662e9ef7dd3af270191a8`, SHA256
`e075ea052367cf8decb444d07c9c2f4d050a44e292e50cbde4796d66fc4578ac`.
Публичный #7, LinuxGSM `cs2_7`, `109.195.19.84:27075`, de_mirage_fps,
CSS 1.0.378; перед изменениями и после завершения 0 humans / 0 bots.
Другие экземпляры не менялись; профилирование не запускалось.

Два предварительных запуска остановились до сценариев из-за слишком раннего
опроса `status` при запуске. Неизвестный ответ не принимался за пустой сервер.
После второго прекращённого запуска исходник был восстановлен: 712 равных
хешей и 0 WP warnings/errors. Исправлен readiness collector: неизвестный статус
теперь ожидается, а обнаруженный человек по-прежнему запрещает restart.
Итоговый прогон выполнен через LinuxGSM stop → замена DLL на остановленном
экземпляре → start, без hot reload старой native-hook сборки.

Новая DLL загружена и сверена по SHA256. Прошли:

- стабильные 16 регистраций после повторного OnAllPluginsLoaded и четыре schema offsets;
- удаление native-шапки за два кадра после прямого вызова production post-damage
  handler с нулевым здоровьем курицы (это не новый ballistic/visual test);
- два paint на приобретённом ботом АК, патроны/reserve/attack timing/active slot,
  удаление старых serial и ровно один world AK после 100 запросов;
- четыре типа ножа 507/515/526/42, по 150 немедленных/перемежённых запросов:
  один нож, правильный definition/active, старый serial удалён, очередь пуста;
  сохранность serial/патронов основной пушки, knife-only и отмена после смерти;
- cached preview, неверный serial, disconnect/reconnect, expiry и empty image;
  движок выделил slot 1 → 2, поэтому forced same-slot native reuse не заявляется;
- перед cleanup все refresh/retiring/holster/deferred/knife-paint/DB queues равны 0.

После тестов helper-модули и боты удалены, исходная 3.3a-db6 восстановлена с
SHA256 `640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610`.
Config/runtime возвращены, exact LOADED и 0/0 подтверждены. **Все 712 хешей**
DLL/shared/config/gamedata/JSON совпадают с исходными; новых WP warnings/errors 0.
Новый пакет находится в `C:/Users/koiie/.codex/tasks/wp-review-live-20261110/WeaponPaints.zip`.
[Логи, fixtures, манифесты и release SHA256](../docs/measurements/review-20261110/result.json).
Новых функциональных регрессий в проверенных сценариях нет. Клиентский рендер
снова не тестировался; прежняя приёмка владельца сохранена. Условие владельца для
merge выполнено; full-tick/реальный активный онлайн и массовая выкладка остаются
отдельными действиями.

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
