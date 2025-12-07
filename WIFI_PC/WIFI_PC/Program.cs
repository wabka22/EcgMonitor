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
                        Log("Стриминг начат. Нажмите любую клавишу для остановки...", "SUCCESS");
                    }
                    else
                    {
                        Log($"Неожиданный ответ от ESP: {response}", "WARN");
                    }

                    // Чтение данных в реальном времени
                    while (!Console.KeyAvailable)
                    {
                        if (stream.DataAvailable)
                        {
                            string data = reader.ReadLine();
                            if (!string.IsNullOrEmpty(data))
                            {
                                // Обработка данных (можно добавить парсинг)
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {data}");
                            }
                        }
                        Thread.Sleep(10);
                    }

                    // Очистка буфера клавиатуры
                    while (Console.KeyAvailable) Console.ReadKey(true);

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

    static void Main()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        Log("=== ESP32 Data Streamer ===", "INFO");
        Log($"Текущая директория: {Directory.GetCurrentDirectory()}", "INFO");

        // Загружаем конфигурацию
        Config config = LoadConfig();

        // Показываем текущую конфигурацию
        Log($"Конфигурация:", "INFO");
        Log($"  PC WiFi: {config.PcWifiSsid}", "INFO");
        Log($"  ESP сеть: {config.EspNetworkName}", "INFO");
        Log($"  ESP IP: {config.EspApIp}:{config.EspPort}", "INFO");

        Console.WriteLine("\nВыберите действие:");
        Console.WriteLine("1. Настроить ESP на подключение к домашней WiFi");
        Console.WriteLine("2. Начать стриминг данных");
        Console.WriteLine("3. Изменить конфигурацию");
        Console.WriteLine("4. Автоматический поиск ESP");
        Console.Write("\nВаш выбор (1-4): ");

        string choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                // Отправляем данные домашней сети на ESP
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
                }
                break;

            case "2":
                // Начинаем стриминг
                StreamDataFromEsp(
                    config.EspApIp,
                    config.EspPort,
                    config.EspNetworkName,
                    config.EspNetworkPassword
                );
                break;

            case "3":
                // Редактирование конфигурации
                EditConfiguration(config);
                break;

            case "4":
                // Автопоиск ESP
                string? discoveredIp = DiscoverEspInNetwork();
                if (discoveredIp != null)
                {
                    config.EspApIp = discoveredIp;
                    SaveConfig(config);
                    Log($"Конфигурация обновлена. Новый IP ESP: {discoveredIp}", "SUCCESS");
                }
                break;

            default:
                Log("Неверный выбор", "ERROR");
                break;
        }

        // Возврат к домашней сети
        Log("Возврат к домашней сети...", "INFO");
        ConnectToNetwork(config.PcWifiSsid);

        Log("Готово. Нажмите любую клавишу для выхода...", "SUCCESS");
        Console.ReadKey();
    }

    static void EditConfiguration(Config config)
    {
        Console.WriteLine("\n=== Редактирование конфигурации ===");

        Console.Write($"SSID домашней WiFi [{config.PcWifiSsid}]: ");
        string input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.PcWifiSsid = input;

        Console.Write($"Пароль домашней WiFi [{config.PcWifiPassword}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.PcWifiPassword = input;

        Console.Write($"Имя сети ESP [{config.EspNetworkName}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.EspNetworkName = input;

        Console.Write($"Пароль сети ESP [{config.EspNetworkPassword}]: ");
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
    }
}