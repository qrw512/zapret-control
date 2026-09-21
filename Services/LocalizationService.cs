using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZapretGui.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private string _currentLanguage = "RU";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(string.Empty);
                OnPropertyChanged("Item[]");
            }
        }
    }

    private readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        ["RU"] = new()
        {
            // ==== Общие ====
            ["BypassModes"]      = "Режимы обхода",
            ["Settings"]         = "Настройки",
            ["SettingsTitle"]    = "Настройки",
            ["PathToZapret"]     = "Путь к папке Zapret",
            ["Browse"]           = "Обзор...",
            ["Theme"]            = "Тема оформления",
            ["ThemeSystem"]      = "Системная",
            ["ThemeLight"]       = "Светлая",
            ["ThemeDark"]        = "Темная",
            ["Language"]         = "Язык / Language",
            ["Done"]             = "Готово",
            ["Update"]           = "Обновить",
            ["StatusStopped"]    = "Остановлен",
            ["StatusRunning"]    = "Работает",
            ["StatusError"]      = "Ошибка запуска",
            ["Selected"]         = "Выбран",
            ["CheckUpdates"]     = "Проверка обновлений...",
            ["LatestVersion"]    = "Версия {0} (Актуальная)",
            ["UpdateAvailable"]  = "Доступно обновление {0}!",
            ["UpdateSuccess"]    = "Успешно обновлено до {0}!",
            ["UpdateCheckFailed"] = "Не удалось проверить обновления",
            ["MainSubtitle"]     = "Управление службой",
            ["BtnTools"]         = "Инструменты",

            // ==== Tray ====
            ["TrayOpen"]  = "Открыть",
            ["TrayToggleStart"]   = "Включить обход",
            ["TrayToggleStop"]    = "Выключить обход",
            ["TrayPresetsHeader"] = "Режим обхода",
            ["TrayExit"]  = "Выход",

            // ==== ServiceView ====
            ["ServiceViewTitle"]    = "Центр обслуживания Zapret",
            ["ServiceViewSubtitle"] = "Управление компонентами, службой и диагностикой без консоли",
            ["BtnRun"]              = "Запустить",
            ["BtnShowOutput"]       = "Показать / скрыть вывод",
            ["StatusReady"]         = "Готов к запуску",
            ["StatusInProgress"]    = "Выполняется...",
            ["StatusDone"]          = "Успешно выполнено",
            ["StatusFailed"]        = "Завершено с ошибкой",
            ["SelectedFormat"] = "Выбран: {0}",

            // ==== Tabs ====
            ["TabIpSettings"]   = "Настройки IP",
            ["TabSiteLists"]    = "Список сайтов",
            ["TabDiagnostics"]  = "Диагностика",

            // ==== IP tab ====
            ["IpUpdateHint"]      = "Обновление глобальных списков IP-адресов и файла hosts:",
            ["IpExcludeHint"]     = "Исключения IP-адресов (по одному в строке, поддерживаются CIDR и подсети):",
            ["IpExcludeWatermark"]= "192.168.0.0/16\n10.0.0.0/8",
            ["BtnUpdateIpset"]    = "Обновить список IP",
            ["BtnUpdateHosts"]    = "Обновить hosts",
            ["BtnSaveExcludeIps"] = "Сохранить исключения",
            ["MsgIpsetUpdateOk"]  = "[OK] Список IP обновлён ({0} байт)",
            ["MsgHostsUpdateOk"]  = "[OK] Файл hosts обновлён",
            ["MsgExcludeIpsSaved"]= "[OK] Исключения IP сохранены ({0} записей)",

            // ==== Sites tab ====
            ["SiteAddHint"]        = "Обход (whitelist) — домены, которые принудительно обрабатываются Zapret:",
            ["SiteExcludeHint"]    = "Исключения (blacklist) — домены, которые НЕ обрабатываются Zapret:",
            ["BtnAddSite"]         = "Добавить в обход",
            ["BtnAddExcludeSite"]  = "Добавить в исключения",
            ["CurrentSites"]       = "Текущий whitelist",
            ["CurrentExcludes"]    = "Текущий blacklist",
            ["MsgSiteAdded"]       = "[OK] Сайт добавлен: {0}",
            ["MsgSiteExists"]      = "[!] Сайт уже в списке: {0}",
            ["MsgSiteAddedExclude"]= "[OK] Добавлено в исключения: {0}",

            // ==== Traffic ====
            ["TrafficTitle"]         = "Мониторинг трафика (Beta)",
            ["TrafficSubtitle"]      = "Активность сетевых процессов в реальном времени",
            ["TrafficUpload"]        = "Отправка",
            ["TrafficDownload"]      = "Загрузка",
            ["TrafficProcess"]       = "Процесс",
            ["TrafficZapretTag"]     = "ZAPRET",
            ["BtnStartMonitor"]      = "Запустить",
            ["BtnStopMonitor"]       = "Остановить",
            ["TrafficNeedsAdmin"]    = "Для мониторинга трафика требуются права администратора",
            ["TrafficNoData"]        = "Нет активных сетевых соединений",
            ["TrafficTotal"]         = "Итого",

            // ==== Tasks ====
            ["TaskDiagnosticsTitle"] = "Диагностика проблем",
            ["TaskDiagnosticsDesc"]  = "Проверка целостности компонентов, драйверов и окружения",
            ["TaskStatusTitle"]      = "Статус службы и winws",
            ["TaskStatusDesc"]       = "Проверка состояния работы службы Zapret и фонового процесса",
            ["TaskIpsetTitle"]       = "Обновить IPSet списки",
            ["TaskIpsetDesc"]        = "Загрузка актуальных списков заблокированных ресурсов и IP",
            ["TaskHostsTitle"]       = "Проверить файл Hosts",
            ["TaskHostsDesc"]        = "Автоматическая диагностика и запись необходимых записей в Hosts",
            ["TaskFakesTitle"]       = "Заменить активные Fakes",
            ["TaskFakesDesc"]        = "Обновление и ротация поддельных TLS/HTTP пакетов",
            ["TaskTestsTitle"]       = "Встроенное тестирование",
            ["TaskTestsDesc"]        = "Запуск тестов работоспособности через PowerShell",

            // ==== Preset menu ====
            ["PresetMenuTooltip"]  = "Тесты и действия",
            ["MenuTestCurrent"]   = "Проверить текущий режим",
            ["MenuTestAll"]       = "Проверить все режимы",
            ["BtnTestStandard"]   = "Standard tests",
            ["BtnTestDpi"]        = "DPI checkers",
            ["TestingAllModes"]   = "Проверка всех режимов",
            ["MsgTestAllNeedAdmin"] = "Для проверки всех режимов требуются права администратора.",
            ["MsgTestNoResult"]   = "Нет сохранённого результата. Запустите тест.",
            ["StatusAllModesHeader"] = "=== {0} ===",
            ["MenuStandardTest"]   = "Стандартные тесты",
            ["MenuDpiTest"]        = "DPI checkers",
            ["MenuShowLastResult"] = "Показать последний результат",
            ["BtnClose"]           = "Закрыть",
            ["TestModalTitleFmt"]  = "Результат теста: {0}",
            ["MsgTestsNeedAdmin"] = "Тестирование возможно только при запуске Zapret Control от имени администратора.",
            ["TestStandard"]         = "Стандартные тесты",
            ["TestDpi"]              = "DPI checkers",

            // ==== Settings ====
            ["AutoStart"]            = "Автозагрузка Zapret",
            ["AutoStartDesc"]        = "Установить службу с последним выбранным пресетом. Работает при старте Windows.",
            ["GameFilter"]           = "Игровой фильтр (Game-Filter)",
            ["GameFilterDesc"]       = "Дополнительные правила обхода для Discord / Steam / игр",
            ["GameFilterOff"]        = "Отключен",
            ["GameFilterAll"]        = "TCP и UDP",
            ["GameFilterTcp"]        = "Только TCP",
            ["GameFilterUdp"]        = "Только UDP",
            ["SettingsSubtitle"]      = "Настройки приложения и обхода",
            ["AutoStartCardTitle"]    = "Автозапуск",
            ["AutoStartCardDesc"]     = "Управление запуском приложения и обхода при старте системы",
            ["ThemeDesc"]             = "Цветовая схема приложения",
            ["LanguageDesc"]          = "Язык интерфейса приложения",

            // ==== ServiceManager messages ====
            ["MsgNeedAdmin"]         = "Требуются права администратора (запустите Zapret Control от имени администратора)",
            ["MsgNoPreset"]          = "Не выбран preset",
            ["MsgPresetNotFound"]    = "Preset не найден: {0}",
            ["MsgArgsExtractFail"]   = "Не удалось извлечь аргументы winws.exe из {0}.\nПроверьте, что файл содержит строку с winws.exe.",
            ["MsgWinwsMissing"]      = "winws.exe не найден: {0}",
            ["MsgInstallOK"]         = "[OK] Служба установлена и запущена с пресетом '{0}'.",
            ["MsgInstallCreatedNoRun"] = "[!] Служба создана, но НЕ запустилась.",
            ["MsgScCreateFailed"]    = "sc create завершился с ошибкой.",
            ["MsgScCreateOkNoService"] = "[X] sc create вернул 0, но служба не зарегистрирована",
            ["MsgServiceRemoved"]    = "[OK] Служба zapret удалена.",
            ["MsgServiceNotInstalled"] = "Служба zapret не была установлена.",
            ["MsgCleanupDone"]       = "[OK] Очистка завершена.",
            ["MsgIpsetUpdated"]      = "[OK] IPSet обновлён ({0} байт)",
            ["MsgIpsetFailed"]       = "Не удалось обновить IPSet: {0}",
            ["MsgHostsNotFound"]     = "Файл hosts не найден",
            ["MsgHostsRemoteEmpty"]  = "Полученный файл hosts пуст",
            ["MsgHostsNeedsUpdate"]  = "[?] Файл Hosts требует обновления",
            ["MsgHostsUpToDate"]     = "[OK] Файл Hosts актуален",
            ["MsgHostsCheckFailed"]  = "Проверка Hosts не удалась: {0}",
            ["MsgUtilsCreateFail"]   = "Не удалось создать папку utils: {0}",
            ["MsgGameFilterHint"]    = "Изменения применятся после перезапуска Zapret (переключите тумблер на главной странице).",
            ["MsgGameFilterOff"]     = "[OK] Игровой фильтр отключен",
            ["MsgGameFilterAll"]     = "[OK] Игровой фильтр включён (TCP и UDP)",
            ["MsgGameFilterTcp"]     = "[OK] Игровой фильтр включён (только TCP)",
            ["MsgGameFilterUdp"]     = "[OK] Игровой фильтр включён (только UDP)",
            ["MsgNoAdmin"]           = "[!] Приложение запущено без прав администратора",
            ["MsgServiceRunning"]    = "Служба \"{0}\" РАБОТАЕТ.",
            ["MsgServiceNotRunning"] = "Служба \"{0}\" НЕ работает.",
            ["MsgServiceNotInstalledFmt"] = "Служба \"{0}\" НЕ установлена.",
            ["MsgBypassRunning"]     = "Обход (winws.exe) РАБОТАЕТ.",
            ["MsgBypassNotRunning"]  = "Обход (winws.exe) НЕ работает.",
            ["MsgPresetNotSelected"] = "[X] Preset не выбран — выберите режим обхода слева",
            ["MsgServiceInstalledOk"] = "[OK] Служба установлена: {0}",
            ["MsgServiceRemovedOk"]  = "[OK] Служба удалена",
            ["MsgError"]             = "[X] {0}",
            ["TrafficNotAdmin"] = "Для мониторинга трафика нужны права администратора. Перезапустите Zapret Control от имени администратора.",
            ["TrafficEtwError"] = "Не удалось запустить ETW-сессию",
            ["TrafficSessionLimit"] = "Достигнут лимит ETW-сессий Windows. Закройте другие программы мониторинга (PerfView, Visual Studio Diagnostic Tools, Process Monitor) и перезапустите мониторинг.",

            // ==== Fakes ====
            ["FakesModalTitle"]  = "Замена активных Fakes",
            ["FakesTypeLabel"]   = "Тип подмены",
            ["FakesFileLabel"]   = "Файл подмены",
            ["FakesTypeDiscord"] = "Discord UDP",
            ["FakesTypeGame"]    = "GameFilter UDP",
            ["BtnApply"]         = "Применить",
            ["BtnCancel"]        = "Отмена",

            // ==== Автозапуск ====
            ["StartWithWindows"]         = "Запускать с Windows",
            ["StartWithWindowsDesc"]     = "Zapret Control будет стартовать при входе в систему и сворачиваться в трей.",
            ["AutoStartBypass"]          = "Автозапуск обхода",
            ["AutoStartBypassDesc"]      = "Автоматически включать обход с последним выбранным режимом при старте приложения.",
            ["StatusSwitching"] = "Переключение режима: {0}…",

            // ==== Self update ====
            ["SelfUpdateTitle"]         = "Обновление программы",
            ["SelfUpdateAvailableFmt"]  = "Доступна новая версия {0} (у вас {1}). Обновить сейчас?",
            ["SelfUpdateDownloading"]   = "Загрузка обновления…",
            ["SelfUpdateFailed"]        = "Не удалось выполнить обновление. Скачайте новую версию вручную с GitHub - https://github.com/qrw512/zapret-control.",
            ["SelfUpdateYes"]           = "Обновить",
            ["SelfUpdateNo"]            = "Позже",

            // ==== About ====
            ["About"]              = "О программе",
            ["AppVersionLabel"]    = "Версия приложения",
            ["GitHubProjectLabel"] = "Проект на GitHub",
            ["OpenInBrowser"]      = "Открыть в браузере",
            ["CheckForUpdates"]    = "Проверить обновления",
            ["UpToDateMsg"]        = "У вас установлена последняя версия",

            // ==== Bypass mode ====
            ["BypassModeWhitelist"]    = "Только из списка",
            ["BypassModeAll"]          = "Все сайты",
            ["BypassModeHintWhitelist"] = "Обход только для доменов из whitelist",
            ["BypassModeHintAll"]       = "Обход всего трафика без фильтрации",

            // Кнопки тестов (переехали в нижнюю панель)
            ["BtnTestStandard"]        = "Тест обхода",
            ["BtnTestDpi"]             = "Тест DPI",
            ["BtnTestStandardTip"]     = "Быстрая проверка: работает ли обход с текущим пресетом",
            ["BtnTestDpiTip"]          = "Глубокий тест: проверка обхода популярных DPI-ресурсов",
        },
        ["EN"] = new()
        {
            // ==== Common ====
            ["BypassModes"]      = "Bypass Modes",
            ["Settings"]         = "Settings",
            ["SettingsTitle"]    = "Settings",
            ["PathToZapret"]     = "Zapret Folder Path",
            ["Browse"]           = "Browse...",
            ["Theme"]            = "App Theme",
            ["ThemeSystem"]      = "System",
            ["ThemeLight"]       = "Light",
            ["ThemeDark"]        = "Dark",
            ["Language"]         = "Language",
            ["Done"]             = "Done",
            ["Update"]           = "Update",
            ["StatusStopped"]    = "Stopped",
            ["StatusRunning"]    = "Running",
            ["StatusError"]      = "Launch Error",
            ["Selected"]         = "Selected",
            ["CheckUpdates"]     = "Checking for updates...",
            ["LatestVersion"]    = "Version {0} (Latest)",
            ["UpdateAvailable"]  = "Update {0} available!",
            ["UpdateSuccess"]    = "Successfully updated to {0}!",
            ["UpdateCheckFailed"] = "Failed to check for updates",
            ["MainSubtitle"]     = "Service management",
            ["BtnTools"]         = "Tools",

            // ==== Tray ====
            ["TrayOpen"]  = "Open",
            ["TrayToggleStart"]   = "Enable bypass",
            ["TrayToggleStop"]    = "Disable bypass",
            ["TrayPresetsHeader"] = "Bypass mode",
            ["TrayExit"]  = "Exit",

            // ==== ServiceView ====
            ["ServiceViewTitle"]    = "Zapret Service Center",
            ["ServiceViewSubtitle"] = "Manage components, service and diagnostics without console",
            ["BtnRun"]              = "Run",
            ["BtnShowOutput"]       = "Show / hide output",
            ["StatusReady"]         = "Ready to run",
            ["StatusInProgress"]    = "Running...",
            ["StatusDone"]          = "Completed successfully",
            ["StatusFailed"]        = "Completed with error",
            ["SelectedFormat"] = "Selected: {0}",

            // ==== Tabs ====
            ["TabIpSettings"]   = "IP Settings",
            ["TabSiteLists"]    = "Site Lists",
            ["TabDiagnostics"]  = "Diagnostics",

            // ==== IP tab ====
            ["IpUpdateHint"]      = "Update global IP lists and hosts file:",
            ["IpExcludeHint"]     = "IP exclusions (one per line, CIDR supported):",
            ["IpExcludeWatermark"]= "192.168.0.0/16\n10.0.0.0/8",
            ["BtnUpdateIpset"]    = "Update IP list",
            ["BtnUpdateHosts"]    = "Update hosts",
            ["BtnSaveExcludeIps"] = "Save exclusions",
            ["MsgIpsetUpdateOk"]  = "[OK] IP list updated ({0} bytes)",
            ["MsgHostsUpdateOk"]  = "[OK] hosts file updated",
            ["MsgExcludeIpsSaved"]= "[OK] IP exclusions saved ({0} entries)",

            // ==== Sites tab ====
            ["SiteAddHint"]        = "Whitelist — domains forced through Zapret:",
            ["SiteExcludeHint"]    = "Blacklist — domains bypassing Zapret:",
            ["BtnAddSite"]         = "Add to whitelist",
            ["BtnAddExcludeSite"]  = "Add to exceptions",
            ["CurrentSites"]       = "Current whitelist",
            ["CurrentExcludes"]    = "Current blacklist",
            ["MsgSiteAdded"]       = "[OK] Site added: {0}",
            ["MsgSiteExists"]      = "[!] Site already in list: {0}",
            ["MsgSiteAddedExclude"]= "[OK] Added to exceptions: {0}",

            // ==== Traffic ====
            ["TrafficTitle"]         = "Traffic Monitor (Beta)",
            ["TrafficSubtitle"]      = "Real-time network activity by process",
            ["TrafficUpload"]        = "Upload",
            ["TrafficDownload"]      = "Download",
            ["TrafficProcess"]       = "Process",
            ["TrafficZapretTag"]     = "ZAPRET",
            ["BtnStartMonitor"]      = "Start",
            ["BtnStopMonitor"]       = "Stop",
            ["TrafficNeedsAdmin"]    = "Administrator rights required to monitor traffic",
            ["TrafficNoData"]        = "No active network connections",
            ["TrafficTotal"]         = "Total",

            // ==== Tasks ====
            ["TaskDiagnosticsTitle"] = "Diagnostics",
            ["TaskDiagnosticsDesc"]  = "Check components, drivers and environment integrity",
            ["TaskStatusTitle"]      = "Service and winws status",
            ["TaskStatusDesc"]       = "Check Zapret service and background process state",
            ["TaskIpsetTitle"]       = "Update IPSet lists",
            ["TaskIpsetDesc"]        = "Download latest blocked resources and IP lists",
            ["TaskHostsTitle"]       = "Check Hosts file",
            ["TaskHostsDesc"]        = "Automatic diagnostics and update of Hosts entries",
            ["TaskFakesTitle"]       = "Replace active Fakes",
            ["TaskFakesDesc"]        = "Rotate and update fake TLS/HTTP packets",
            ["TaskTestsTitle"]       = "Built-in tests",
            ["TaskTestsDesc"]        = "Run performance tests through PowerShell",

            // ==== Preset menu ====
            ["PresetMenuTooltip"]  = "Tests and actions",
            ["MenuTestCurrent"]   = "Test current mode",
            ["MenuTestAll"]       = "Test all modes",
            ["BtnTestStandard"]   = "Standard tests",
            ["BtnTestDpi"]        = "DPI checkers",
            ["TestingAllModes"]   = "Testing all modes",
            ["MsgTestAllNeedAdmin"] = "Administrator rights required to test all modes.",
            ["MsgTestNoResult"]   = "No saved result. Run a test first.",
            ["StatusAllModesHeader"] = "=== {0} ===",
            ["MenuStandardTest"]   = "Standard tests",
            ["MenuDpiTest"]        = "DPI checkers",
            ["MenuShowLastResult"] = "Show last result",
            ["BtnClose"]           = "Close",
            ["TestModalTitleFmt"]  = "Test result: {0}",
            ["MsgTestsNeedAdmin"] = "Tests can be run only when Zapret Control is launched as Administrator.",
            ["TestStandard"]         = "Standard tests",
            ["TestDpi"]              = "DPI checkers",

            // ==== Settings ====
            ["AutoStart"]            = "Zapret AutoStart",
            ["AutoStartDesc"]        = "Install service with the last selected preset. Runs on Windows startup.",
            ["GameFilter"]           = "Game Filter",
            ["GameFilterDesc"]       = "Extra bypass rules for Discord / Steam / Games",
            ["GameFilterOff"]        = "Disabled",
            ["GameFilterAll"]        = "TCP and UDP",
            ["GameFilterTcp"]        = "TCP only",
            ["GameFilterUdp"]        = "UDP only",
            ["SettingsSubtitle"]      = "Application and bypass settings",
            ["AutoStartCardTitle"]    = "Auto-start",
            ["AutoStartCardDesc"]     = "Manage app launch and bypass on system startup",
            ["ThemeDesc"]             = "Application color scheme",
            ["LanguageDesc"]          = "Application interface language",

            // ==== ServiceManager messages ====
            ["MsgNeedAdmin"]         = "Administrator rights required (run Zapret Control as Administrator)",
            ["MsgNoPreset"]          = "No preset selected",
            ["MsgPresetNotFound"]    = "Preset not found: {0}",
            ["MsgArgsExtractFail"]   = "Could not extract winws.exe arguments from {0}.\nMake sure the file contains a line with winws.exe.",
            ["MsgWinwsMissing"]      = "winws.exe not found: {0}",
            ["MsgInstallOK"]         = "[OK] Service installed and started with preset '{0}'.",
            ["MsgInstallCreatedNoRun"] = "[!] Service created but FAILED to start.",
            ["MsgScCreateFailed"]    = "sc create failed.",
            ["MsgScCreateOkNoService"] = "[X] sc create returned 0 but service is not registered",
            ["MsgServiceRemoved"]    = "[OK] Service zapret removed.",
            ["MsgServiceNotInstalled"] = "Service zapret was not installed.",
            ["MsgCleanupDone"]       = "[OK] Cleanup done.",
            ["MsgIpsetUpdated"]      = "[OK] IPSet updated ({0} bytes)",
            ["MsgIpsetFailed"]       = "Failed to update IPSet: {0}",
            ["MsgHostsNotFound"]     = "hosts file not found",
            ["MsgHostsRemoteEmpty"]  = "Remote hosts file is empty",
            ["MsgHostsNeedsUpdate"]  = "[?] Hosts file needs update",
            ["MsgHostsUpToDate"]     = "[OK] Hosts file is up to date",
            ["MsgHostsCheckFailed"]  = "Hosts check failed: {0}",
            ["MsgUtilsCreateFail"]   = "Could not create utils folder: {0}",
            ["MsgGameFilterHint"]    = "Changes will be applied after Zapret restart (toggle the switch on the main page).",
            ["MsgGameFilterOff"]     = "[OK] Game filter disabled",
            ["MsgGameFilterAll"]     = "[OK] Game filter enabled (TCP and UDP)",
            ["MsgGameFilterTcp"]     = "[OK] Game filter enabled (TCP only)",
            ["MsgGameFilterUdp"]     = "[OK] Game filter enabled (UDP only)",
            ["MsgNoAdmin"]           = "[!] Application is running without administrator rights",
            ["MsgServiceRunning"]    = "Service \"{0}\" is RUNNING.",
            ["MsgServiceNotRunning"] = "Service \"{0}\" is NOT running.",
            ["MsgServiceNotInstalledFmt"] = "Service \"{0}\" is NOT installed.",
            ["MsgBypassRunning"]     = "Bypass (winws.exe) is RUNNING.",
            ["MsgBypassNotRunning"]  = "Bypass (winws.exe) is NOT running.",
            ["MsgPresetNotSelected"] = "[X] No preset selected — pick a bypass mode on the left",
            ["MsgServiceInstalledOk"] = "[OK] Service installed: {0}",
            ["MsgServiceRemovedOk"]  = "[OK] Service removed",
            ["MsgError"]             = "[X] {0}",
            ["TrafficNotAdmin"] = "Administrator rights are required for traffic monitoring. Restart Zapret Control as Administrator.",
            ["TrafficEtwError"] = "Failed to start ETW session",
            ["TrafficSessionLimit"] = "Windows ETW session limit reached. Close other monitoring tools (PerfView, Visual Studio Diagnostic Tools, Process Monitor) and restart monitoring.",

            // ==== Fakes ====
            ["FakesModalTitle"]  = "Replace active Fakes",
            ["FakesTypeLabel"]   = "Fake type",
            ["FakesFileLabel"]   = "Fake file",
            ["FakesTypeDiscord"] = "Discord UDP",
            ["FakesTypeGame"]    = "GameFilter UDP",
            ["BtnApply"]         = "Apply",
            ["BtnCancel"]        = "Cancel",

            // ==== Auto-start ====
            ["StartWithWindows"]         = "Start with Windows",
            ["StartWithWindowsDesc"]     = "Zapret Control will launch on system startup and minimize to tray.",
            ["AutoStartBypass"]          = "Auto-start bypass",
            ["AutoStartBypassDesc"]      = "Automaticlly enable bypass with the last selected mode when the app starts.",
            ["StatusSwitching"] = "Switching mode: {0}…",

            // ==== Self update ====
            ["SelfUpdateTitle"]         = "Application update",
            ["SelfUpdateAvailableFmt"]  = "Version {0} is available (you have {1}). Update now?",
            ["SelfUpdateDownloading"]   = "Downloading update…",
            ["SelfUpdateFailed"]        = "Update failed. Please download the new version manually from GitHub - https://github.com/qrw512/zapret-control.",
            ["SelfUpdateYes"]           = "Update",
            ["SelfUpdateNo"]            = "Later",

            // ==== About ====
            ["About"]              = "About",
            ["AppVersionLabel"]    = "App version",
            ["GitHubProjectLabel"] = "GitHub project",
            ["OpenInBrowser"]      = "Open in browser",
            ["CheckForUpdates"]    = "Check for updates",
            ["UpToDateMsg"]        = "You have the latest version",

            // ==== Bypass mode ====
            ["BypassModeWhitelist"]    = "Whitelist only",
            ["BypassModeAll"]          = "All sites",
            ["BypassModeHintWhitelist"] = "Bypass only domains from the whitelist",
            ["BypassModeHintAll"]       = "Bypass all traffic without filtering",

            // Test buttons (moved to the bottom bar)
            ["BtnTestStandard"]        = "Test bypass",
            ["BtnTestDpi"]             = "Test DPI",
            ["BtnTestStandardTip"]     = "Quick check: is the current preset working",
            ["BtnTestDpiTip"]          = "Deep test: check DPI bypass against popular resources",

        }
    };

    public string this[string key]
    {
        get
        {
            if (_translations.TryGetValue(_currentLanguage, out var langDict)
                && langDict.TryGetValue(key, out var text))
            {
                return text;
            }
            return key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
