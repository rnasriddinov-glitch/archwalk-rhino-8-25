# ARCHWALK — установка и удаление

Целевой стенд: **Windows x64**, Rhino **8.25.25328.11001**, .NET 8. Пакет локальный; в общий каталог Package Manager ничего не публикуется этим репозиторием.

GUID плагина (стабильный): `6f2e1c8a-3b47-4d9e-9a1c-7e4b2f90c8d1`.

## Сборка артефактов

```powershell
dotnet build src\ArchWalk.Rhino\ArchWalk.Rhino.csproj -c Debug
.\packaging\Build-Yak.ps1
```

- Плагин: `src\ArchWalk.Rhino\bin\plugin\net8.0-windows\ArchWalk.rhp` (+ `ArchWalk.Core.dll`, `ArchWalk.WindowsInput.dll`)
- Yak: `packaging\dist\archwalk-1.0.0-*.yak`

## Установка через Package Manager (рекомендуется)

1. Закройте Rhino, если открыт другой экземпляр с уже загруженным ARCHWALK.
2. Запустите Rhino 8.25.
3. Команда `_PackageManager` → вкладка **Installed** / поиск локального пакета, либо:

```text
_PackageManager
```

и установка из файла `.yak` (Browse / Install from file — в зависимости от UI сборки).

4. Перезапустите Rhino после первой установки .NET-плагина.
5. Проверка: `_AWPanel` открывает «Наблюдатель»; `_AWPlace` начинает установку.

### Чистый пользовательский профиль (A59)

1. Создайте новый Windows-профиль или временный Rhino scheme/profile без сторонних плагинов.
2. Установите только `archwalk-1.0.0-*-win.yak`.
3. Убедитесь, что загружается сборка **1.0.0** и GUID совпадает.
4. Пройдите `_AWPlace` → Enter → Esc: курсор свободен, нет захвата мыши.
5. Отключите/удалите пакет (ниже) и перезапустите Rhino: команд `_AW*` нет, панель «Наблюдатель» не зарегистрирована, глобальные настройки Rhino не изменены.

## Ручная установка `.rhp`

1. Скопируйте в одну папку: `ArchWalk.rhp`, `ArchWalk.Core.dll`, `ArchWalk.WindowsInput.dll`.
2. В Rhino: `_PlugInManager` → Install → выберите `ArchWalk.rhp`.
3. Перезапустите Rhino.

Не разносите DLL по разным каталогам: загрузчик ищет соседние сборки рядом с `.rhp`.

## Удаление / отключение

1. Завершите прогулку (**Esc** или `_AWResetInput`), закройте панель.
2. `_PackageManager` → Uninstall **archwalk**, либо `_PlugInManager` → снимите галочку / Remove.
3. Перезапустите Rhino.

Ожидаемо после штатного удаления:

- нет алиасов ARCHWALK и команд `_AW*`;
- нет активного cursor capture / WH_GETMESSAGE от плагина;
- нет «панелей-призраков» с сессией;
- модель не повреждена (наблюдатели остаются в 3dm как plugin data, пока файл не пересохранён без плагина — см. A51).

**Ограничение хоста:** горячая выгрузка .NET-сборки Rhino 8.25 без перезапуска не гарантируется. После Update/Uninstall нужен рестарт Rhino.

## Аварийное освобождение ввода

Если мышь «залипла» внутри прогулки: `_AWResetInput` или Alt+Tab / Esc. Плагин освобождает принадлежащий ему захват без перезапуска Rhino в штатных путях (A29).
