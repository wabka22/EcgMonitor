using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

class Program
{
    const string ConfigFile = "config.json";

    // Класс для конфигурации
    class Config
    {
        public string PcWifiSsid { get; set; } = "";
        public string PcWifiPassword { get; set; } = "";
        public string EspNetworkName { get; set; } = "";
        public string EspNetworkPassword { get; set; } = "";
        public string EspApIp { get; set; } = "192.168.4.1";
        public int EspPort { get; set; } = 8888;
        public string? EspHomeIp { get; set; } // Новое поле для хранения IP в домашней сети
    }

    // ---------- ЛОГИРОВАНИЕ ----------
    static void Log(string msg, string level = "INFO")
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        ConsoleColor color = level switch
        {
            "INFO" => ConsoleColor.Cyan,
            "SUCCESS" => ConsoleColor.Green,
            "WARN" => ConsoleColor.Yellow,
            "ERROR" => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
        Console.ForegroundColor = color;
        Console.WriteLine($"[{ts}] [{level}] {msg}");
        Console.ResetColor();
    }

    // ---------- ПОЛУЧЕНИЕ АКТИВНОЙ ПОДСЕТИ ----------
    static string? GetActiveSubnet()
    {
        try
        {
            // Получаем активное WiFi подключение
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                           n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                           n.GetIPProperties().UnicastAddresses
                            .Any(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                .ToList();

            foreach (var ni in interfaces)
            {
                var ipInfo = ni.GetIPProperties();
                foreach (var addr in ipInfo.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        string[] parts = ip.Split('.');
                        if (parts.Length == 4)
                        {
                            // Игнорируем Docker/WSL подсети
                            if (parts[0] == "172" || parts[0] == "192" && parts[1] == "168")
                            {
                                string subnet = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                                Log($"Обнаружена активная подсеть WiFi: {subnet}", "INFO");
                                return subnet;
                            }
                        }
                    }
                }
            }

            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                           n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                           n.GetIPProperties().UnicastAddresses
                            .Any(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                .ToList();

            foreach (var ni in allInterfaces)
            {
                var ipInfo = ni.GetIPProperties();
                foreach (var addr in ipInfo.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        string[] parts = ip.Split('.');
                        if (parts.Length == 4)
                        {
                            string subnet = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                            Log($"Обнаружена подсеть интерфейса {ni.Name}: {subnet}", "INFO");
                            return subnet;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка при определении подсети: {ex.Message}", "WARN");
        }

        return null;
    }

    // ---------- ПОИСК ESP В ДОМАШНЕЙ СЕТИ ----------
    static List<string> DiscoverEspInNetwork(string? customSubnet = null)
    {
        List<string> foundDevices = new List<string>();

        List<string> subnetsToScan = new List<string>();

        if (!string.IsNullOrEmpty(customSubnet))
        {
            subnetsToScan.Add(customSubnet);
        }
        else
        {
            string? activeSubnet = GetActiveSubnet();
            if (activeSubnet != null)
            {
                subnetsToScan.Add(activeSubnet);
            }

            string[] commonSubnets = {
                "192.168.1.",
                "192.168.0.",
                "192.168.137.",
                "192.168.100.",
                "192.168.10.",
                "192.168.2.",
                "192.168.3.",
                "192.168.4.",
                "10.0.0.",
                "10.0.1.",
                "172.16.0.",
                "172.17.0.",
                "172.18.0.",
                "172.19.0."
            };

            foreach (var subnet in commonSubnets)
            {
                if (!subnetsToScan.Contains(subnet))
                {
                    subnetsToScan.Add(subnet);
                }
            }
        }

        Log($"Сканирование {subnetsToScan.Count} подсетей...", "INFO");

        foreach (string subnet in subnetsToScan)
        {
            Log($"Сканирование подсети: {subnet}", "INFO");
            List<string> devicesInSubnet = ScanSubnet(subnet);
            foundDevices.AddRange(devicesInSubnet);

            if (devicesInSubnet.Count > 0)
            {
                Log($"В подсети {subnet} найдено устройств: {devicesInSubnet.Count}", "SUCCESS");
            }
        }

        return foundDevices;
    }

    static List<string> ScanSubnet(string subnet)
    {
        List<string> foundDevices = new List<string>();
        object lockObj = new object();
        int totalScanned = 0;
        int total = 254;

        var tasks = new List<Task>();

        for (int i = 1; i <= total; i++)
        {
            int current = i;

            // Пропускаем .1 (шлюз) и .255 (широковещательный)
            if (current == 1 || current == 255)
            {
                continue;
            }

            tasks.Add(Task.Run(() =>
            {
                try
                {
                    string ip = subnet + current;

                    if (CheckEspAvailability(ip, 8888, 150))
                    {
                        lock (lockObj)
                        {
                            foundDevices.Add(ip);
                            Log($"Найдена ESP: {ip}", "SUCCESS");
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки
                }
                finally
                {
                    lock (lockObj)
                    {
                        totalScanned++;
                        if (totalScanned % 50 == 0)
                        {
                            Console.Write($"\rСканировано: {totalScanned}/{tasks.Count} адресов в подсети {subnet}...");
                        }
                    }
                }
            }));
        }

        // Ждем завершения всех задач
        Task.WhenAll(tasks).Wait();
        Console.WriteLine(); // Новая строка

        return foundDevices;
    }

    // ---------- ПРОВЕРКА ДОСТУПНОСТИ ESP ----------
    static bool CheckEspAvailability(string ip, int port, int timeout = 1000)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = timeout;
                client.ReceiveTimeout = timeout;

                // Асинхронное подключение с таймаутом
                var connectTask = client.ConnectAsync(ip, port);
                if (!connectTask.Wait(timeout))
                {
                    return false;
                }

                if (!client.Connected)
                {
                    return false;
                }

                // Проверяем, что это действительно ESP
                using (NetworkStream stream = client.GetStream())
                {
                    stream.WriteTimeout = timeout;

                    // Отправляем команду STATUS
                    byte[] ping = System.Text.Encoding.UTF8.GetBytes("STATUS\n");
                    stream.Write(ping, 0, ping.Length);

                    // Читаем ответ
                    StreamReader reader = new StreamReader(stream);
                    string response = reader.ReadLine();

                    return response?.Contains("ESP") == true ||
                           response?.Contains("AP SSID") == true ||
                           response?.Contains("karch") == true;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    // ---------- ЧТЕНИЕ КОНФИГУРАЦИИ ----------
    static Config LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<Config>(json);
            }
            else
            {
                // Создаем конфиг по умолчанию
                Config defaultConfig = new Config
                {
                    PcWifiSsid = "MyHomeWiFi",
                    PcWifiPassword = "mypassword122",
                    EspNetworkName = "karch_sin_88005553535",
                    EspNetworkPassword = "12345678",
                    EspApIp = "192.168.4.1",
                    EspPort = 8888,
                    EspHomeIp = null
                };

                SaveConfig(defaultConfig);
                Log($"Создан файл конфигурации: {ConfigFile}", "INFO");
                return defaultConfig;
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка загрузки конфигурации: {e}", "ERROR");
            return new Config();
        }
    }

    static void SaveConfig(Config config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception e)
        {
            Log($"Ошибка сохранения конфигурации: {e}", "ERROR");
        }
    }

    // ---------- ПОДКЛЮЧЕНИЕ К Wi-Fi ----------
    static bool ConnectToNetwork(string ssid, string? name = null)
    {
        if (string.IsNullOrEmpty(ssid)) return false;

        string networkName = name ?? ssid;
        Log($"Попытка подключения к сети {ssid}...", "INFO");

        try
        {
            // Сначала добавляем профиль если его нет
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"wlan connect ssid=\"{ssid}\" name=\"{networkName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(10000);

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(error))
                {
                    Log($"Ошибка подключения: {error.Trim()}", "WARN");

                    // Если профиля нет, создаем его
                    if (error.Contains("не присвоен профиль"))
                    {
                        Log($"Создание профиля для сети {ssid}...", "INFO");

                        string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
        </security>
    </MSM>
</WLANProfile>";

                        string tempFile = Path.GetTempFileName();
                        File.WriteAllText(tempFile, profileXml);

                        Process.Start("netsh", $"wlan add profile filename=\"{tempFile}\"")?.WaitForExit(5000);
                        File.Delete(tempFile);

                        // Пробуем снова подключиться
                        Process.Start("netsh", $"wlan connect ssid=\"{ssid}\" name=\"{ssid}\"")?.WaitForExit(10000);
                    }
                }

                Thread.Sleep(5000);
                Log($"Подключение к {ssid} выполнено", "SUCCESS");
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            Log($"Ошибка подключения к {ssid}: {e}", "ERROR");
            return false;
        }
    }

    // ---------- ОТПРАВКА ДАННЫХ НА ESP ----------
    static bool SendWifiCredentialsToEsp(string espIp, int espPort, string ssid, string pass)
    {
        Log($"Отправка данных на ESP ({espIp}:{espPort})...", "INFO");

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 5000;

                client.Connect(espIp, espPort);

                using (NetworkStream stream = client.GetStream())
                {
                    string data = $"SET\n{ssid}\n{pass}\n";
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
                    stream.Write(bytes, 0, bytes.Length);

                    // Чтение ответа
                    StreamReader reader = new StreamReader(stream);
                    string response = reader.ReadLine();

                    if (response?.Contains("OK") == true)
                    {
                        Log($"ESP ответила: {response.Trim()}", "SUCCESS");
                        return true;
                    }
                    else
                    {
                        Log($"ESP вернула ошибку: {response?.Trim()}", "WARN");
                        return false;
                    }
                }
            }
        }
        catch (SocketException e)
        {
            Log($"Не удалось подключиться к ESP по адресу {espIp}:{espPort}. Проверьте:\n" +
                $"1. Подключены ли вы к сети ESP\n" +
                $"2. Запущен ли сервер на ESP\n" +
                $"3. Правильно ли указан IP-адрес", "ERROR");
            return false;
        }
        catch (Exception e)
        {
            Log($"Ошибка при отправке данных на ESP: {e}", "ERROR");
            return false;
        }
    }

    // ---------- СТРИМИНГ ДАННЫХ С ESP ----------
    static void StreamDataFromEsp(Config config)
    {
        Console.Clear();
        Log("=== ЗАПУСК СТРИМИНГА ===", "INFO");

        string espIp = config.EspApIp;
        bool useHomeIp = false;

        // Пробуем использовать сохраненный домашний IP если есть
        if (!string.IsNullOrEmpty(config.EspHomeIp))
        {
            if (CheckEspAvailability(config.EspHomeIp, config.EspPort, 2000))
            {
                Log($"ESP доступна по домашнему IP: {config.EspHomeIp}", "SUCCESS");
                espIp = config.EspHomeIp;
                useHomeIp = true;
            }
        }

        // Если домашний IP не сработал, пробуем стандартный
        if (!useHomeIp && !CheckEspAvailability(espIp, config.EspPort, 2000))
        {
            Log($"ESP недоступна по адресу {espIp}:{config.EspPort}", "WARN");

            // Пытаемся найти ESP в сети
            Log("Поиск ESP в сети...", "INFO");
            var foundDevices = DiscoverEspInNetwork();

            if (foundDevices.Count > 0)
            {
                if (foundDevices.Count == 1)
                {
                    espIp = foundDevices[0];
                    Log($"Найдена ESP: {espIp}", "SUCCESS");

                    // Сохраняем найденный IP как домашний
                    config.EspHomeIp = espIp;
                    SaveConfig(config);
                }
                else
                {
                    Log($"Найдено несколько устройств:", "INFO");
                    for (int i = 0; i < foundDevices.Count; i++)
                    {
                        Console.WriteLine($"{i + 1}. {foundDevices[i]}");
                    }

                    Console.Write("Выберите устройство (1-{foundDevices.Count}): ");
                    if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0 && choice <= foundDevices.Count)
                    {
                        espIp = foundDevices[choice - 1];
                        config.EspHomeIp = espIp;
                        SaveConfig(config);
                        Log($"Выбрана ESP: {espIp}", "SUCCESS");
                    }
                }
            }
            else
            {
                // Подключаемся к AP режиму ESP
                Log("Подключение к AP режиму ESP...", "INFO");
                if (ConnectToNetwork(config.EspNetworkName, config.EspNetworkName))
                {
                    Thread.Sleep(5000);
                    espIp = "192.168.4.1";

                    if (!CheckEspAvailability(espIp, config.EspPort))
                    {
                        Log("Не удалось подключиться к ESP", "ERROR");
                        WaitForKey();
                        return;
                    }
                }
                else
                {
                    Log("Не удалось подключиться к сети ESP", "ERROR");
                    WaitForKey();
                    return;
                }
            }
        }

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 5000;

                Log($"Подключение к {espIp}:{config.EspPort}...", "INFO");
                client.Connect(espIp, config.EspPort);

                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    // Отправляем команду START_STREAM
                    byte[] cmdBytes = System.Text.Encoding.UTF8.GetBytes("START_STREAM\n");
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    // Ждем подтверждения
                    string response = reader.ReadLine();
                    if (response?.Contains("OK") == true)
                    {
                        Log("Стриминг начат. Нажмите 'Q' для остановки...", "SUCCESS");
                    }
                    else
                    {
                        Log($"Неожиданный ответ от ESP: {response}", "WARN");
                    }

                    // Чтение данных в реальном времени
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        Log("Прерывание стриминга...", "INFO");
                    };

                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                            {
                                break;
                            }
                        }

                        if (stream.DataAvailable)
                        {
                            string data = reader.ReadLine();
                            if (!string.IsNullOrEmpty(data))
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {data}");
                            }
                        }
                        Thread.Sleep(10);
                    }

                    // Отправляем команду остановки
                    byte[] stopBytes = System.Text.Encoding.UTF8.GetBytes("STOP_STREAM\n");
                    stream.Write(stopBytes, 0, stopBytes.Length);

                    Log("Стриминг остановлен.", "INFO");
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка при стриминге данных: {e}", "ERROR");
        }

        // Возвращаемся к домашней сети
        Log("Возврат к домашней сети...", "INFO");
        ConnectToNetwork(config.PcWifiSsid);
        WaitForKey("Нажмите любую клавишу для возврата в меню...");
    }

    // ---------- ОСНОВНОЙ ЦИКЛ ----------
    static void Main()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Console.Title = "ESP32 Data Streamer v3.0";

        // Загружаем конфигурацию
        Config config = LoadConfig();

        bool running = true;

        while (running)
        {
            Console.Clear();
            ShowHeader(config);

            string choice = GetMenuChoice();

            switch (choice)
            {
                case "1":
                    ConfigureEsp(config);
                    break;

                case "2":
                    StreamDataFromEsp(config);
                    break;

                case "3":
                    EditConfiguration(config);
                    break;

                case "4":
                    DiscoverEsp(config);
                    break;

                case "5":
                    ShowCurrentConfig(config);
                    break;

                case "6":
                    running = false;
                    Log("Выход из программы...", "INFO");
                    break;

                case "h":
                    ShowHelp();
                    break;

                default:
                    Log("Неверный выбор. Нажмите 'h' для помощи.", "ERROR");
                    WaitForKey();
                    break;
            }
        }

        // Возврат к домашней сети перед выходом
        Log("Возврат к домашней сети...", "INFO");
        ConnectToNetwork(config.PcWifiSsid);

        Log("Программа завершена. Нажмите любую клавишу...", "SUCCESS");
        Console.ReadKey();
    }

    // ---------- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ----------

    static void ShowHeader(Config config)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║           ESP32 Data Streamer v3.0              ║");
        Console.WriteLine("║       (с интеллектуальным поиском ESP)          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"Текущая конфигурация:");
        Console.WriteLine($"  Домашняя WiFi: {config.PcWifiSsid}");
        Console.WriteLine($"  Сеть ESP: {config.EspNetworkName}");
        Console.WriteLine($"  IP ESP (AP): {config.EspApIp}:{config.EspPort}");

        if (!string.IsNullOrEmpty(config.EspHomeIp))
        {
            Console.WriteLine($"  IP ESP (Home): {config.EspHomeIp}:{config.EspPort}");
        }

        // Проверяем доступность ESP
        bool apAvailable = CheckEspAvailability(config.EspApIp, config.EspPort, 1000);
        bool homeAvailable = !string.IsNullOrEmpty(config.EspHomeIp) &&
                            CheckEspAvailability(config.EspHomeIp, config.EspPort, 1000);

        Console.WriteLine($"  Статус: {(homeAvailable ? "✅ В домашней сети" : apAvailable ? "✅ В AP режиме" : "❌ Недоступна")}");

        Console.WriteLine(new string('-', 50));
    }

    static string GetMenuChoice()
    {
        Console.WriteLine("\nМеню:");
        Console.WriteLine("1. Настроить ESP на подключение к домашней WiFi");
        Console.WriteLine("2. Начать стриминг данных");
        Console.WriteLine("3. Изменить конфигурацию");
        Console.WriteLine("4. Автоматический поиск ESP");
        Console.WriteLine("5. Показать текущую конфигурацию");
        Console.WriteLine("6. Выход");
        Console.WriteLine("\n[h] Помощь");

        Console.Write("\nВаш выбор (1-6): ");
        return Console.ReadLine()?.ToLower().Trim() ?? "";
    }

    static void WaitForKey(string message = "Нажмите любую клавишу для продолжения...")
    {
        Console.WriteLine($"\n{message}");
        Console.ReadKey(true);
    }

    static void ShowHelp()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== СПРАВКА ===");
        Console.ResetColor();
        Console.WriteLine("\nОпции меню:");
        Console.WriteLine("1. Отправляет данные WiFi на ESP и настраивает подключение к домашней сети");
        Console.WriteLine("2. Автоматически находит ESP и начинает стриминг данных");
        Console.WriteLine("3. Редактирует настройки подключения");
        Console.WriteLine("4. Ищет ESP во всех возможных подсетях");
        Console.WriteLine("5. Показывает текущие настройки");
        Console.WriteLine("6. Выход из программы");
        Console.WriteLine("\nВо время стриминга нажмите 'Q' для остановки.");
        Console.WriteLine("\nESP автоматически сохраняет IP в домашней сети для быстрого подключения.");
        WaitForKey();
    }

    static void ShowCurrentConfig(Config config)
    {
        Console.Clear();
        Console.WriteLine("=== ТЕКУЩАЯ КОНФИГУРАЦИЯ ===");
        Console.WriteLine($"SSID домашней WiFi: {config.PcWifiSsid}");
        Console.WriteLine($"Пароль домашней WiFi: {new string('*', config.PcWifiPassword.Length)}");
        Console.WriteLine($"Имя сети ESP: {config.EspNetworkName}");
        Console.WriteLine($"Пароль сети ESP: {new string('*', config.EspNetworkPassword.Length)}");
        Console.WriteLine($"IP-адрес ESP (AP): {config.EspApIp}");
        Console.WriteLine($"IP-адрес ESP (Home): {config.EspHomeIp ?? "не задан"}");
        Console.WriteLine($"Порт ESP: {config.EspPort}");
        Console.WriteLine($"Файл конфигурации: {Path.GetFullPath(ConfigFile)}");

        // Проверка доступности
        bool apAvailable = CheckEspAvailability(config.EspApIp, config.EspPort);
        bool homeAvailable = !string.IsNullOrEmpty(config.EspHomeIp) &&
                            CheckEspAvailability(config.EspHomeIp, config.EspPort);

        Console.WriteLine($"\nСтатус:");
        Console.WriteLine($"  AP режим: {(apAvailable ? "✅ Доступна" : "❌ Недоступна")}");
        if (!string.IsNullOrEmpty(config.EspHomeIp))
        {
            Console.WriteLine($"  Домашняя сеть: {(homeAvailable ? "✅ Доступна" : "❌ Недоступна")}");
        }

        WaitForKey();
    }

    static void ConfigureEsp(Config config)
    {
        Console.Clear();
        Log("=== НАСТРОЙКА ESP ===", "INFO");

        // Подключаемся к сети ESP
        Log($"Подключение к сети ESP: {config.EspNetworkName}", "INFO");
        if (!ConnectToNetwork(config.EspNetworkName, config.EspNetworkName))
        {
            Log("Не удалось подключиться к сети ESP", "ERROR");
            WaitForKey();
            return;
        }

        Thread.Sleep(7000);

        bool success = SendWifiCredentialsToEsp(
            "192.168.4.1",
            config.EspPort,
            config.PcWifiSsid,
            config.PcWifiPassword
        );

        if (success)
        {
            Log("ESP настроена. Ожидание подключения к домашней WiFi...", "INFO");

            // Ждем 20 секунд
            for (int i = 0; i < 20; i++)
            {
                Console.Write($"\rОжидание: {i + 1}/20 секунд...");
                Thread.Sleep(1000);
            }
            Console.WriteLine();

            // Возвращаемся к домашней сети
            Log("Возврат к домашней сети...", "INFO");
            ConnectToNetwork(config.PcWifiSsid);

            Thread.Sleep(3000);

            // Ищем ESP в домашней сети
            Log("Поиск ESP в домашней сети...", "INFO");
            var foundDevices = DiscoverEspInNetwork();

            if (foundDevices.Count > 0)
            {
                string newIp = foundDevices[0];
                config.EspHomeIp = newIp;
                SaveConfig(config);
                Log($"ESP найдена в домашней сети: {newIp}", "SUCCESS");
                Log("IP сохранен в конфигурации", "INFO");
            }
            else
            {
                Log("ESP не найдена в домашней сети. Проверьте:", "WARN");
                Console.WriteLine("1. Подключена ли ESP к WiFi");
                Console.WriteLine("2. Правильность пароля WiFi");
                Console.WriteLine("3. Возможно ESP в другой подсети");
            }
        }
        else
        {
            Log("Не удалось настроить ESP", "ERROR");
        }

        WaitForKey();
    }

    static void EditConfiguration(Config config)
    {
        Console.Clear();
        Console.WriteLine("=== РЕДАКТИРОВАНИЕ КОНФИГУРАЦИИ ===\n");

        Console.WriteLine("Введите новые значения (оставьте пустым, чтобы не менять):");

        Console.Write($"SSID домашней WiFi [{config.PcWifiSsid}]: ");
        string input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.PcWifiSsid = input;

        Console.Write($"Пароль домашней WiFi [{new string('*', config.PcWifiPassword.Length)}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.PcWifiPassword = input;

        Console.Write($"Имя сети ESP [{config.EspNetworkName}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspNetworkName = input;

        Console.Write($"Пароль сети ESP [{new string('*', config.EspNetworkPassword.Length)}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspNetworkPassword = input;

        Console.Write($"IP-адрес ESP (AP) [{config.EspApIp}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspApIp = input;

        Console.Write($"IP-адрес ESP (Home) [{config.EspHomeIp ?? "не задан"}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspHomeIp = input;

        Console.Write($"Порт ESP [{config.EspPort}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int port))
        {
            config.EspPort = port;
        }

        SaveConfig(config);
        Log("Конфигурация сохранена", "SUCCESS");
        WaitForKey();
    }

    static void DiscoverEsp(Config config)
    {
        Console.Clear();
        Log("=== ПОИСК ESP ВО ВСЕХ СЕТЯХ ===", "INFO");

        Console.WriteLine("Выберите режим поиска:");
        Console.WriteLine("1. Автоматический поиск во всех подсетях");
        Console.WriteLine("2. Ручной ввод подсети для поиска");
        Console.Write("Ваш выбор: ");

        string mode = Console.ReadLine();
        List<string> foundDevices;

        if (mode == "2")
        {
            Console.Write("Введите подсеть для поиска (например, 192.168.1.): ");
            string subnet = Console.ReadLine();
            if (string.IsNullOrEmpty(subnet) || !subnet.EndsWith("."))
            {
                Log("Неверный формат подсети", "ERROR");
                WaitForKey();
                return;
            }

            foundDevices = DiscoverEspInNetwork(subnet);
        }
        else
        {
            foundDevices = DiscoverEspInNetwork();
        }

        if (foundDevices.Count > 0)
        {
            Log($"Найдено устройств: {foundDevices.Count}", "SUCCESS");

            if (foundDevices.Count == 1)
            {
                Console.Write($"\nОбновить домашний IP ESP на {foundDevices[0]}? (y/n): ");
                if (Console.ReadLine()?.ToLower() == "y")
                {
                    config.EspHomeIp = foundDevices[0];
                    SaveConfig(config);
                    Log($"IP сохранен: {foundDevices[0]}", "SUCCESS");
                }
            }
            else
            {
                Console.WriteLine("\nНайдено несколько устройств:");
                for (int i = 0; i < foundDevices.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {foundDevices[i]}");
                }

                Console.Write("Выберите устройство для сохранения (0 - не сохранять): ");
                if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0 && choice <= foundDevices.Count)
                {
                    config.EspHomeIp = foundDevices[choice - 1];
                    SaveConfig(config);
                    Log($"IP сохранен: {foundDevices[choice - 1]}", "SUCCESS");
                }
            }
        }
        else
        {
            Log("ESP не найдена", "WARN");
        }

        WaitForKey();
    }
}