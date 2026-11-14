# BankVerification

Учебный REST-сервис управления двумя банковскими счетами. HTTP-слой реализован на C# и ASP.NET Web API 2, бизнес-логика — на Spec# с формальной проверкой через SscBoogie и Z3.

## Требования

- Windows и Windows PowerShell 5.1 (`powershell.exe`).
- .NET Framework 4.8 или совместимая более новая версия .NET Framework 4.x; компилятор `csc.exe` в `%WINDIR%\Microsoft.NET\Framework\v4.0.30319`.
- Установленный классический **Spec# с SscBoogie 2.00 и совместимым Z3 из той же поставки**. В его каталоге `bin` нужны `ssc.exe`, `SscBoogie.exe`, `Z3.exe`, `Microsoft.SpecSharp.Runtime.dll` и `System.Compiler.Runtime.dll`. Современный отдельный Boogie не заменяет этот набор.
- Доступ к `https://api.nuget.org` при первом восстановлении зависимостей.

По умолчанию инструменты Spec# ищутся в `C:\Program Files (x86)\Microsoft\Spec#\bin`. Они устанавливаются отдельно и не входят в репозиторий. .NET SDK, IIS, Python, Word и LibreOffice для сборки и проверки сервиса не нужны.

## Полная проверка из чистой копии

После клонирования репозитория откройте PowerShell в его корневой папке и выполните:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Check.ps1
```

Скрипт автоматически:

1. Скачает закреплённые версии NuGet-пакетов в `packages`.
2. Соберёт библиотеку Spec# и HTTP-приложение в `build`.
3. Проверит корректное ядро и отдельно запустит намеренно ошибочный пример.
4. Выполнит прямые тесты библиотеки.
5. Запустит локальный сервер, выполнит HTTP-тесты и остановит созданный процесс.

При другом расположении Spec# или занятом порте:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Check.ps1 -SpecSharpBin "D:\Tools\SpecSharp\bin" -Port 8086
```

Если HTTP.sys запрещает регистрацию адреса, выполните команду в PowerShell с правами администратора. Скрипты не меняют URL ACL и системную политику выполнения; `-ExecutionPolicy Bypass` действует только для запускаемого процесса. Перед проверкой остановите вручную запущенный экземпляр сервиса на том же порте.

Ожидаемые итоги:

| Этап | Результат |
| --- | --- |
| Сборка | `BUILD PASSED` |
| Рабочее ядро | `10 verified, 0 errors` |
| Намеренная ошибка | `1 verified, 1 error`, нарушено постусловие |
| Прямые тесты | `12 checks passed, 0 failed`, включая 10 000 шагов модели |
| HTTP-тесты | `61 checks passed, 0 failed`, включая 100 параллельных пополнений и 100 переводов |
| Весь прогон | `ALL CHECKS PASSED` |

Ошибка в `WithdrawBroken` ожидаема: это отдельная демонстрация обнаружения дефекта, она не входит в сервис. `RequiresException` рядом с `PASS` и HTTP 400/404/405/409 в негативных тестах также ожидаемы. Предупреждение CS1701 о Newtonsoft.Json связано с версиями сборок; нужный binding redirect задан в `src/BankApi.exe.config`.

Протоколы создаются в `artifacts`: `compile-specsharp.log`, `compile-api.log`, `verify-correct.log`, `verify-broken.log`, `test-core.log`, `test-http.log`, `server.log`, `server-error.log`. Контрольные суммы загруженных пакетов сохраняются в `packages/sha256.json`.

## Запуск сервиса

Для сборки без полного прогона и последующего запуска:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
.\build\BankApi.exe
```

Адрес по умолчанию — `http://localhost:8085`. Другой адрес передаётся аргументом: `.\build\BankApi.exe http://localhost:8086`. Поддерживаются локальные HTTP-адреса. Остановка — **Ctrl+C**.

Начальные балансы: счёт 1 — `1000`, счёт 2 — `500`. Деньги задаются целым числом минимальных денежных единиц. Лимит одного счёта — `1 000 000`. Состояние хранится в памяти и сбрасывается при перезапуске.

Примеры запросов в другом окне PowerShell:

```powershell
Invoke-RestMethod 'http://localhost:8085/api/account/balance?accountId=1'

Invoke-RestMethod 'http://localhost:8085/api/account/deposit' -Method Post -ContentType 'application/json' -Body '{"accountId":1,"amount":200}'

Invoke-RestMethod 'http://localhost:8085/api/account/withdraw' -Method Post -ContentType 'application/json' -Body '{"accountId":1,"amount":100}'

Invoke-RestMethod 'http://localhost:8085/api/account/transfer' -Method Post -ContentType 'application/json' -Body '{"fromAccountId":1,"toAccountId":2,"amount":300}'
```

## Отдельные этапы и Visual Studio

После сборки можно запускать этапы независимо:

```powershell
# Формальная проверка, включая негативный пример
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Verify.ps1

# Прямые тесты ядра
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test.ps1

# HTTP-тесты против уже работающего сервера
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test.ps1 -BaseUrl http://localhost:8085
```

`Build.ps1` и `Verify.ps1` принимают `-SpecSharpBin`. HTTP-тесты изменяют балансы и восстанавливают начальные значения при успешном завершении; запускайте их на учебном экземпляре сервиса.

Для Visual Studio откройте `BankVerification.sln`. Проект восстанавливает зависимости и собирает библиотеку Spec# перед сборкой C#. Путь к инструментам можно задать свойством MSBuild `SpecSharpBin`. Visual Studio собирает приложение; полный набор проверок запускается через `scripts/Check.ps1`.

## Состав репозитория

| Файл или каталог | Назначение |
| --- | --- |
| `BankAccountService.ssc` | Ядро Spec#, контракты и инварианты |
| `src/` | Web API, поток владельца, хост и конфигурация |
| `tests/` | Прямые и HTTP-тесты |
| `verification/` | Намеренно ошибочный пример |
| `scripts/` | Восстановление, сборка и проверки |
| `BankVerification.sln`, `BankVerification.csproj` | Решение Visual Studio |
| [Отчёт](Отчет_SpecSharp.docx) | Формальная спецификация, результаты верификации каждого метода и описание скриптов |

`build`, `obj`, `packages` и `artifacts` генерируются заново и исключены через `.gitignore`. Каталог `.local`, если присутствует, содержит локальный архив исходного образца и резервных материалов; он также не загружается в репозиторий. Для публикации достаточно исходников, скриптов, документов и `.gitignore`; готовые DLL, EXE, PDB и скачанные пакеты добавлять не нужно.

Формальная проверка охватывает контракты ядра: точность денежных операций, сохранение суммы при переводе, рамки изменения и инварианты. Переполнение обосновано явными диапазонами и проверками выполнения. Параллелизм HTTP-адаптера проверен тестами, формально не доказан.
