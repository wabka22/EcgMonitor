using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

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
                    EspNetworkName = "karch_eeg_88005553535",
                    EspNetworkPassword = "12345678",
                    EspApIp = "192.168.4.1",
                    EspPort = 8888
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
    static void ConnectToNetwork(string ssid, string? name = null)
    {
        if (string.IsNullOrEmpty(ssid)) return;

        string networkName = name ?? ssid;
        Log($"Попытка подключения к сети {ssid}...", "INFO");

        try
        {
            Process.Start("netsh", $"wlan connect ssid=\"{ssid}\" name=\"{networkName}\"")?.WaitForExit();
            Thread.Sleep(5000);
            Log($"Подключение к {ssid} выполнено (или в процессе)...", "SUCCESS");
        }
        catch (Exception e)
        {
            Log($"Ошибка подключения к {ssid}: {e}", "ERROR");
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
                // Таймаут подключения
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
                $"1. Подключены ли вы к сети ESP ({espIp} - обычно это 192.168.4.1)\n" +
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
    static void StreamDataFromEsp(string espApIp, int espPort, string espNetworkName, string espNetworkPassword)
    {
        Log($"Подключение к ESP AP ({espNetworkName}) для стриминга данных...", "INFO");

        // Подключаемся к сети ESP
        ConnectToNetwork(espNetworkName);
        Thread.Sleep(5000);

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 5000;

                Log($"Подключение к {espApIp}:{espPort}...", "INFO");
                client.Connect(espApIp, espPort);

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
                                // Обработка данных
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
    }

    // ---------- ПОИСК ESP В СЕТИ ----------
    static string? DiscoverEspInNetwork(string baseIp = "192.168.4.")
    {
        Log("Попытка обнаружения ESP в сети...", "INFO");

        for (int i = 1; i < 255; i++)
        {
            string ip = baseIp + i;

            // Пропускаем типичные адреса шлюзов
            if (i == 1 || i == 254) continue;

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.SendTimeout = 1000;
                    client.ReceiveTimeout = 1000;

                    if (client.ConnectAsync(ip, 8888).Wait(500))
                    {
                        Log($"Найдена ESP по адресу: {ip}", "SUCCESS");
                        return ip;
                    }
                }
            }
            catch
            {
                // Игнорируем неудачные попытки
            }
        }

        Log("ESP не обнаружена в сети", "WARN");
        return null;
    }

    // ---------- ОСНОВНОЙ ЦИКЛ ----------
    static void Main()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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
                    StartStreaming(config);
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
        Console.WriteLine("║           ESP32 Data Streamer v1.0              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"Текущая конфигурация:");
        Console.WriteLine($"  Домашняя WiFi: {config.PcWifiSsid}");
        Console.WriteLine($"  Сеть ESP: {config.EspNetworkName}");
        Console.WriteLine($"  IP ESP: {config.EspApIp}:{config.EspPort}");
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
        Console.WriteLine("1. Отправляет данные вашей домашней WiFi на ESP");
        Console.WriteLine("2. Подключается к ESP и начинает стриминг данных");
        Console.WriteLine("3. Редактирует настройки подключения");
        Console.WriteLine("4. Автоматически ищет ESP в локальной сети");
        Console.WriteLine("5. Показывает текущие настройки");
        Console.WriteLine("6. Выход из программы");
        Console.WriteLine("\nВо время стриминга нажмите 'Q' для остановки.");
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
        Console.WriteLine($"IP-адрес ESP AP: {config.EspApIp}");
        Console.WriteLine($"Порт ESP: {config.EspPort}");
        Console.WriteLine($"Файл конфигурации: {Path.GetFullPath(ConfigFile)}");
        WaitForKey();
    }

    static void ConfigureEsp(Config config)
    {
        Console.Clear();
        Log("=== НАСТРОЙКА ESP ===", "INFO");

        bool success = SendWifiCredentialsToEsp(
            config.EspApIp,
            config.EspPort,
            config.PcWifiSsid,
            config.PcWifiPassword
        );

        if (success)
        {
            Log("Ждём 10 секунд, пока ESP применит настройки...", "INFO");
            Thread.Sleep(10000);

            // Возвращаемся к домашней сети
            Log("Возврат к домашней сети...", "INFO");
            ConnectToNetwork(config.PcWifiSsid);
        }

        WaitForKey();
    }

    static void StartStreaming(Config config)
    {
        Console.Clear();
        Log("=== ЗАПУСК СТРИМИНГА ===", "INFO");

        StreamDataFromEsp(
            config.EspApIp,
            config.EspPort,
            config.EspNetworkName,
            config.EspNetworkPassword
        );

        // Возвращаемся к домашней сети
        Log("Возврат к домашней сети...", "INFO");
        ConnectToNetwork(config.PcWifiSsid);

        WaitForKey("Нажмите любую клавишу для возврата в меню...");
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

        Console.Write($"IP-адрес ESP AP [{config.EspApIp}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspApIp = input;

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
        Log("=== АВТОПОИСК ESP ===", "INFO");

        string? discoveredIp = DiscoverEspInNetwork();
        if (discoveredIp != null)
        {
            Console.Write($"Обновить конфигурацию на IP {discoveredIp}? (y/n): ");
            string answer = Console.ReadLine()?.ToLower() ?? "";

            if (answer == "y" || answer == "yes" || answer == "да")
            {
                config.EspApIp = discoveredIp;
                SaveConfig(config);
                Log($"Конфигурация обновлена. Новый IP ESP: {discoveredIp}", "SUCCESS");
            }
        }

        WaitForKey();
    }
}