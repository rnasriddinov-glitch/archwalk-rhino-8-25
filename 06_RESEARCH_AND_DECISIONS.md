# Исследование и решения ARCHWALK

Проверка источников выполнена 17 сентября 2026 года. Этот документ отделяет подтверждённые возможности SDK от проектных решений и от поведения, которое ещё необходимо проверить в реальном Rhino 8.25.

## 1 Что уже умеет Rhino

В Rhino 8 существует WalkAbout: правая кнопка управляет азимутом и наклоном, стрелки перемещают камеру вперёд, назад и в стороны, Page Up/Down меняют шаг. Команда Camera позволяет показать положение камеры в других видах. Поэтому проект не исходит из ошибочного предположения, что в Rhino отсутствует перемещение от первого лица. [Официальная справка WalkAbout](https://docs.mcneel.com/rhino/8/help/en-us/commands/walkabout.htm).

Смысл отдельного плагина — объединить постановку наблюдателя, предварительную проверку кадра, привычный FPS-ввод, физические настройки и сохранение точек в один понятный рабочий процесс. Реализация выбранного поведения через постоянное выполнение макросов WalkAbout не даёт нужного контракта движения, поэтому выбран собственный контроллер поверх родной камеры.

## 2 Проверенные первичные источники

| Источник | Что подтверждено | Граница вывода |
| --- | --- | --- |
| [Runtime Rhino и .NET](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/) | Переход Rhino 8.20+ к .NET 8 по умолчанию | Runtime конкретной установки всё равно проверяется |
| [RhinoViewport](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_Display_RhinoViewport.htm) | Доступ к камере, проекции, target и экранным преобразованиям | Не подтверждает бесконфликтную сессию FPS само по себе |
| [ViewportInfo](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_DocObjects_ViewportInfo.htm) | Представление frustum и копирование параметров viewport | Не хранит все настройки UI и жизненный цикл окна |
| [SetViewProjection](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/M_Rhino_Display_RhinoViewport_SetViewProjection.htm) | Документированная установка проекции и параметр обработки target; Since 5.0 | Точное восстановление проверяется на целевой сборке |
| [CameraAngle](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/P_Rhino_Display_RhinoViewport_CameraAngle.htm) | Это половина меньшего угла обзора | Нельзя напрямую считать его полным вертикальным FOV |
| [MouseCallback](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_UI_MouseCallback.htm) | Обработка мыши в видах, возможность Cancel в начальных событиях | Не равнозначно полному keyboard capture |
| [KeyboardHookEvent](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_RhinoApp_KeyboardHookEvent.htm) | Делегат с параметром int key и возвратом void | Нет документированного Handled; не проектировать на выдуманном аргументе события |
| [GetPoint](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_Input_Custom_GetPoint.htm) | Интерактивный выбор точки; полный redraw во время выбора дорог | Не оправдывает бесконечную команду во время прогулки |
| [Display Conduits](https://developer.rhino3d.com/guides/rhinocommon/display-conduits/) | Дополнительная графика в display pipeline | Conduit не создаёт автоматически сохранённый объект наблюдателя |
| [DrawToBitmap](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/M_Rhino_Display_DisplayPipeline_DrawToBitmap.htm) | Off-screen bitmap заданного RhinoViewport; Since 5.0 | Контекст документа и clipping копии требуют P0C |
| [Panels](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_UI_Panels.htm) | Регистрация и доступ к панелям Rhino | Конкретные перегрузки проверяются против 8.25 |
| [PlugIn](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/T_Rhino_PlugIns_PlugIn.htm) | ReadDocument, WriteDocument, ShouldCallWriteDocument и настройки | Сохранение неизвестных данных без плагина требует round-trip |
| [AddCustomUndoEvent](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/M_Rhino_RhinoDoc_AddCustomUndoEvent.htm) | Наличие механизма custom undo; Since 5.0 | Реальную симметрию Undo/Redo доказывает прототип |
| [MeshRay](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/M_Rhino_Geometry_Intersect_Intersection_MeshRay.htm) | Первое пересечение луча с mesh | Это не готовый контроллер пола, ступеней или тела |
| [UnitScale](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/M_Rhino_RhinoMath_UnitScale.htm) | Коэффициент перевода систем единиц | Unitless-модель требует явного масштаба |
| [UnitsChangedWithScaling](https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/E_Rhino_RhinoDoc_UnitsChangedWithScaling.htm) | Уведомление до масштабирования объектов; Since 7.20 | Нельзя дважды масштабировать записи при последующем событии свойств |
| [WM_KEYDOWN](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-keydown) | Scan code, repeat state и связь с окном фокуса | Позиция обработки относительно Rhino accelerators устанавливается в P0A |
| [RegisterRawInputDevices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices) | Один получатель класса raw-input-устройств в процессе; предупреждение для библиотек | Регистрация внутри плагина не считается безвредной по умолчанию |
| [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos) | Программное положение указателя Windows | Пользовательский комфорт, DPI и suppression synthetic events требуют тестов |
| [Пакет Yak](https://developer.rhino3d.com/guides/yak/creating-a-rhino-plugin-package/) | Сборка пакета и указание платформы | Создание пакета не означает разрешение его публичной публикации |

Онлайн API в момент исследования показывает как страницы Rhino 8.37, так и Rhino 9. Пометки Since полезны для первичной оценки доступности, но не являются результатом компиляции на 8.25. Номера API-сборок, конкретные неподтверждённые перегрузки и результаты замеров здесь намеренно не выдуманы.

## 3 Реестр принятых решений

| ID | Решение | Причина |
| --- | --- | --- |
| D01 | Windows x64 первая платформа | Удержать надёжность native ввода; отдельная macOS-реализация — самостоятельная задача |
| D02 | Родной Rhino viewport | Пользователь видит текущую рабочую модель без экспорта в движок |
| D03 | Точка у ног плюс физическая высота глаз | Исключить смешение отметки этажа и человеческого роста |
| D04 | Два клика и живое превью | Направление и композиция проверяются до входа |
| D05 | Сохранённый наблюдатель отделён от временной позы | Прогулка не уничтожает удачную исходную точку |
| D06 | Три режима, без автоматического дальнего падения | Поддержать полную и неполную архитектурную модель |
| D07 | Стены проходимы в v1 | Пользователь не просил физический симулятор; незавершённые модели часто требуют свободного прохода |
| D08 | Мышь без фильтра по умолчанию, короткое торможение позиции | Быстрый отклик головы и управляемая остановка |
| D09 | Обычная перспектива при свободном pitch | Избежать скрытой коррекции вертикалей при взгляде вверх и вниз |
| D10 | Esc оставляет кадр, Backspace возвращает исходный | Однозначно разделить найденный результат и отмену прогулки |
| D11 | Временная графика вместо объектов модели | Не засорять геометрию, слои, экспорт и Zoom Extents |
| D12 | Отдельный WindowsInputBridge | Зафиксировать наиболее сложную host-зависимую часть и проверить её первой |
| D13 | Raw Input не регистрируется вслепую | Учитывать процесс Rhino как хозяина ввода |
| D14 | Настройки по умолчанию не переписывают старые точки | Сохранённые ракурсы остаются воспроизводимыми |

## 4 Остаточные риски и решения при неудаче

| Риск | Проверка | Предусмотренное действие |
| --- | --- | --- |
| Клавиши перехватывает Rhino раньше адаптера | P0A, A12, A27 | Исправить native точку обработки; при необходимости изолированный native bridge |
| Копия viewport неверно наследует документ или clipping | P0C, A05, A40 | Небольшой собственный родной preview view вместо off-screen-копии |
| Отображение большой модели тормозит | A53, A57 | Явная опция Shaded, ограничение частоты превью, оптимизация кэша; без обещания невозможного FPS |
| Сложный тип объекта не даёт достоверную опору | P0E, A39 | Явное исключение из опор, выбор набора поверхностей или режим по отметке |
| Сериализация/Undo ведут себя иначе в 8.25 | P0D, A44–A51 | Исправить транзакцию и адаптер данных до выпуска |
| Ошибочная размерность исходной модели | A14, unitless-тест | Показать масштаб; потребовать явное значение для неизвестных единиц, не масштабировать архитектуру |

Эти риски не делают концепцию неопределённой. Пользовательский контракт задан; проверяется конкретный способ интеграции с целевым хостом. Изменение пользовательского поведения возможно только как явное изменение проекта с объяснением последствий.

## 5 Возможное развитие после v1

Физические столкновения с капсулой и sliding вдоль стен; профили сидящего человека; управляемая коррекция вертикалей при подготовке кадра; маршруты и запись камеры; macOS; импорт и экспорт коллекций наблюдателей. Каждый пункт требует отдельного проекта и приёмки. В код первой версии не нужно заранее встраивать сложные подсистемы ради этих возможностей.
