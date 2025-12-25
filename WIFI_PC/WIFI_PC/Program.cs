using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

class Program
{
    const string ConfigFile = "config.json";

    // Глобальный список активных работников
    private static List<StreamWorker> _activeWorkers = new List<StreamWorker>();

    // Класс для конфигурации ОДНОГО ESP устройства
    class EspDeviceConfig
    {
        public string Name { get; set; } = "";
        public string ApSsid { get; set; } = "";
        public string ApPassword { get; set; } = "";
        public string ApIp { get; set; } = "192.168.4.1";
        public int Port { get; set; } = 8888;
        public string? HotspotIp { get; set; } // IP в сети хот-спота
        public string MacAddress { get; set; } = ""; // MAC-адрес устройства
    }

    // Главный класс конфигурации
    class Config
    {
        public string HotspotSsid { get; set; } = "MyHomeWiFi"; // SSID хот-спота компьютера
        public string HotspotPassword { get; set; } = "mypassword122"; // Пароль хот-спота
        public string Esp1NetworkName { get; set; } = "ESP32_Cos_Streamer";
        public string Esp2NetworkName { get; set; } = "ESP32_Sin_Streamer";
        public string EspNetworkPassword { get; set; } = "12345678";
        public List<EspDeviceConfig> EspDevices { get; set; } = new List<EspDeviceConfig>();
    }

    // ---------- ЛОГИРОВАНИЕ ----------
    static void Log(string msg, string level = "INFO", string deviceTag = "")
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
        string tag = string.IsNullOrEmpty(deviceTag) ? "" : $"[{deviceTag}] ";
        Console.WriteLine($"[{ts}] [{level}] {tag}{msg}");
        Console.ResetColor();
    }

    // ---------- ПРИНУДИТЕЛЬНАЯ ОСТАНОВКА ВСЕХ АКТИВНЫХ СТРИМОВ ----------
    static void StopAllActiveStreams()
    {
        if (_activeWorkers.Count == 0)
        {
            Log("Нет активных стримов для остановки", "INFO");
            return;
        }

        Log($"Останавливаю {_activeWorkers.Count} активных стрима(ов)...", "INFO");

        foreach (var worker in _activeWorkers.ToList())
        {
            try
            {
                worker.Stop();
                Log($"Стрим для {worker.Device.Name} остановлен", "SUCCESS", worker.Device.Name);
            }
            catch (Exception ex)
            {
                Log($"Ошибка при остановке стрима {worker.Device.Name}: {ex.Message}", "ERROR");
            }
        }

        _activeWorkers.Clear();
        Thread.Sleep(1000);
        Log("Все активные стримы остановлены", "SUCCESS");
    }

    // ---------- ПРОВЕРКА ПОДКЛЮЧЕНИЯ К СЕТИ ----------
    static bool IsConnectedToNetwork(string networkName)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866)
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Проверяем несколько вариантов
            if (output.Contains($" {networkName} ") ||
                output.Contains($": {networkName}") ||
                output.Contains($"SSID : {networkName}") ||
                output.Contains($"SSID : {networkName}"))
            {
                // Проверяем статус подключения
                string[] connectedMarkers = { "State : connected", "Состояние : подключено",
                                          "State             : connected", "Состояние         : подключено" };
                foreach (string marker in connectedMarkers)
                {
                    if (output.Contains(marker))
                    {
                        return true;
                    }
                }

                // Если нашли сеть, но нет явного статуса, проверяем контекст
                int ssidIndex = output.IndexOf(networkName, StringComparison.OrdinalIgnoreCase);
                if (ssidIndex >= 0)
                {
                    // Берем 200 символов вокруг найденного SSID
                    int start = Math.Max(0, ssidIndex - 100);
                    int length = Math.Min(200, output.Length - start);
                    string context = output.Substring(start, length);

                    // Ищем маркеры подключения в контексте
                    string[] contextMarkers = { "connected", "подключено", "authenticated", "аутентифицирован" };
                    foreach (string marker in contextMarkers)
                    {
                        if (context.Contains(marker, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        catch (Exception e)
        {
            Log($"Ошибка при проверке подключения: {e.Message}", "ERROR");
            return false;
        }
    }

    // ---------- ПОДКЛЮЧЕНИЕ К СЕТИ ESP ----------
    static bool ConnectToEspNetwork(string networkName, string password)
    {
        Log($"Подключение к сети ESP: {networkName}...", "INFO");

        try
        {
            // Отключаемся от текущей сети
            RunNetshCommand("wlan disconnect", false);
            Thread.Sleep(2000);

            // Создаем XML профиль для WiFi сети
            string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{networkName}</name>
    <SSIDConfig>
        <SSID>
            <name>{networkName}</name>
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
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

            // Сохраняем профиль во временный файл
            string tempFile = Path.Combine(Path.GetTempPath(), $"wifi_{Guid.NewGuid().ToString("N").Substring(0, 8)}.xml");
            File.WriteAllText(tempFile, profileXml, Encoding.UTF8);

            // Удаляем старый профиль если есть
            RunNetshCommand($"wlan delete profile name=\"{networkName}\"", false);
            Thread.Sleep(500);

            // Добавляем новый профиль
            string addResult = RunNetshCommand($"wlan add profile filename=\"{tempFile}\"");
            if (addResult.Contains("added") || addResult.Contains("добавлен"))
            {
                Log("Профиль WiFi успешно добавлен", "SUCCESS");
            }
            else
            {
                Log($"Ошибка добавления профиля: {addResult}", "WARN");
            }

            // Подключаемся к сети
            Log($"Подключаюсь к сети {networkName}...", "INFO");
            string connectResult = RunNetshCommand($"wlan connect name=\"{networkName}\"");

            if (connectResult.Contains("requested") || connectResult.Contains("выполнен") ||
                connectResult.Contains("connected") || connectResult.Contains("подключен"))
            {
                Log($"Запрос на подключение к {networkName} отправлен", "SUCCESS");
            }
            else
            {
                Log($"Результат подключения: {connectResult}", "WARN");
            }

            // Удаляем временный файл
            try { File.Delete(tempFile); } catch { }

            // Ждем подключения
            for (int i = 0; i < 15; i++) // Ждем до 15 секунд
            {
                Thread.Sleep(1000);
                if (IsConnectedToNetwork(networkName))
                {
                    Log($"Успешно подключен к сети {networkName}!", "SUCCESS");
                    Thread.Sleep(2000);
                    return true;
                }

                if (i % 5 == 0) // Каждые 5 секунд показываем прогресс
                {
                    Log($"Ожидание подключения... {i + 1}/15 секунд", "INFO");
                }
            }

            Log("Не удалось подключиться к сети ESP", "ERROR");

            // Показываем доступные сети для диагностики
            Log("Доступные сети WiFi:", "INFO");
            string networks = RunNetshCommand("wlan show networks");
            Console.WriteLine(networks);

            return false;
        }
        catch (Exception e)
        {
            Log($"Ошибка подключения: {e.Message}", "ERROR");
            return false;
        }
    }

    // ---------- РУЧНОЕ ПОДКЛЮЧЕНИЕ К ESP ----------
    static bool ManualConnectToEsp(string networkName, string password)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                РУЧНОЕ ПОДКЛЮЧЕНИЕ К ESP                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"\nДля продолжения подключитесь к сети ESP:");
        Console.WriteLine($"\nНазвание сети: {networkName}");
        Console.WriteLine($"Пароль: {password}");

        Console.WriteLine("\nИнструкция:");
        Console.WriteLine("1. Нажмите на иконку WiFi в правом нижнем углу экрана");
        Console.WriteLine("2. Найдите сеть " + networkName);
        Console.WriteLine("3. Нажмите на неё и введите пароль");
        Console.WriteLine("4. Подождите пока подключится");
        Console.WriteLine("5. Вернитесь в эту программу и нажмите Enter");

        Console.Write("\nНажмите Enter когда подключитесь...");
        Console.ReadLine();

        return IsConnectedToNetwork(networkName);
    }

    // ---------- ВОЗВРАТ К ХОТ-СПОТУ ----------
    static void ReconnectToHotspot(Config config)
    {
        Log("Возвращаюсь в режим хот-спота...", "INFO");

        try
        {
            // Отключаемся от текущей сети
            RunNetshCommand("wlan disconnect", false);
            Thread.Sleep(2000);

            // Включаем режим хот-спота (если выключен)
            Log("Проверяю режим хот-спота...", "INFO");
            var netshOutput = RunNetshCommand("wlan show hostednetwork");

            if (!netshOutput.Contains("Запущена") && !netshOutput.Contains("Started"))
            {
                Log("Хот-спот не запущен. Запускаю...", "INFO");
                RunNetshCommand("wlan start hostednetwork");
                Thread.Sleep(3000);
            }

            Log("Компьютер в режиме хот-спота", "SUCCESS");
        }
        catch (Exception e)
        {
            Log($"Ошибка при возврате в хот-спот: {e.Message}", "ERROR");
        }
    }

    // ---------- ВЫПОЛНЕНИЕ КОМАНД netsh ----------
    static string RunNetshCommand(string arguments, bool showErrors = true)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866),
                    StandardErrorEncoding = Encoding.GetEncoding(866)
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(error) && showErrors)
            {
                Log($"netsh error: {error}", "WARN");
            }

            return output + error;
        }
        catch (Exception e)
        {
            Log($"Ошибка выполнения netsh: {e.Message}", "ERROR");
            return "";
        }
    }

    // ---------- ПРОВЕРКА ДОСТУПНОСТИ ESP ----------
    static bool CheckEspAvailability(string ip, int port, int timeout = 1000)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeout);
                if (!success) return false;
                client.EndConnect(result);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.WriteTimeout = timeout;
                    stream.ReadTimeout = timeout;

                    // Отправляем простую команду PING
                    byte[] ping = Encoding.UTF8.GetBytes("PING\n");
                    stream.Write(ping, 0, ping.Length);

                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    return !string.IsNullOrEmpty(response);
                }
            }
        }
        catch { return false; }
    }

    // ---------- ОТПРАВКА КОМАНДЫ НА ESP ----------
    static string SendCommandToEsp(string ip, int port, string command, int timeout = 3000)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeout);
                if (!success) return "ERROR: Connection failed";
                client.EndConnect(result);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.WriteTimeout = timeout;
                    stream.ReadTimeout = timeout;
                    byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    byte[] buffer = new byte[4096];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
        }
        catch (Exception e) { return $"ERROR: {e.Message}"; }
    }

    // ---------- БЕЗОПАСНАЯ ПРОВЕРКА И ПОДКЛЮЧЕНИЕ К ESP ----------
    static string SafelyConnectToEsp(EspDeviceConfig device, Config globalConfig, string operationName = "операции")
    {
        Console.Clear();
        Log($"=== {operationName.ToUpper()} для {device.Name} ===", "INFO");

        // Проверяем, подключены ли уже к нужной сети
        bool isConnected = IsConnectedToNetwork(device.ApSsid);

        if (!isConnected)
        {
            Console.WriteLine($"\nДля работы с {device.Name} необходимо подключиться к его WiFi сети.");
            Console.WriteLine($"\nСеть: {device.ApSsid}");
            Console.WriteLine($"Пароль: {device.ApPassword}");

            Console.WriteLine("\nВыберите способ подключения:");
            Console.WriteLine("1. Автоматическое подключение");
            Console.WriteLine("2. Ручное подключение (рекомендуется)");
            Console.WriteLine("3. Отмена");

            Console.Write("\nВаш выбор (1-3): ");
            string choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    if (!ConnectToEspNetwork(device.ApSsid, device.ApPassword))
                    {
                        Log($"Не удалось подключиться к WiFi сети {device.ApSsid}", "ERROR");
                        return null;
                    }
                    break;
                case "2":
                    if (!ManualConnectToEsp(device.ApSsid, device.ApPassword))
                    {
                        Log($"Не удалось подключиться к WiFi сети {device.ApSsid}", "ERROR");
                        return null;
                    }
                    break;
                default:
                    return null;
            }
        }
        else
        {
            Log($"Уже подключен к сети {device.ApSsid}", "INFO");
        }

        // Ждем, чтобы ESP могла принять соединение
        Log("Ожидание готовности ESP...", "INFO");
        Thread.Sleep(3000);

        // Пробуем подключиться к ESP с несколькими попытками
        int maxAttempts = 5;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Log($"Попытка подключения {attempt}/{maxAttempts}...", "INFO");

            if (CheckEspAvailability(device.ApIp, device.Port, 2000))
            {
                Log($"{device.Name} доступен по адресу {device.ApIp}", "SUCCESS", device.Name);
                return device.ApIp;
            }

            if (attempt < maxAttempts)
            {
                Log("ESP не отвечает, жду 2 секунды...", "WARN");
                Thread.Sleep(2000);
            }
        }

        // Если ESP не отвечает
        Log($"Не удалось подключиться к {device.Name}", "ERROR", device.Name);
        Console.WriteLine($"\nВозможные причины:");
        Console.WriteLine($"1. ESP не включена или не раздает WiFi");
        Console.WriteLine($"2. ESP уже стримит и не принимает новые соединения");
        Console.WriteLine($"3. Неправильные настройки IP/порта");

        Console.WriteLine($"\nРекомендуемые действия:");
        Console.WriteLine($"1. Нажмите кнопку RESET на {device.Name}");
        Console.WriteLine($"2. Подождите 10 секунд после перезагрузки");
        Console.WriteLine($"3. Попробуйте снова");

        Console.WriteLine($"\nДля принудительного переподключения нажмите 'R'");
        Console.WriteLine($"Для отмены нажмите любую другую клавишу...");

        if (Console.ReadKey(true).Key == ConsoleKey.R)
        {
            // Принудительное переподключение
            Log("Принудительное переподключение...", "INFO");
            RunNetshCommand("wlan disconnect", false);
            Thread.Sleep(3000);

            if (ManualConnectToEsp(device.ApSsid, device.ApPassword))
            {
                Thread.Sleep(5000); // Даем ESP время на инициализацию

                if (CheckEspAvailability(device.ApIp, device.Port, 3000))
                {
                    Log($"{device.Name} теперь доступен!", "SUCCESS", device.Name);
                    return device.ApIp;
                }
            }
        }

        return null;
    }

    // ---------- ОТПРАВКА ДАННЫХ WIFI НА ESP ----------
    static bool SendWifiCredentialsToEsp(string espIp, int espPort, string ssid, string pass, string deviceTag)
    {
        Log($"Отправка данных WiFi на {deviceTag}...", "INFO", deviceTag);
        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(espIp, espPort);
                using (NetworkStream stream = client.GetStream())
                {
                    string data = $"SET\n{ssid}\n{pass}\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    stream.Write(bytes, 0, bytes.Length);

                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (response.Contains("OK"))
                    {
                        Log($"Данные WiFi отправлены успешно. ESP перезагрузится.", "SUCCESS", deviceTag);
                        return true;
                    }
                    else
                    {
                        Log($"Ошибка: {response.Trim()}", "WARN", deviceTag);
                        return false;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка: {e.Message}", "ERROR", deviceTag);
            return false;
        }
    }

    // ---------- ЧТЕНИЕ И СОХРАНЕНИЕ КОНФИГУРАЦИИ ----------
    static Config LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<Config>(json);

                if (config.EspDevices == null || config.EspDevices.Count == 0)
                {
                    Log("Обнаружен старый формат config.json. Выполняю автоматическое обновление...", "INFO");
                    config.EspDevices = new List<EspDeviceConfig>();

                    config.EspDevices.Add(new EspDeviceConfig
                    {
                        Name = "ESP32_Cos",
                        ApSsid = config.Esp1NetworkName,
                        ApPassword = config.EspNetworkPassword,
                        ApIp = "192.168.4.1",
                        Port = 8888,
                        MacAddress = "c4:de:e2:19:2b:6c" // MAC-адрес ESP32_Cos
                    });

                    config.EspDevices.Add(new EspDeviceConfig
                    {
                        Name = "ESP32_Sin",
                        ApSsid = config.Esp2NetworkName,
                        ApPassword = config.EspNetworkPassword,
                        ApIp = "192.168.4.1",
                        Port = 8888,
                        MacAddress = "cc:7b:5c:34:cc:f8" // MAC-адрес ESP32_Sin
                    });

                    SaveConfig(config);
                    Log("Конфигурация успешно обновлена до нового формата с двумя устройствами.", "SUCCESS");
                }
                return config;
            }
            else
            {
                // Конфигурация для работы через хот-спот
                Config defaultConfig = new Config
                {
                    HotspotSsid = "MyHomeWiFi", // SSID вашей домашней сети
                    HotspotPassword = "mypassword122", // Пароль вашей домашней сети
                    Esp1NetworkName = "ESP32_Cos_Streamer",
                    Esp2NetworkName = "ESP32_Sin_Streamer",
                    EspNetworkPassword = "12345678",
                    EspDevices = new List<EspDeviceConfig>
                    {
                        new EspDeviceConfig
                        {
                            Name = "ESP32_Cos",
                            ApSsid = "ESP32_Cos_Streamer",
                            ApPassword = "12345678",
                            ApIp = "192.168.4.1",
                            Port = 8888,
                            HotspotIp = "192.168.137.102", // Прямое назначение IP из вашей сети
                            MacAddress = "c4:de:e2:19:2b:6c"
                        },
                        new EspDeviceConfig
                        {
                            Name = "ESP32_Sin",
                            ApSsid = "ESP32_Sin_Streamer",
                            ApPassword = "12345678",
                            ApIp = "192.168.4.1",
                            Port = 8888,
                            HotspotIp = "192.168.137.173", // Прямое назначение IP из вашей сети
                            MacAddress = "cc:7b:5c:34:cc:f8"
                        }
                    }
                };

                SaveConfig(defaultConfig);
                Log($"Создан новый файл конфигурации: {ConfigFile}", "INFO");
                Log($"ВАЖНО: В конфиг уже добавлены IP-адреса ваших ESP из сети {defaultConfig.HotspotSsid}!", "SUCCESS");
                return defaultConfig;
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка загрузки конфигурации: {e.Message}", "ERROR");
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
            Log($"Ошибка сохранения конфигурации: {e.Message}", "ERROR");
        }
    }

    // ---------- КЛАСС ДЛЯ ПАРАЛЛЕЛЬНОГО СТРИМИНГА ----------
    class StreamWorker
    {
        public EspDeviceConfig Device { get; }
        public string Ip { get; }
        private volatile bool _running = false;
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private readonly Queue<string> _dataQueue = new Queue<string>();
        private readonly object _lock = new object();

        public StreamWorker(EspDeviceConfig device, string ip)
        {
            Device = device;
            Ip = ip;
        }

        public bool StartStreaming()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(Ip, Device.Port);
                _stream = _client.GetStream();
                _reader = new StreamReader(_stream);

                byte[] cmdBytes = Encoding.UTF8.GetBytes("START_STREAM\n");
                _stream.Write(cmdBytes, 0, cmdBytes.Length);

                _running = true;

                lock (_activeWorkers)
                {
                    _activeWorkers.Add(this);
                }

                Log($"Работник для {Device.Name} добавлен в активные", "INFO", Device.Name);

                // Чтение данных
                while (_running && _client.Connected)
                {
                    if (_stream.DataAvailable)
                    {
                        string data = _reader.ReadLine();
                        if (!string.IsNullOrEmpty(data))
                        {
                            lock (_lock)
                            {
                                if (_dataQueue.Count > 100) _dataQueue.Dequeue();
                                _dataQueue.Enqueue(data);
                            }
                        }
                    }
                    Thread.Sleep(10);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Ошибка в потоке для {Device.Name}: {ex.Message}", "ERROR", Device.Name);
                return false;
            }
        }

        public List<string> GetRecentData()
        {
            lock (_lock)
            {
                return _dataQueue.ToList();
            }
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_stream != null)
                {
                    byte[] stopBytes = Encoding.UTF8.GetBytes("STOP_STREAM\n");
                    _stream.Write(stopBytes, 0, stopBytes.Length);
                    Thread.Sleep(100);
                }
                _client?.Close();

                lock (_activeWorkers)
                {
                    _activeWorkers.Remove(this);
                }
            }
            catch { }
        }
    }

    // ---------- ОТОБРАЖЕНИЕ ПАРАЛЛЕЛЬНЫХ ДАННЫХ ----------
    static void DisplayParallelStreams(List<StreamWorker> workers)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("ПАРАЛЛЕЛЬНЫЙ СТРИМИНГ | ЛЕВО: КОСИНУС (ESP1) | ПРАВО: СИНУС (ESP2)");
        Console.WriteLine(new string('=', 80));

        try
        {
            while (true)
            {
                Console.SetCursorPosition(0, Console.CursorTop);

                if (workers.Count >= 2 && workers[0] != null && workers[1] != null)
                {
                    var data1 = workers[0].GetRecentData();
                    var data2 = workers[1].GetRecentData();

                    int maxLines = Math.Max(data1.Count, data2.Count);
                    int startIdx = Math.Max(0, maxLines - 10);

                    for (int i = startIdx; i < maxLines; i++)
                    {
                        string left = i < data1.Count ? data1[i] : "";
                        string right = i < data2.Count ? data2[i] : "";

                        string leftFormatted = string.IsNullOrEmpty(left) ? "" : $"{left,-35}";
                        string rightFormatted = string.IsNullOrEmpty(right) ? "" : $"{right,-35}";

                        Console.WriteLine($"[{workers[0].Device.Name}] {leftFormatted} | [{workers[1].Device.Name}] {rightFormatted}");
                    }
                }

                Thread.Sleep(500);
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка отображения: {ex.Message}", "ERROR");
        }
    }

    // ---------- ГЛАВНОЕ МЕНЮ ----------
    static string GetMenuChoice()
    {
        Console.WriteLine("\nГЛАВНОЕ МЕНЮ:");
        Console.WriteLine("--- ОДИНОЧНЫЕ ОПЕРАЦИИ ---");
        Console.WriteLine("1. Настроить ESP на домашнюю сеть компьютера");
        Console.WriteLine("2. Начать стриминг с одной ESP");
        Console.WriteLine("3. Изменить конфигурацию");
        Console.WriteLine("4. Найти ESP в домашней сети");
        Console.WriteLine("5. Показать текущую конфигурацию");

        Console.WriteLine("\n--- ПАРАЛЛЕЛЬНЫЕ ОПЕРАЦИИ ---");
        Console.WriteLine("6. Параллельная настройка двух ESP");
        Console.WriteLine("7. Параллельный стриминг с двух ESP");

        Console.WriteLine("\n--- СЛУЖЕБНЫЕ ---");
        Console.WriteLine("8. Остановить все активные стримы");
        Console.WriteLine("9. Проверить доступность ESP");
        Console.WriteLine("10. Диагностика WiFi");
        Console.WriteLine("11. Прямое подключение к ESP (по IP)");
        Console.WriteLine("12. Выход");

        Console.WriteLine("\n[h] Помощь по командам ESP");

        Console.Write("\nВаш выбор (1-12): ");
        return Console.ReadLine()?.ToLower().Trim() ?? "";
    }

    // ---------- ОТОБРАЖЕНИЕ ЗАГОЛОВКА ----------
    static void ShowHeader(Config config)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       ESP32 Dual Stream Manager - Работа через домашнюю сеть       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"\nРежим: РАБОТА ЧЕРЕЗ ДОМАШНЮЮ СЕТЬ");
        Console.WriteLine($"Домашняя сеть: {config.HotspotSsid}");

        Console.WriteLine($"\nЗарегистрированные устройства: {config.EspDevices.Count}");

        foreach (var device in config.EspDevices)
        {
            Console.Write($"  {device.Name}: ");
            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"✓ {device.HotspotIp}");
                Console.ResetColor();
                Console.WriteLine($" (MAC: {device.MacAddress})");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"✗ IP не назначен");
                Console.ResetColor();
            }
        }

        Console.WriteLine($"\nАктивных стримов: {_activeWorkers.Count}");
        if (_activeWorkers.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Внимание: активные стримы занимают соединение с ESP!");
            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 60));
    }

    // ==================== ОСНОВНЫЕ ФУНКЦИИ ====================

    // ---------- 1. НАСТРОЙКА ОДНОЙ ESP НА ДОМАШНЮЮ СЕТЬ ----------
    static void ConfigureSingleEsp(Config config)
    {
        if (config.EspDevices.Count == 0)
        {
            Log("Нет настроенных устройств.", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine("\nВыберите устройство для настройки:");
        for (int i = 0; i < config.EspDevices.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {config.EspDevices[i].Name} ({config.EspDevices[i].ApSsid})");
        }
        Console.Write($"\nВаш выбор (1-{config.EspDevices.Count}): ");

        if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > config.EspDevices.Count)
        {
            Log("Неверный выбор.", "ERROR");
            return;
        }

        var device = config.EspDevices[choice - 1];

        // Останавливаем все активные стримы ПЕРЕД настройкой
        StopAllActiveStreams();

        string espIp = SafelyConnectToEsp(device, config, "НАСТРОЙКА НА ДОМАШНЮЮ СЕТЬ");

        if (string.IsNullOrEmpty(espIp)) return;

        Console.WriteLine($"\n=== НАСТРОЙКА ДОМАШНЕЙ СЕТИ ДЛЯ {device.Name} ===");
        Console.WriteLine($"\nТекущие данные домашней сети:");
        Console.WriteLine($"SSID: {config.HotspotSsid}");
        Console.WriteLine($"Пароль: {new string('*', config.HotspotPassword.Length)}");

        Console.Write("\nИспользовать эти данные? (y/n): ");
        if (Console.ReadLine()?.ToLower() != "y")
        {
            Console.Write("Введите SSID вашей домашней сети: ");
            config.HotspotSsid = Console.ReadLine();
            Console.Write("Введите пароль домашней сети: ");
            config.HotspotPassword = Console.ReadLine();
            SaveConfig(config);
        }

        bool success = SendWifiCredentialsToEsp(espIp, device.Port, config.HotspotSsid, config.HotspotPassword, device.Name);
        if (success)
        {
            Log($"\n{device.Name} перезагружается и попытается подключиться к домашней сети.", "INFO");
            Log("Это займет 30-40 секунд...", "INFO");

            // Даем ESP время на перезагрузку
            Thread.Sleep(30000);

            Log("Ожидание подключения ESP к домашней сети...", "INFO");
            Thread.Sleep(10000);

            // Пробуем найти ESP в домашней сети
            FindAndSaveEspInNetwork(device, config);
        }
        WaitForKey();
    }

    // ---------- 2. СТРИМИНГ С ОДНОЙ ESP ----------
    static void StreamFromSingleEsp(Config config)
    {
        if (config.EspDevices.Count == 0)
        {
            Log("Нет настроенных устройств.", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine("\nВыберите устройство для стриминга:");
        for (int i = 0; i < config.EspDevices.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {config.EspDevices[i].Name} ({config.EspDevices[i].ApSsid})");
        }
        Console.Write($"\nВаш выбор (1-{config.EspDevices.Count}): ");

        if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > config.EspDevices.Count)
        {
            Log("Неверный выбор.", "ERROR");
            return;
        }

        var device = config.EspDevices[choice - 1];

        // Останавливаем все активные стримы перед новым стримингом
        StopAllActiveStreams();

        // Пробуем сначала через домашнюю сеть, если есть IP
        if (!string.IsNullOrEmpty(device.HotspotIp) && CheckEspAvailability(device.HotspotIp, device.Port, 1000))
        {
            Log($"Подключение через домашнюю сеть: {device.HotspotIp}", "INFO", device.Name);
            StartSingleStream(device, device.HotspotIp);
        }
        else
        {
            // Иначе через AP режим ESP
            string espIp = SafelyConnectToEsp(device, config, "СТРИМИНГ ДАННЫХ");
            if (!string.IsNullOrEmpty(espIp))
            {
                StartSingleStream(device, espIp);
            }
        }
        WaitForKey();
    }

    static void StartSingleStream(EspDeviceConfig device, string ip)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(ip, device.Port);
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    byte[] cmdBytes = Encoding.UTF8.GetBytes("START_STREAM\n");
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    string response = reader.ReadLine();
                    Log($"Ответ: {response}", "INFO", device.Name);
                    Log("Стриминг начат. Нажмите 'Q' для остановки...", "SUCCESS", device.Name);

                    DateTime lastDataTime = DateTime.Now;
                    while (true)
                    {
                        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q) break;

                        if (stream.DataAvailable)
                        {
                            string data = reader.ReadLine();
                            if (!string.IsNullOrEmpty(data))
                            {
                                lastDataTime = DateTime.Now;
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{device.Name}] {data}");
                            }
                        }
                        else if ((DateTime.Now - lastDataTime).TotalSeconds > 5)
                        {
                            try
                            {
                                byte[] ping = Encoding.UTF8.GetBytes("PING\n");
                                stream.Write(ping, 0, ping.Length);
                            }
                            catch
                            {
                                Log("Потеряно соединение", "ERROR", device.Name);
                                break;
                            }
                        }
                        Thread.Sleep(10);
                    }

                    try
                    {
                        byte[] stopBytes = Encoding.UTF8.GetBytes("STOP_STREAM\n");
                        stream.Write(stopBytes, 0, stopBytes.Length);
                        Log("Стриминг остановлен.", "INFO", device.Name);
                    }
                    catch { }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка: {e.Message}", "ERROR", device.Name);
        }
    }

    // ---------- 6. ПАРАЛЛЕЛЬНАЯ НАСТРОЙКА ДВУХ ESP ----------
    static void ConfigureTwoEspParallel(Config config)
    {
        if (config.EspDevices.Count < 2)
        {
            Log("Требуется минимум 2 устройства.", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine("\n=== ПАРАЛЛЕЛЬНАЯ НАСТРОЙКА ДВУХ ESP ===");
        Console.WriteLine($"\nБудет выполнена настройка для:");
        Console.WriteLine($"1. {config.EspDevices[0].Name}");
        Console.WriteLine($"2. {config.EspDevices[1].Name}");

        Console.Write("\nИспользовать текущие данные домашней сети? (y/n): ");
        if (Console.ReadLine()?.ToLower() != "y")
        {
            Console.Write("Введите SSID вашей домашней сети: ");
            config.HotspotSsid = Console.ReadLine();
            Console.Write("Введите пароль домашней сети: ");
            config.HotspotPassword = Console.ReadLine();
            SaveConfig(config);
        }

        bool allSuccess = true;

        // Настройка первой ESP
        Log("\n=== НАСТРОЙКА ПЕРВОЙ ESP ===", "INFO");
        StopAllActiveStreams();

        string ip1 = SafelyConnectToEsp(config.EspDevices[0], config, "НАСТРОЙКА ESP1");
        if (!string.IsNullOrEmpty(ip1))
        {
            bool success1 = SendWifiCredentialsToEsp(ip1, config.EspDevices[0].Port,
                config.HotspotSsid, config.HotspotPassword, config.EspDevices[0].Name);
            allSuccess &= success1;

            if (success1)
            {
                Log($"{config.EspDevices[0].Name} перезагружается. Ждем 20 секунд...", "INFO");
                Thread.Sleep(20000);
            }
        }
        else allSuccess = false;

        // Настройка второй ESP
        Log("\n=== НАСТРОЙКА ВТОРОЙ ESP ===", "INFO");
        StopAllActiveStreams();

        string ip2 = SafelyConnectToEsp(config.EspDevices[1], config, "НАСТРОЙКА ESP2");
        if (!string.IsNullOrEmpty(ip2))
        {
            bool success2 = SendWifiCredentialsToEsp(ip2, config.EspDevices[1].Port,
                config.HotspotSsid, config.HotspotPassword, config.EspDevices[1].Name);
            allSuccess &= success2;

            if (success2)
            {
                Log($"{config.EspDevices[1].Name} перезагружается. Ждем 20 секунд...", "INFO");
                Thread.Sleep(20000);
            }
        }
        else allSuccess = false;

        if (allSuccess)
        {
            Log("\nОбе ESP настроены. Ожидание подключения к домашней сети...", "SUCCESS");
            Thread.Sleep(10000);

            // Ищем обе ESP в домашней сети
            bool foundAny = false;
            foreach (var device in config.EspDevices)
            {
                if (FindAndSaveEspInNetwork(device, config))
                {
                    foundAny = true;
                }
            }

            if (foundAny)
            {
                SaveConfig(config);
                Log("\nНастройка завершена. Используйте меню 7 для параллельного стриминга.", "SUCCESS");
            }
            else
            {
                Log("\nESP не найдены в домашней сети. Проверьте:", "WARN");
                Console.WriteLine("1. ESP подключены к питанию");
                Console.WriteLine("2. Домашняя сеть работает");
                Console.WriteLine("3. ESP подключены к домашней сети");
                Console.WriteLine("4. Попробуйте найти ESP через меню 4");
            }
        }
        WaitForKey();
    }

    // ---------- 7. ПАРАЛЛЕЛЬНЫЙ СТРИМИНГ С ДВУХ ESP ----------
    static void StreamFromTwoEspParallel(Config config)
    {
        if (config.EspDevices.Count < 2)
        {
            Log("Требуется минимум 2 устройства.", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine("\n=== ПАРАЛЛЕЛЬНЫЙ СТРИМИНГ ЧЕРЕЗ ДОМАШНЮЮ СЕТЬ ===");

        // Проверяем IP в домашней сети
        bool hasIp1 = !string.IsNullOrEmpty(config.EspDevices[0].HotspotIp);
        bool hasIp2 = !string.IsNullOrEmpty(config.EspDevices[1].HotspotIp);

        if (!hasIp1 || !hasIp2)
        {
            Log("Для параллельного стриминга необходимо сначала найти ESP в домашней сети (меню 4).", "ERROR");
            WaitForKey();
            return;
        }

        // Проверяем доступность ESP через домашнюю сеть
        Log($"Проверяем доступность {config.EspDevices[0].Name}...", "INFO");
        if (!CheckEspAvailability(config.EspDevices[0].HotspotIp, config.EspDevices[0].Port, 3000))
        {
            Log($"{config.EspDevices[0].Name} недоступна по адресу {config.EspDevices[0].HotspotIp}", "ERROR");
            WaitForKey();
            return;
        }

        Log($"Проверяем доступность {config.EspDevices[1].Name}...", "INFO");
        if (!CheckEspAvailability(config.EspDevices[1].HotspotIp, config.EspDevices[1].Port, 3000))
        {
            Log($"{config.EspDevices[1].Name} недоступна по адресу {config.EspDevices[1].HotspotIp}", "ERROR");
            WaitForKey();
            return;
        }

        StopAllActiveStreams();

        var workers = new List<StreamWorker>();
        var streamTasks = new List<Task<bool>>();

        // Запускаем оба стрима через домашнюю сеть
        var worker1 = new StreamWorker(config.EspDevices[0], config.EspDevices[0].HotspotIp);
        workers.Add(worker1);
        streamTasks.Add(Task.Run(() => worker1.StartStreaming()));

        var worker2 = new StreamWorker(config.EspDevices[1], config.EspDevices[1].HotspotIp);
        workers.Add(worker2);
        streamTasks.Add(Task.Run(() => worker2.StartStreaming()));

        Thread.Sleep(2000);
        Log("\n=== ПАРАЛЛЕЛЬНЫЙ СТРИМИНГ АКТИВЕН ===", "SUCCESS");
        Log($"  СЛЕВА:  {config.EspDevices[0].Name} (косинус) - {config.EspDevices[0].HotspotIp}", "INFO");
        Log($"  СПРАВА: {config.EspDevices[1].Name} (синус) - {config.EspDevices[1].HotspotIp}", "INFO");
        Log("\nНажмите 'Q' чтобы остановить оба стрима...", "INFO");

        var displayTask = Task.Run(() => DisplayParallelStreams(workers));

        while (true)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
            {
                Log("\nОстанавливаю оба стрима...", "INFO");
                break;
            }
            Thread.Sleep(100);
        }

        StopAllActiveStreams();
        Log("Параллельный стриминг остановлен.", "SUCCESS");
        WaitForKey();
    }

    // ---------- 8. ОСТАНОВИТЬ ВСЕ АКТИВНЫЕ СТРИМЫ ----------
    static void StopAllStreamsMenu()
    {
        Console.Clear();
        Log("=== ОСТАНОВКА ВСЕХ АКТИВНЫХ СТРИМОВ ===", "INFO");
        StopAllActiveStreams();
        WaitForKey();
    }

    // ---------- 9. ПРОВЕРИТЬ ДОСТУПНОСТЬ ESP ----------
    static void CheckEspAvailabilityMenu(Config config)
    {
        Console.Clear();
        Log("=== ПРОВЕРКА ДОСТУПНОСТИ ESP ===", "INFO");

        if (config.EspDevices.Count == 0)
        {
            Log("Нет настроенных устройств.", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine("\nВыберите устройство для проверки:");
        for (int i = 0; i < config.EspDevices.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {config.EspDevices[i].Name} ({config.EspDevices[i].ApSsid})");
        }
        Console.WriteLine($"{config.EspDevices.Count + 1}. Все устройства");

        Console.Write($"\nВаш выбор (1-{config.EspDevices.Count + 1}): ");

        if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > config.EspDevices.Count + 1)
        {
            Log("Неверный выбор.", "ERROR");
            WaitForKey();
            return;
        }

        if (choice == config.EspDevices.Count + 1)
        {
            foreach (var device in config.EspDevices)
            {
                Console.WriteLine($"\nПроверка {device.Name}...");

                // Проверяем через домашнюю сеть
                if (!string.IsNullOrEmpty(device.HotspotIp))
                {
                    Console.Write($"  Домашняя сеть: {device.HotspotIp}");

                    bool isHotspotAvailable = CheckEspAvailability(device.HotspotIp, device.Port, 2000);
                    if (isHotspotAvailable)
                    {
                        Console.WriteLine($" - ✓ Доступна");
                        Log($"{device.Name} доступен через домашнюю сеть", "SUCCESS", device.Name);
                    }
                    else
                    {
                        Console.WriteLine($" - ✗ Недоступен");
                        Log($"{device.Name} не доступен через домашнюю сеть", "WARN", device.Name);
                    }
                }
                else
                {
                    Console.WriteLine($"  Домашняя сеть: ✗ IP не назначен");
                }

                // Проверяем AP режим
                bool isConnected = IsConnectedToNetwork(device.ApSsid);
                if (isConnected)
                {
                    Console.Write($"  AP режим: ✓ Подключен");

                    bool isAvailable = CheckEspAvailability(device.ApIp, device.Port, 2000);
                    if (isAvailable)
                    {
                        Console.WriteLine($", ESP: ✓ Отвечает");
                    }
                    else
                    {
                        Console.WriteLine($", ESP: ✗ Не отвечает");
                    }
                }
                else
                {
                    Console.WriteLine($"  AP режим: ✗ Не подключен");
                }
            }
        }
        else
        {
            var device = config.EspDevices[choice - 1];
            Console.WriteLine($"\nПроверка {device.Name}...");

            // Проверяем через домашнюю сеть
            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                Console.WriteLine($"\nПроверка через домашнюю сеть ({device.HotspotIp})...");
                bool isHotspotAvailable = CheckEspAvailability(device.HotspotIp, device.Port, 3000);
                if (isHotspotAvailable)
                {
                    Console.WriteLine($"  ✓ Доступен");

                    try
                    {
                        string status = SendCommandToEsp(device.HotspotIp, device.Port, "STATUS", 3000);
                        Console.WriteLine($"\nСтатус ESP:\n{status}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nНе удалось получить статус: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"  ✗ Недоступен");
                }
            }
            else
            {
                Console.WriteLine($"  ✗ IP в домашней сети не назначен");
            }
        }

        WaitForKey();
    }

    // ---------- 10. ДИАГНОСТИКА WIFI ----------
    static void DiagnoseWifi(Config config)
    {
        Console.Clear();
        Log("=== ДИАГНОСТИКА WIFI ПОДКЛЮЧЕНИЯ ===", "INFO");

        Console.WriteLine("\n1. Проверить текущее подключение");
        Console.WriteLine("2. Показать доступные сети");
        Console.WriteLine("3. Проверить ARP-таблицу (устройства в сети)");
        Console.WriteLine("4. Пинг ESP устройств");
        Console.WriteLine("5. Вернуться в меню");

        Console.Write("\nВаш выбор (1-5): ");
        string choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                CheckCurrentConnection();
                break;
            case "2":
                ShowAvailableNetworks();
                break;
            case "3":
                ShowArpTable();
                break;
            case "4":
                PingEspDevices(config);
                break;
            default:
                return;
        }

        WaitForKey();
    }

    static void CheckCurrentConnection()
    {
        Log("Текущее состояние WiFi:", "INFO");
        string result = RunNetshCommand("wlan show interfaces");
        Console.WriteLine("\n" + result);
    }

    static void ShowAvailableNetworks()
    {
        Log("Доступные WiFi сети:", "INFO");
        string result = RunNetshCommand("wlan show networks");
        Console.WriteLine("\n" + result);
    }

    static void ShowArpTable()
    {
        Log("ARP-таблица (устройства в сети):", "INFO");
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866)
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Console.WriteLine("\n" + output);

            // Парсим вывод для поиска ESP устройств
            string[] lines = output.Split('\n');
            bool foundEsp = false;

            foreach (string line in lines)
            {
                if (line.Contains("192.168.137.") || line.Contains("192.168.1.") || line.Contains("192.168.0."))
                {
                    // Ищем MAC-адреса ESP
                    if (line.Contains("c4:de:e2:19:2b:6c") || line.Contains("c4-de-e2-19-2b-6c") ||
                        line.Contains("cc:7b:5c:34:cc:f8") || line.Contains("cc-7b-5c-34-cc-f8"))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"НАЙДЕНА ESP: {line.Trim()}");
                        Console.ResetColor();
                        foundEsp = true;
                    }
                }
            }

            if (!foundEsp)
            {
                Log("ESP устройства не найдены в ARP-таблице", "WARN");
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка при получении ARP-таблицы: {ex.Message}", "ERROR");
        }
    }

    static void PingEspDevices(Config config)
    {
        Log("Пинг ESP устройств:", "INFO");

        foreach (var device in config.EspDevices)
        {
            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                Console.Write($"\nПинг {device.Name} ({device.HotspotIp}): ");

                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ping",
                            Arguments = $"-n 2 -w 1000 {device.HotspotIp}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (output.Contains("TTL=") || output.Contains("Превышен"))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓ Доступен");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("✗ Недоступен");
                        Console.ResetColor();
                    }
                }
                catch
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("✗ Ошибка пинга");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine($"\n{device.Name}: IP не назначен");
            }
        }
    }

    // ---------- 11. ПРЯМОЕ ПОДКЛЮЧЕНИЕ К ESP (ПО IP) ----------
    static void DirectConnectToEsp(Config config)
    {
        Console.Clear();
        Log("=== ПРЯМОЕ ПОДКЛЮЧЕНИЕ К ESP ПО IP ===", "INFO");

        Console.WriteLine("\nВведите IP-адрес ESP:");
        Console.Write("IP: ");
        string ip = Console.ReadLine();

        Console.Write("Порт (по умолчанию 8888): ");
        string portStr = Console.ReadLine();
        int port = string.IsNullOrEmpty(portStr) ? 8888 : int.Parse(portStr);

        if (string.IsNullOrEmpty(ip))
        {
            Log("IP-адрес не введен", "ERROR");
            WaitForKey();
            return;
        }

        // Проверяем доступность
        Log($"Проверка доступности {ip}:{port}...", "INFO");
        if (CheckEspAvailability(ip, port, 2000))
        {
            Log($"ESP доступна по адресу {ip}:{port}", "SUCCESS");

            // Пробуем подключиться и получить статус
            string status = SendCommandToEsp(ip, port, "STATUS", 3000);
            Console.WriteLine($"\nСтатус ESP:\n{status}");

            // Предлагаем начать стриминг
            Console.Write("\nНачать стриминг с этого устройства? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                // Создаем временную конфигурацию устройства
                var tempDevice = new EspDeviceConfig
                {
                    Name = "ESP_Direct",
                    ApSsid = "Direct_Connection",
                    ApPassword = "",
                    ApIp = ip,
                    Port = port,
                    HotspotIp = ip
                };

                StopAllActiveStreams();
                StartSingleStream(tempDevice, ip);
            }
        }
        else
        {
            Log($"ESP недоступна по адресу {ip}:{port}", "ERROR");
        }

        WaitForKey();
    }

    // ---------- 3. РЕДАКТИРОВАНИЕ КОНФИГУРАЦИИ ----------
    static void EditConfiguration(Config config)
    {
        Console.Clear();
        Console.WriteLine("=== РЕДАКТИРОВАНИЕ КОНФИГУРАЦИИ ===\n");

        Console.WriteLine("НАСТРОЙКИ ДОМАШНЕЙ СЕТИ:");
        Console.Write($"SSID домашней сети [{config.HotspotSsid}]: ");
        string input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.HotspotSsid = input;

        Console.Write($"Пароль домашней сети [***]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input)) config.HotspotPassword = input;

        Console.WriteLine($"\nРЕДАКТИРОВАНИЕ УСТРОЙСТВ (всего: {config.EspDevices.Count}):");

        for (int i = 0; i < config.EspDevices.Count; i++)
        {
            var device = config.EspDevices[i];
            Console.WriteLine($"\n--- Устройство {i + 1}: {device.Name} ---");

            Console.Write($"  Имя [{device.Name}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.Name = input;

            Console.Write($"  SSID сети (AP) [{device.ApSsid}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.ApSsid = input;

            Console.Write($"  Пароль сети [***]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.ApPassword = input;

            Console.Write($"  IP в AP режиме [{device.ApIp}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.ApIp = input;

            Console.Write($"  Порт [{device.Port}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int port))
                device.Port = port;

            Console.Write($"  MAC-адрес [{device.MacAddress}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.MacAddress = input;

            Console.Write($"  IP в домашней сети [{device.HotspotIp}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrEmpty(input)) device.HotspotIp = input;
        }

        SaveConfig(config);
        Log("Конфигурация сохранена", "SUCCESS");
        WaitForKey();
    }

    // ---------- 4. ПОИСК ESP В ДОМАШНЕЙ СЕТИ ----------
    static void FindEspInHotspotNetwork(Config config)
    {
        Console.Clear();
        Log("=== ПОИСК ESP В ДОМАШНЕЙ СЕТИ ===", "INFO");

        Console.WriteLine($"\nПоиск всех ESP в домашней сети {config.HotspotSsid}...");
        bool foundAny = false;

        foreach (var device in config.EspDevices)
        {
            if (FindAndSaveEspInNetwork(device, config))
            {
                foundAny = true;
            }
        }

        if (foundAny)
        {
            SaveConfig(config);
            Log($"\nПоиск завершен. Конфигурация сохранена.", "SUCCESS");
        }
        else
        {
            Log($"\nESP не найдены. Проверьте:", "WARN");
            Console.WriteLine("1. ESP подключены к питанию");
            Console.WriteLine("2. ESP настроены на подключение к домашней сети (меню 1 или 6)");
            Console.WriteLine("3. ESP подключены к сети " + config.HotspotSsid);
            Console.WriteLine("4. Попробуйте перезагрузить ESP кнопкой RESET");
            Console.WriteLine("\nАльтернативные действия:");
            Console.WriteLine("- Используйте меню 11 для прямого подключения по IP");
            Console.WriteLine("- Назначьте IP-адреса вручную через меню 3");
        }
        WaitForKey();
    }

    // Новый метод для поиска и сохранения ESP в домашней сети
    static bool FindAndSaveEspInNetwork(EspDeviceConfig device, Config config)
    {
        Log($"Поиск {device.Name} в сети...", "INFO", device.Name);

        // СПИСОК ВОЗМОЖНЫХ ПОДСЕТЕЙ ДЛЯ ПОИСКА
        List<string> possibleSubnets = new List<string>
    {
        "192.168.137.",  // Подсеть ваших ESP32
        "192.168.1.",    // Типичная домашняя подсеть
        "192.168.0.",    // Другая типичная подсеть
        "172.17.160.",   // Текущая подсеть компьютера
        "10.0.0.",       // Еще одна возможная подсеть
    };

        // 1. Сначала проверяем сохраненный IP (если есть)
        if (!string.IsNullOrEmpty(device.HotspotIp))
        {
            Log($"Проверяю сохраненный IP: {device.HotspotIp}", "INFO", device.Name);
            if (CheckEspAvailability(device.HotspotIp, device.Port, 500))
            {
                Log($"Устройство {device.Name} доступно по сохраненному IP: {device.HotspotIp}", "SUCCESS", device.Name);
                return true;
            }
            else
            {
                Log($"Сохраненный IP {device.HotspotIp} не доступен", "WARN", device.Name);
            }
        }

        // 2. Поиск по MAC-адресу через ARP
        if (!string.IsNullOrEmpty(device.MacAddress))
        {
            string arpIp = FindIpByMacAddress(device.MacAddress);
            if (!string.IsNullOrEmpty(arpIp))
            {
                Log($"Найден IP по MAC-адресу: {arpIp}", "INFO", device.Name);
                if (CheckEspAvailability(arpIp, device.Port, 500))
                {
                    device.HotspotIp = arpIp;
                    Log($"Устройство {device.Name} найдено по MAC-адресу: {arpIp}", "SUCCESS", device.Name);
                    return true;
                }
            }
        }

        // 3. Активное сканирование всех возможных подсетей
        string foundIp = null;

        foreach (string subnet in possibleSubnets)
        {
            Log($"Сканирую подсеть {subnet}...", "INFO", device.Name);

            // Быстрое сканирование только нескольких адресов
            Parallel.For(1, 255, (i, state) =>
            {
                // Проверяем только "интересные" адреса
                if (i == 102 || i == 173 || i == 1 || i == 100 || i == 50 || i == 200 || i < 10)
                {
                    string testIp = subnet + i;
                    if (CheckEspAvailability(testIp, device.Port, 100)) // Уменьшаем таймаут для скорости
                    {
                        foundIp = testIp;
                        state.Break();
                    }
                }
            });

            if (!string.IsNullOrEmpty(foundIp))
            {
                break;
            }
        }

        // 4. Если не нашли быстрым способом, делаем полное сканирование
        if (string.IsNullOrEmpty(foundIp))
        {
            Log($"Делаю полное сканирование подсети 192.168.137....", "INFO", device.Name);

            // Специально проверяем ваши известные IP
            string[] knownIps = { "192.168.137.102", "192.168.137.173", "192.168.137.1", "192.168.137.100" };

            Parallel.ForEach(knownIps, (testIp, state) =>
            {
                if (CheckEspAvailability(testIp, device.Port, 500))
                {
                    foundIp = testIp;
                    state.Break();
                }
            });
        }

        if (!string.IsNullOrEmpty(foundIp))
        {
            device.HotspotIp = foundIp;
            Log($"Найдена {device.Name}: {foundIp}", "SUCCESS", device.Name);
            return true;
        }

        Log($"{device.Name} не найдена в сети", "WARN", device.Name);

        // Предлагаем ввести IP вручную
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n{device.Name} не найдена автоматически.");
        Console.WriteLine($"Известные IP ваших устройств:");
        Console.WriteLine($"  ESP32_Cos: 192.168.137.102");
        Console.WriteLine($"  ESP32_Sin: 192.168.137.173");
        Console.ResetColor();

        Console.Write($"\nВведите IP-адрес для {device.Name} вручную (или Enter чтобы пропустить): ");
        string manualIp = Console.ReadLine();

        if (!string.IsNullOrEmpty(manualIp))
        {
            if (CheckEspAvailability(manualIp, device.Port, 1000))
            {
                device.HotspotIp = manualIp;
                Log($"IP {manualIp} назначен вручную для {device.Name}", "SUCCESS", device.Name);
                return true;
            }
            else
            {
                Log($"IP {manualIp} не доступен", "ERROR", device.Name);
            }
        }

        return false;
    }

    // ---------- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ----------

    static string FindIpByMacAddress(string macAddress)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866)
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string[] lines = output.Split('\n');
            foreach (string line in lines)
            {
                // Нормализуем MAC-адрес для сравнения
                string normalizedMac = macAddress.Replace(':', '-').ToLower();

                if (line.ToLower().Contains(normalizedMac))
                {
                    // Извлекаем IP из строки ARP-таблицы
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1)
                    {
                        return parts[0];
                    }
                }
            }
        }
        catch { }

        return null;
    }

    // ---------- 5. ПОКАЗАТЬ КОНФИГУРАЦИЮ ----------
    static void ShowCurrentConfig(Config config)
    {
        Console.Clear();
        Console.WriteLine("=== ТЕКУЩАЯ КОНФИГУРАЦИЯ ===");
        Console.WriteLine($"\nДомашняя сеть:");
        Console.WriteLine($"  SSID: {config.HotspotSsid}");
        Console.WriteLine($"  Пароль: {new string('*', config.HotspotPassword.Length)}");

        Console.WriteLine($"\nУстройств: {config.EspDevices.Count}");
        foreach (var device in config.EspDevices)
        {
            Console.WriteLine($"\n--- {device.Name} ---");
            Console.WriteLine($"  Сеть AP: {device.ApSsid}");
            Console.WriteLine($"  Пароль AP: {new string('*', device.ApPassword.Length)}");
            Console.WriteLine($"  IP (AP): {device.ApIp}:{device.Port}");
            Console.WriteLine($"  MAC-адрес: {device.MacAddress}");
            Console.WriteLine($"  IP (домашняя сеть): {device.HotspotIp ?? "не найден"}");

            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                Console.Write($"  Статус: ");
                if (CheckEspAvailability(device.HotspotIp, device.Port, 500))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Доступен");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("✗ Недоступен");
                    Console.ResetColor();
                }
            }
        }

        Console.WriteLine($"\nАктивных стримов: {_activeWorkers.Count}");
        if (_activeWorkers.Count > 0)
        {
            Console.WriteLine("  Активные устройства:");
            foreach (var worker in _activeWorkers)
            {
                Console.WriteLine($"    - {worker.Device.Name} ({worker.Ip})");
            }
        }

        Console.WriteLine($"\nФайл: {Path.GetFullPath(ConfigFile)}");
        WaitForKey();
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
        Console.WriteLine("=== СПРАВКА ПО КОМАНДАМ ESP32 ===");
        Console.ResetColor();
        Console.WriteLine("\nПоддерживаемые команды:");
        Console.WriteLine("  SET - настройка WiFi");
        Console.WriteLine("  STATUS - информация об устройстве");
        Console.WriteLine("  START_STREAM - начать стриминг");
        Console.WriteLine("  STOP_STREAM - остановить стриминг");
        Console.WriteLine("  CLEAR - очистить настройки WiFi");
        Console.WriteLine("\nВажная информация:");
        Console.WriteLine("  • ESP принимает только ОДНО TCP-соединение за раз");
        Console.WriteLine("  • Если ESP уже стримит, она не ответит на другие команды");
        Console.WriteLine("  • Перед любой операцией останавливайте активные стримы (меню 8)");
        Console.WriteLine("  • Если ESP не отвечает, перезагрузите ее кнопкой RESET");
        Console.WriteLine("\nРАБОТА ЧЕРЕЗ ДОМАШНЮЮ СЕТЬ:");
        Console.WriteLine("  1. Компьютер должен быть подключен к домашней сети");
        Console.WriteLine("  2. Настройте ESP на подключение к домашней сети (меню 1 или 6)");
        Console.WriteLine("  3. Найдите ESP в домашней сети (меню 4)");
        Console.WriteLine("  4. Запускайте параллельный стриминг (меню 7)");
        Console.WriteLine("\nВАЖНО:");
        Console.WriteLine("  • В конфиг уже добавлены IP-адреса ваших ESP:");
        Console.WriteLine("    - ESP32_Cos: 192.168.137.102");
        Console.WriteLine("    - ESP32_Sin: 192.168.137.173");
        Console.WriteLine("  • Для прямого подключения используйте меню 11");
        WaitForKey();
    }

    // ---------- ГЛАВНЫЙ ЦИКЛ ----------
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.Title = "ESP32 Dual Stream Manager - Работа через домашнюю сеть";
        Console.OutputEncoding = Encoding.UTF8;

        Config config = LoadConfig();

        // Проверяем доступность ESP при старте
        Log("=== ПРОВЕРКА ДОСТУПНОСТИ ESP ПРИ СТАРТЕ ===", "INFO");
        foreach (var device in config.EspDevices)
        {
            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                bool isAvailable = CheckEspAvailability(device.HotspotIp, device.Port, 1000);
                Log($"{device.Name} ({device.HotspotIp}): {(isAvailable ? "✓ Доступна" : "✗ Недоступна")}",
                    isAvailable ? "SUCCESS" : "WARN");
            }
        }
        Thread.Sleep(2000);

        bool running = true;
        while (running)
        {
            Console.Clear();
            ShowHeader(config);
            string choice = GetMenuChoice();

            switch (choice)
            {
                case "1": ConfigureSingleEsp(config); break;
                case "2": StreamFromSingleEsp(config); break;
                case "3": EditConfiguration(config); break;
                case "4": FindEspInHotspotNetwork(config); break;
                case "5": ShowCurrentConfig(config); break;
                case "6": ConfigureTwoEspParallel(config); break;
                case "7": StreamFromTwoEspParallel(config); break;
                case "8": StopAllStreamsMenu(); break;
                case "9": CheckEspAvailabilityMenu(config); break;
                case "10": DiagnoseWifi(config); break;
                case "11": DirectConnectToEsp(config); break;
                case "12":
                    StopAllActiveStreams();
                    running = false;
                    Log("Выход...", "INFO");
                    break;
                case "h": ShowHelp(); break;
                default: Log("Неверный выбор. Нажмите 'h' для помощи.", "WARN"); WaitForKey(); break;
            }
        }
    }
}