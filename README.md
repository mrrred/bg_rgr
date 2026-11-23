# Автопредприятие на Windows

Проекты используют .NET 8 и Windows Desktop 8. Для сборки файла решения `BD_RGR.slnx` на Windows используйте SDK с поддержкой формата SLNX, например .NET 10. API использует Npgsql 10.0.3. [Пакет Npgsql](https://www.nuget.org/packages/Npgsql/10.0.3)

## Запуск

Команды приведены для PowerShell из корня `_rgz`. Создайте серверный файл конфигурации из шаблона, если его ещё нет.

```powershell
$localConfig = '.\BD_RGR\BD_RGR.Api\appsettings.Local.json'
if (-not (Test-Path -LiteralPath $localConfig)) {
    Copy-Item -LiteralPath .\info\config\appsettings.Local.example.json -Destination $localConfig
}
```

Заполните `Database.Host`, `Database.Port`, `Database.Name`, `Database.Accounts.Username` и `Database.Accounts.Password`. Для исходной настройки это `127.0.0.1`, `5432`, `mve` и `app_auth`. Для предметных запросов заполните `Username` и `Password` в `Database.BusinessRoles` для каждой используемой роли. API выбирает этот блок по роли из серверной сессии. Рабочие логины должны наследовать соответствующие группы ЛР5. Серверный `appsettings.Local.json` исключён из Git и поставки.

Для готовых отчётов и аналитики используются рабочие логины из `Database.BusinessRoles`. Для ручного SELECT выполните `04_report_reader.sql` в базе, указанной в `Database.Name`, от администратора PostgreSQL. Пример для `mve`.

```powershell
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
& $psql -X -h 127.0.0.1 -p 5432 -U postgres -d mve -v ON_ERROR_STOP=1 -f .\info\database\04_report_reader.sql
& $psql -X -h 127.0.0.1 -p 5432 -U postgres -d mve
```

В `psql` задайте пароль командой `\password u_report_reader` и выйдите через `\q`. Затем установите `Database.Reporting.Username` в `u_report_reader` и заполните `Database.Reporting.Password`. В ранее созданном серверном JSON добавьте раздел `Reporting` по обновлённому шаблону. После правок перезапустите API. В клиентском `clientsettings.json` задаётся адрес API.

Сборка из терминала PowerShell в VS Code:

```powershell
dotnet build .\BD_RGR\BD_RGR.slnx
```

Чтобы собрать решение и запустить оба проекта одной командой, выполните скрипт. После закрытия WPF он остановит запущенный им API.

```powershell
.\BD_RGR\Start-Autopark.ps1
```

Для раздельного запуска откройте API в первом терминале.

```powershell
dotnet run --project .\BD_RGR\BD_RGR.Api\BD_RGR.Api.csproj
```

Во втором терминале запустите WPF. Файл `BD_RGR/BD_RGR/clientsettings.json` задаёт `http://127.0.0.1:5080`. Он копируется вместе с клиентом.

```powershell
dotnet run --project .\BD_RGR\BD_RGR\BD_RGR.csproj
```

На экране входа откройте «Регистрация», введите логин, пароль и выберите роль. После регистрации экран входа подставит логин. Смена пароля находится в «Разное → Сменить пароль». После успешной смены нужно войти снова.

## Документы и аналитика

- В «Документы» выберите шаблон или «Ручной SELECT». Доступный шаблон зависит от роли. Ручной редактор принимает один `SELECT` с необязательными `DISTINCT`, `JOIN`, `WHERE`, `GROUP BY`, `ORDER BY` и `LIMIT`. Для псевдонимов используйте `AS`. Разрешены `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, `LOWER`, `UPPER`. Источники ограничены таблицами маршрутов, пунктов, рейсов, грузоперевозок, истории пробега, гаражей, марок, моделей и транспорта, которые разрешены роли. Для диспетчеров паспорт автомобиля в готовых шаблонах фильтруется RLS. Пример ручного запроса доступен всем пяти ролям.

  ```sql
  SELECT id, name FROM public.model ORDER BY id LIMIT 20
  ```

- Запрос выполняется в транзакции `READ ONLY`. Лимиты отчёта составляют 500 возвращаемых строк, 32 столбца и 4 секунды выполнения SQL. CSV выгружается из сохранённого снимка результата для той же сессии. Снимок хранится до 20 минут в памяти API. CSV содержит UTF‑8 BOM, локализованные заголовки и защиту текстовых ячеек от интерпретации как формул. [PostgreSQL READ ONLY](https://www.postgresql.org/docs/18/sql-set-transaction.html)
- «Аналитика» открывается отдельным окном. Границы периода включаются в выборку. В зависимости от роли доступны сумма пассажиров в день, число грузоперевозок и число показаний пробега. Окно опрашивает API каждые 5 секунд. Для трёх простых временных рядов использован встроенный WPF-рендеринг. Он даёт общий код отрисовки для экрана и PNG. Библиотеки наподобие LiveCharts полезны для интерактивных осей и сложных графиков. [WPF RenderTargetBitmap](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.rendertargetbitmap)
- «Разное → Настройки» сразу применяет тему, размер текста, язык, режим для дальтоников и плотность строк. Единый профиль `%LOCALAPPDATA%\BD_RGR\preferences.json` действует на экране входа и под любым аккаунтом приложения. При первом запуске новой версии из старого блока `Guest` переносятся язык, компактная плотность и крупный текст. Кнопка сброса возвращает светлую тему, обычный текст, русский язык и обычную плотность, а также выключает режим для дальтоников.

### Проверка этапа 6

Интеграционные сценарии выполнены на отдельной PostgreSQL 18.6, восстановленной из дампа. Для повторной проверки откройте готовый отчёт и ручной SELECT, выполните успешный запрос и запрос с ошибочным полем. Смените язык и выгрузите тот же результат. Проверьте заголовки и кириллицу в CSV. Откройте аналитику за период с рейсами, добавьте рейс из второй сессии и дождитесь изменения столбца без ручного обновления. Выгрузите PNG и сверьте его период с экраном.

В проверке WPF изменение количества пассажиров после фиксации записи из второй сессии появлялось примерно через 4,7–4,9 секунды. Снимки отчёта и аналитики в справке получены из WPF на учебных данных.

## Проверка PostgreSQL

Если в `mve` уже есть `app.users` и `app.roles`, а предметная схема `public` пуста, загрузите структуру от администратора PostgreSQL. Дамп содержит структуру, права и RLS. Таблицы `app` сохраняются. Для заполненной базы сначала сверьте её схему с дампом.

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -X -h 127.0.0.1 -p 5432 -U postgres -d mve --single-transaction -v ON_ERROR_STOP=1 -f .\info\database\mve_schema.sql
```

Запустите `info/database/03_check_app_auth.sql` под служебным логином. `psql` запросит пароль, если он требуется.

```powershell
psql -X -h 127.0.0.1 -p 5432 -d mve -U app_auth -v ON_ERROR_STOP=1 -f .\info\database\03_check_app_auth.sql
```

Скрипт показывает поля, индекс на `lower(username)`, пять ролей и действующие привилегии. Если нужных прав не хватает, владелец базы может применить только недостающие из следующих выдач. Для тестовой базы замените `mve` на её имя.

```sql
GRANT CONNECT ON DATABASE mve TO app_auth;
GRANT USAGE ON SCHEMA app TO app_auth;
GRANT SELECT (id, code) ON app.roles TO app_auth;
GRANT SELECT (id, username, password_hash, id_role),
      INSERT (username, password_hash, id_role),
      UPDATE (password_hash) ON app.users TO app_auth;
GRANT USAGE ON SEQUENCE app.users_id_seq TO app_auth;
```

Для отдельной тестовой базы сначала выполните `01_create_app_accounts.sql` и `02_seed_app_roles.sql` из `info/database` от владельца тестовой базы. Создайте отдельный служебный логин для тестов и выдайте ему указанные права.

## Учебная копия и ручная проверка

Запустите отдельный экземпляр PostgreSQL 18 на loopback. Следующие команды используют новый каталог и тестовый порт `55497`. При инициализации задайте тестовый пароль для `postgres`.

```powershell
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$testCluster = Join-Path $env:TEMP ('autopark-' + [guid]::NewGuid().ToString('N'))
& "$pgBin\initdb.exe" -D $testCluster -U postgres --auth=scram-sha-256 --pwprompt --encoding=UTF8
& "$pgBin\pg_ctl.exe" start -D $testCluster -l (Join-Path $testCluster 'server.log') -o '-h 127.0.0.1 -p 55497' -w
& "$pgBin\psql.exe" -X -h 127.0.0.1 -p 55497 -U postgres -d postgres
```

В открывшемся `psql` создайте базу `mve_stage3` и роли из дампа. После SQL задайте тестовые пароли для `app_auth` и пяти рабочих логинов командой `\password имя_логина`, затем выйдите через `\q`.

```sql
CREATE ROLE mrred;
CREATE ROLE director_role;
CREATE ROLE pass_dispatcher_role;
CREATE ROLE cargo_dispatcher_role;
CREATE ROLE garage_engineer_role;
CREATE ROLE read_only_role;
CREATE ROLE u_director LOGIN IN ROLE director_role;
CREATE ROLE u_pass_dispatcher LOGIN IN ROLE pass_dispatcher_role;
CREATE ROLE u_cargo_dispatcher LOGIN IN ROLE cargo_dispatcher_role;
CREATE ROLE u_engineer LOGIN IN ROLE garage_engineer_role;
CREATE ROLE u_reader1 LOGIN IN ROLE read_only_role;
CREATE ROLE app_auth LOGIN;
CREATE DATABASE mve_stage3;
```

Если тестовый сервер слушает `55497`, восстановите схемы из корня `_rgz`.

```powershell
& "$pgBin\psql.exe" -X -h 127.0.0.1 -p 55497 -U postgres -d mve_stage3 -v ON_ERROR_STOP=1 -f .\info\database\mve_schema.sql
& "$pgBin\psql.exe" -X -h 127.0.0.1 -p 55497 -U postgres -d mve_stage3 -v ON_ERROR_STOP=1 -f .\info\database\01_create_app_accounts.sql
& "$pgBin\psql.exe" -X -h 127.0.0.1 -p 55497 -U postgres -d mve_stage3 -v ON_ERROR_STOP=1 -f .\info\database\02_seed_app_roles.sql
```

Выполните выдачи прав `app_auth` из предыдущего раздела в `mve_stage3`. Укажите эту базу, порт и тестовые учётные данные в серверном JSON, затем запустите `Start-Autopark.ps1`.

1. Зарегистрируйте директора, читателя, обоих диспетчеров и инженера. Под директором создайте город, улицу, сотрудника и две бригады, одну без бригадира. Найдите сотрудника по ФИО, назначьте бригадиром, откройте карточку по ФИО в таблице и вернитесь. Проверьте, что строка, поиск и сортировка восстановились. Под читателем проверьте просмотр.
2. Под директором создайте марку, модель и гараж. Под каждым диспетчером создайте машину своего типа. Под пассажирским добавьте два пункта, маршрут, закрепление и рейс. Под грузовым создайте груз с типом, выбранным из справочника, и перевозку. Откройте позиции груза из карточки перевозки. Для создания типа груза используйте директора.
3. Под директором откройте карточку сотрудника и раздельно проверьте кадровую запись, членство в бригаде и закрепление за машиной. Если нужны кадровые справочники, заполните их в меню «Справочники». Добавьте ремонт и проверьте статус транспорта в его карточке. После удаления последнего активного ремонта статус должен стать «Исправен».
4. Под инженером в «История пробега» добавьте показания 100 и 185 км на две разные даты для одной машины. Выберите машину и эти даты в «Расчёт пробега за период». Результат должен быть 85 км. Повторите показание на одну из дат и проверьте сообщение о дубликате.

После проверки остановите учебный экземпляр командой `& "$pgBin\pg_ctl.exe" stop -D $testCluster -m fast -w`.

Для проверки исторических записей транспорта выполните под директором запрос. Он показывает автомобили без подтипа и автомобили с несколькими подтипами.

```sql
SELECT v.id, v.registration_number
FROM public.vehicle v
LEFT JOIN public.passenger_vehicle p ON p.vehicle_id = v.id
LEFT JOIN public.truck t ON t.vehicle_id = v.id
LEFT JOIN public.auxiliary_vehicle a ON a.vehicle_id = v.id
WHERE (p.id IS NOT NULL)::int + (t.id IS NOT NULL)::int + (a.id IS NOT NULL)::int <> 1;
```
