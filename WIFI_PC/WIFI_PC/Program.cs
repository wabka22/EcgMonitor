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
using System.Text.RegularExpressions;
using System.Text;

class Program
{
    const string ConfigFile = "config.json";

    // Класс для конфигурации
    class Config
    {
        public string PcWifiSsid { get; set; } = "";
        public string PcWifiPassword { get; set; } = "";
        public string EspNetworkName { get; set; } = "ESP32_Cos_Streamer";
        public string EspNetworkPassword { get; set; } = "12345678";
        public string EspApIp { get; set; } = "192.168.4.1";
        public int EspPort { get; set; } = 8888;
        public string? EspHomeIp { get; set; }
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

    // ---------- ПРОВЕРКА ПОДКЛЮЧЕНИЯ К СЕТИ ESP ----------
    static bool IsConnectedToEspNetwork(string espNetworkName)
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
                    StandardOutputEncoding = Encoding.GetEncoding(866) // Кодировка OEM Russian
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Ищем SSID в выводе
            if (output.Contains(espNetworkName))
            {
                // Проверяем состояние подключения (русский и английский)
                string[] connectedMarkers = { "подключено", "Connected" };
                foreach (string marker in connectedMarkers)
                {
                    int idx = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        // Ищем SSID рядом с маркером подключения
                        string context = output.Substring(Math.Max(0, idx - 200),
                                                         Math.Min(400, output.Length - Math.Max(0, idx - 200)));
                        if (context.Contains(espNetworkName))
                        {
                            Log($"Подключен к сети '{espNetworkName}'", "SUCCESS");
                            return true;
                        }
                    }
                }
            }

            Log($"Не подключен к сети '{espNetworkName}'", "WARN");
            return false;
        }
        catch (Exception e)
        {
            Log($"Ошибка при проверке подключения: {e.Message}", "ERROR");
            return false;
        }
    }

    // ---------- ПОДКЛЮЧЕНИЕ К СЕТИ ESP ЧЕРЕЗ netsh ----------
    static bool ConnectToEspNetworkViaNetsh(string networkName, string password)
    {
        Log($"Подключение к сети {networkName} через netsh...", "INFO");

        try
        {
            // Сначала удаляем старый профиль, если есть
            RunNetshCommand($"wlan delete profile name=\"{networkName}\"", false);

            // Создаем новый профиль
            string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{networkName}</name>
    <SSIDConfig>
        <SSID>
            <name>{networkName}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
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
            string tempFile = Path.Combine(Path.GetTempPath(), $"wifi_{Guid.NewGuid()}.xml");
            File.WriteAllText(tempFile, profileXml, Encoding.UTF8);

            try
            {
                // Добавляем профиль
                string addResult = RunNetshCommand($"wlan add profile filename=\"{tempFile}\"");

                if (addResult.Contains("added") || addResult.Contains("успешно") ||
                    addResult.Contains("добавлен") || addResult.Contains("completed"))
                {
                    Log("Профиль WiFi успешно добавлен", "SUCCESS");

                    // Подключаемся к сети
                    string connectResult = RunNetshCommand($"wlan connect name=\"{networkName}\"");

                    if (connectResult.Contains("completed") || connectResult.Contains("успешно") ||
                        connectResult.Contains("connected") || connectResult.Contains("подключен"))
                    {
                        Log($"Успешно инициировано подключение к {networkName}", "SUCCESS");

                        // Ждем подключения
                        for (int i = 0; i < 15; i++)
                        {
                            Thread.Sleep(1000);
                            if (IsConnectedToEspNetwork(networkName))
                            {
                                Log($"Подключение к {networkName} установлено!", "SUCCESS");
                                return true;
                            }
                            Console.Write(".");
                        }
                        Console.WriteLine();
                    }
                    else
                    {
                        Log($"Не удалось подключиться: {connectResult}", "ERROR");
                    }
                }
                else
                {
                    Log($"Не удалось добавить профиль: {addResult}", "ERROR");
                }
            }
            finally
            {
                // Удаляем временный файл
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка при подключении: {e.Message}", "ERROR");
        }

        return false;
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

    // ---------- ПОДКЛЮЧЕНИЕ К СЕТИ ESP (АЛЬТЕРНАТИВНЫЙ МЕТОД) ----------
    static bool ConnectToEspNetworkAlternative(string networkName, string password)
    {
        Log($"Попытка альтернативного подключения к {networkName}...", "INFO");

        try
        {
            // Пробуем использовать PowerShell для подключения
            string psCommand = $@"
$profileXml = @'
<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{networkName}</name>
    <SSIDConfig>
        <SSID>
            <name>{networkName}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
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
</WLANProfile>
'@

$profileXml | Out-File -FilePath $env:TEMP\wifi_profile.xml -Encoding UTF8
netsh wlan add profile filename=""$env:TEMP\wifi_profile.xml""
netsh wlan connect name=""{networkName}""
Remove-Item -Path $env:TEMP\wifi_profile.xml -Force
";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand.Replace("\"", "\\\"")}\"",
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

            Log($"PowerShell output: {output}", "INFO");
            if (!string.IsNullOrEmpty(error))
            {
                Log($"PowerShell error: {error}", "WARN");
            }

            // Ждем подключения
            Thread.Sleep(3000);
            return IsConnectedToEspNetwork(networkName);
        }
        catch (Exception e)
        {
            Log($"Ошибка альтернативного подключения: {e.Message}", "ERROR");
            return false;
        }
    }

    // ---------- ПРОСТОЕ СООБЩЕНИЕ О РУЧНОМ ПОДКЛЮЧЕНИИ ----------
    static void ShowManualConnectionInstructions(string networkName, string password)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║               РУЧНОЕ ПОДКЛЮЧЕНИЕ К ESP                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine("\nПОШАГОВАЯ ИНСТРУКЦИЯ:");
        Console.WriteLine("\n1. Нажмите на иконку WiFi в правом нижнем углу экрана");
        Console.WriteLine("   (рядом с часами)");

        Console.WriteLine("\n2. В списке сетей найдите:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"   ▸ {networkName}");
        Console.ResetColor();

        Console.WriteLine("\n3. Нажмите на эту сеть и выберите 'Подключиться'");

        Console.WriteLine("\n4. При запросе пароля введите:");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   ▸ {password}");
        Console.ResetColor();

        Console.WriteLine("\n5. Дождитесь сообщения 'Подключено'");

        Console.WriteLine("\n6. Вернитесь в это окно и нажмите Enter");

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n" + new string('═', 60));
        Console.ResetColor();

        Console.WriteLine("\nПосле подключения WiFi вы сможете:");
        Console.WriteLine("  • Настроить ESP на свою домашнюю сеть");
        Console.WriteLine("  • Начать стриминг данных");
        Console.WriteLine("  • Управлять ESP через программу");

        Console.WriteLine("\n" + new string('═', 60));
        Console.Write("\nНажмите Enter, когда подключитесь к WiFi ESP...");
        Console.ReadLine();

        // Проверяем подключение
        if (IsConnectedToEspNetwork(networkName))
        {
            Log($"Успешно подключено к {networkName}!", "SUCCESS");
        }
        else
        {
            Log("Похоже, вы еще не подключились", "WARN");
            Console.Write("Повторить инструкцию? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                ShowManualConnectionInstructions(networkName, password);
            }
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

                if (!success || !client.Connected)
                {
                    return false;
                }

                client.EndConnect(result);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.WriteTimeout = timeout;
                    stream.ReadTimeout = timeout;

                    // Отправляем команду STATUS (правильную команду)
                    byte[] ping = Encoding.UTF8.GetBytes("STATUS\n");
                    stream.Write(ping, 0, ping.Length);

                    // Читаем ответ
                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    return response.Contains("ESP") ||
                           response.Contains("AP IP") ||
                           response.Contains("Ready") ||
                           response.Contains("OK");
                }
            }
        }
        catch
        {
            return false;
        }
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

                if (!success || !client.Connected)
                {
                    return "ERROR: Connection failed";
                }

                client.EndConnect(result);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.WriteTimeout = timeout;
                    stream.ReadTimeout = timeout;

                    // Отправляем команду
                    byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    // Читаем ответ
                    byte[] buffer = new byte[4096];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
        }
        catch (Exception e)
        {
            return $"ERROR: {e.Message}";
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
                Config defaultConfig = new Config
                {
                    PcWifiSsid = "MyHomeWiFi",
                    PcWifiPassword = "mypassword122",
                    EspNetworkName = "ESP32_Cos_Streamer",
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

    // ---------- ГЛАВНАЯ ФУНКЦИЯ ДЛЯ РАБОТЫ С ESP ----------
    static string EnsureConnectedToEsp(Config config, string operationName = "операции")
    {
        Console.Clear();
        Log($"=== {operationName.ToUpper()} ===", "INFO");

        // Проверяем текущее подключение
        if (IsConnectedToEspNetwork(config.EspNetworkName))
        {
            // Проверяем доступность ESP
            if (CheckEspAvailability(config.EspApIp, config.EspPort, 2000))
            {
                Log($"ESP доступна по адресу {config.EspApIp}", "SUCCESS");
                return config.EspApIp;
            }
            else
            {
                Log($"ESP не отвечает на {config.EspApIp}", "WARN");
            }
        }

        // Если не подключены - предлагаем варианты
        Console.WriteLine("\nДля работы с ESP необходимо подключиться к её WiFi сети.");
        Console.WriteLine($"\nСеть ESP: {config.EspNetworkName}");
        Console.WriteLine($"Пароль: {config.EspNetworkPassword}");

        Console.WriteLine("\nВыберите способ подключения:");
        Console.WriteLine("1. Автоматическое подключение (попробовать программе подключиться)");
        Console.WriteLine("2. Показать инструкцию для ручного подключения");
        Console.WriteLine("3. Отмена");

        Console.Write("\nВаш выбор (1-3): ");
        string choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                Log("Попытка автоматического подключения...", "INFO");

                // Пробуем разные методы
                if (ConnectToEspNetworkViaNetsh(config.EspNetworkName, config.EspNetworkPassword) ||
                    ConnectToEspNetworkAlternative(config.EspNetworkName, config.EspNetworkPassword))
                {
                    // Проверяем ESP
                    if (CheckEspAvailability(config.EspApIp, config.EspPort, 3000))
                    {
                        Log($"ESP доступна по адресу {config.EspApIp}", "SUCCESS");
                        return config.EspApIp;
                    }
                    else
                    {
                        Log("Подключение к WiFi есть, но ESP не отвечает", "ERROR");
                    }
                }
                else
                {
                    Log("Не удалось подключиться автоматически", "ERROR");
                    ShowManualConnectionInstructions(config.EspNetworkName, config.EspNetworkPassword);
                }
                break;

            case "2":
                ShowManualConnectionInstructions(config.EspNetworkName, config.EspNetworkPassword);

                // Проверяем после инструкции
                if (IsConnectedToEspNetwork(config.EspNetworkName))
                {
                    if (CheckEspAvailability(config.EspApIp, config.EspPort, 3000))
                    {
                        Log($"ESP доступна по адресу {config.EspApIp}", "SUCCESS");
                        return config.EspApIp;
                    }
                }
                break;

            case "3":
                Log("Операция отменена", "INFO");
                return null;
        }

        // Если дошли сюда - не удалось подключиться
        Log("Не удалось подключиться к ESP", "ERROR");
        Console.WriteLine("\nВозможные проблемы:");
        Console.WriteLine("1. ESP не включена или не раздает WiFi");
        Console.WriteLine($"2. Неправильное имя сети (должно быть: {config.EspNetworkName})");
        Console.WriteLine($"3. Неправильный пароль (должен быть: {config.EspNetworkPassword})");
        Console.WriteLine("4. WiFi адаптер отключен на ПК");

        WaitForKey("\nНажмите любую клавишу для возврата...");
        return null;
    }

    // ---------- ОТПРАВКА ДАННЫХ НА ESP ----------
    static bool SendWifiCredentialsToEsp(string espIp, int espPort, string ssid, string pass)
    {
        Log($"Отправка данных WiFi на ESP ({espIp}:{espPort})...", "INFO");

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 5000;

                client.Connect(espIp, espPort);

                using (NetworkStream stream = client.GetStream())
                {
                    // ПРАВИЛЬНАЯ команда для ESP кода
                    string data = $"SET\n{ssid}\n{pass}\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    stream.Write(bytes, 0, bytes.Length);

                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (response.Contains("OK") || response.Contains("Success") ||
                        response.Contains("saved") || response.Contains("сохранено"))
                    {
                        Log($"ESP ответила: {response.Trim()}", "SUCCESS");
                        return true;
                    }
                    else
                    {
                        Log($"ESP вернула: {response.Trim()}", "WARN");
                        return false;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка: {e.Message}", "ERROR");
            return false;
        }
    }

    // ---------- СТРИМИНГ ДАННЫХ С ESP ----------
    static void StreamDataFromEsp(Config config)
    {
        string espIp = EnsureConnectedToEsp(config, "СТРИМИНГ ДАННЫХ");

        if (string.IsNullOrEmpty(espIp))
        {
            return;
        }

        // Запускаем стриминг
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
                    // ПРАВИЛЬНАЯ команда для ESP кода - START_STREAM вместо START
                    byte[] cmdBytes = Encoding.UTF8.GetBytes("START_STREAM\n");
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    string response = reader.ReadLine();
                    if (response?.Contains("OK") == true || response?.Contains("started") == true ||
                        response?.Contains("Stream") == true)
                    {
                        Log("Стриминг начат. Нажмите 'Q' для остановки...", "SUCCESS");
                    }
                    else
                    {
                        Log($"Ответ от ESP: {response}", "WARN");
                    }

                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        Log("Прерывание стриминга...", "INFO");
                    };

                    DateTime lastDataTime = DateTime.Now;

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
                                lastDataTime = DateTime.Now;
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {data}");
                            }
                        }
                        else
                        {
                            // Если долго нет данных, проверяем соединение
                            if ((DateTime.Now - lastDataTime).TotalSeconds > 5)
                            {
                                // Отправляем ping (правильная команда PING)
                                try
                                {
                                    byte[] ping = Encoding.UTF8.GetBytes("PING\n");
                                    stream.Write(ping, 0, ping.Length);
                                    lastDataTime = DateTime.Now;

                                    // Читаем ответ на пинг
                                    if (stream.DataAvailable)
                                    {
                                        string pingResponse = reader.ReadLine();
                                        if (!string.IsNullOrEmpty(pingResponse))
                                        {
                                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] PING: {pingResponse}");
                                        }
                                    }
                                }
                                catch
                                {
                                    Log("Потеряно соединение с ESP", "ERROR");
                                    break;
                                }
                            }
                        }

                        Thread.Sleep(10);
                    }

                    // Отправляем команду остановки (правильная команда STOP_STREAM)
                    try
                    {
                        byte[] stopBytes = Encoding.UTF8.GetBytes("STOP_STREAM\n");
                        stream.Write(stopBytes, 0, stopBytes.Length);
                        Log("Стриминг остановлен.", "INFO");
                    }
                    catch
                    {
                        Log("Соединение разорвано.", "INFO");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка при стриминге: {e.Message}", "ERROR");
        }

        WaitForKey("Нажмите любую клавишу для возврата в меню...");
    }

    // ---------- НАСТРОЙКА ESP ----------
    static void ConfigureEsp(Config config)
    {
        string espIp = EnsureConnectedToEsp(config, "НАСТРОЙКА ESP");

        if (string.IsNullOrEmpty(espIp))
        {
            return;
        }

        Console.WriteLine("\n=== НАСТРОЙКА ДОМАШНЕЙ WIFI ДЛЯ ESP ===");
        Console.WriteLine($"\nТекущие данные домашней WiFi:");
        Console.WriteLine($"SSID: {config.PcWifiSsid}");
        Console.WriteLine($"Пароль: {new string('*', config.PcWifiPassword.Length)}");

        Console.Write("\nИспользовать эти данные? (y/n): ");
        if (Console.ReadLine()?.ToLower() != "y")
        {
            Console.Write("Введите SSID вашей домашней WiFi: ");
            config.PcWifiSsid = Console.ReadLine();

            Console.Write("Введите пароль: ");
            config.PcWifiPassword = Console.ReadLine();

            SaveConfig(config);
        }

        // Отправляем данные WiFi
        bool success = SendWifiCredentialsToEsp(
            espIp,
            config.EspPort,
            config.PcWifiSsid,
            config.PcWifiPassword
        );

        if (success)
        {
            Log("\nДанные WiFi отправлены успешно!", "SUCCESS");
            Log("ESP перезагрузится и попытается подключиться к вашей домашней сети.", "INFO");

            Console.WriteLine("\nДальнейшие действия:");
            Console.WriteLine("1. ESP перезагрузится (~10 секунд)");
            Console.WriteLine($"2. Подключитесь к своей домашней WiFi: {config.PcWifiSsid}");
            Console.WriteLine("3. Используйте пункт 'Проверить подключение' для поиска ESP");

            for (int i = 10; i > 0; i--)
            {
                Console.Write($"\rESP перезагружается... {i} секунд ");
                Thread.Sleep(1000);
            }
            Console.WriteLine();

            Log("Теперь подключитесь к своей домашней WiFi", "INFO");
            Console.WriteLine("После подключения используйте пункт 'Проверить подключение'");
            Console.WriteLine("в главном меню для поиска ESP в вашей сети.");
        }
        else
        {
            Log("Не удалось настроить ESP", "ERROR");
        }

        WaitForKey();
    }

    // ---------- ПОИСК ESP В ДОМАШНЕЙ СЕТИ ----------
    static void FindEspInHomeNetwork(Config config)
    {
        Console.Clear();
        Log("=== ПОИСК ESP В ДОМАШНЕЙ СЕТИ ===", "INFO");

        if (string.IsNullOrEmpty(config.PcWifiSsid))
        {
            Log("Сначала настройте данные домашней WiFi", "ERROR");
            WaitForKey();
            return;
        }

        Console.WriteLine($"\nУбедитесь, что вы подключены к домашней WiFi:");
        Console.WriteLine($"Сеть: {config.PcWifiSsid}");
        Console.Write("\nВы подключены к домашней WiFi? (y/n): ");

        if (Console.ReadLine()?.ToLower() != "y")
        {
            Log("Подключитесь к домашней WiFi и попробуйте снова", "WARN");
            WaitForKey();
            return;
        }

        Log("Начинаю поиск ESP в сети...", "INFO");

        // Простой поиск в распространенных подсетях
        string[] commonSubnets = { "192.168.1.", "192.168.0.", "192.168.100.", "192.168.137." };
        string foundIp = null;

        foreach (string subnet in commonSubnets)
        {
            Console.Write($"\nПроверка подсети {subnet}*: ");

            // Проверяем диапазон 2-30
            for (int i = 2; i <= 30; i++)
            {
                string testIp = subnet + i;
                Console.Write($"\rПроверка подсети {subnet}*: {testIp} ");

                if (CheckEspAvailability(testIp, config.EspPort, 150))
                {
                    foundIp = testIp;
                    Console.WriteLine();
                    Log($"Найдена ESP: {foundIp}", "SUCCESS");

                    // Сохраняем IP
                    config.EspHomeIp = foundIp;
                    SaveConfig(config);
                    Log("IP сохранен в конфигурации", "INFO");

                    // Тестируем подключение
                    Console.WriteLine("\nТестирование подключения...");
                    TestEspConnection(foundIp, config.EspPort);

                    WaitForKey();
                    return;
                }
            }
            Console.WriteLine();
        }

        if (foundIp == null)
        {
            Log("ESP не найдена в домашней сети", "ERROR");
            Console.WriteLine("\nВозможные причины:");
            Console.WriteLine("1. ESP не подключена к домашней WiFi");
            Console.WriteLine("2. ESP имеет другой IP-адрес");
            Console.WriteLine("3. ESP не включена");
            Console.WriteLine("4. Брандмауэр блокирует подключение");

            Console.Write("\nХотите ввести IP вручную? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                Console.Write("Введите IP-адрес ESP: ");
                string manualIp = Console.ReadLine();

                if (CheckEspAvailability(manualIp, config.EspPort, 2000))
                {
                    config.EspHomeIp = manualIp;
                    SaveConfig(config);
                    Log($"ESP найдена по адресу: {manualIp}", "SUCCESS");
                }
                else
                {
                    Log("ESP недоступна по этому адресу", "ERROR");
                }
            }
        }

        WaitForKey();
    }

    static void TestEspConnection(string ip, int port)
    {
        try
        {
            string response = SendCommandToEsp(ip, port, "STATUS");
            Console.WriteLine("\nОтвет от ESP:");
            Console.WriteLine(response);
        }
        catch (Exception e)
        {
            Log($"Ошибка тестирования: {e.Message}", "ERROR");
        }
    }

    // ---------- ОСНОВНОЙ ЦИКЛ ----------
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.Title = "ESP32 Data Streamer - Direct WiFi Connection";
        Console.OutputEncoding = Encoding.UTF8;

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
                    FindEspInHomeNetwork(config);
                    break;

                case "5":
                    ShowCurrentConfig(config);
                    break;

                case "6":
                    running = false;
                    Log("Выход из программы...", "INFO");
                    break;

                case "7":
                    TestAllCommands(config);
                    break;

                case "h":
                    ShowHelp();
                    break;

                default:
                    Log("Неверный выбор. Нажмите 'h' для помощи.", "WARN");
                    WaitForKey();
                    break;
            }
        }
    }

    // ---------- ТЕСТИРОВАНИЕ ВСЕХ КОМАНД ----------
    static void TestAllCommands(Config config)
    {
        Console.Clear();
        Log("=== ТЕСТИРОВАНИЕ КОМАНД ESP ===", "INFO");

        string espIp = EnsureConnectedToEsp(config, "ТЕСТИРОВАНИЕ КОМАНД");

        if (string.IsNullOrEmpty(espIp))
        {
            return;
        }

        Console.WriteLine("\nТестирование команд:");
        Console.WriteLine("1. STATUS");
        Console.WriteLine("2. CLEAR");
        Console.WriteLine("3. SET (отправить тестовые данные)");
        Console.WriteLine("4. START_STREAM (краткий тест)");
        Console.WriteLine("5. STOP_STREAM");

        Console.Write("\nВыберите команду для тестирования (1-5, или Enter для всех): ");
        string choice = Console.ReadLine();

        if (string.IsNullOrEmpty(choice))
        {
            // Тестируем все команды
            TestCommand(espIp, config.EspPort, "STATUS");
            Thread.Sleep(1000);
            TestCommand(espIp, config.EspPort, "CLEAR");
            Thread.Sleep(1000);
            TestCommand(espIp, config.EspPort, $"SET\nTestWiFi\nTestPassword123\n");
            Thread.Sleep(1000);
            TestStream(espIp, config.EspPort);
        }
        else
        {
            switch (choice)
            {
                case "1":
                    TestCommand(espIp, config.EspPort, "STATUS");
                    break;
                case "2":
                    TestCommand(espIp, config.EspPort, "CLEAR");
                    break;
                case "3":
                    TestCommand(espIp, config.EspPort, $"SET\nTestWiFi\nTestPassword123\n");
                    break;
                case "4":
                    TestStream(espIp, config.EspPort);
                    break;
                case "5":
                    TestCommand(espIp, config.EspPort, "STOP_STREAM");
                    break;
            }
        }

        WaitForKey();
    }

    static void TestCommand(string ip, int port, string command)
    {
        Log($"Отправка команды: {command.Split('\n')[0]}", "INFO");
        string response = SendCommandToEsp(ip, port, command);
        Console.WriteLine($"Ответ: {response}");
    }

    static void TestStream(string ip, int port)
    {
        Log("Тестирование стриминга...", "INFO");
        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(ip, port);
                using (NetworkStream stream = client.GetStream())
                {
                    // Запускаем стриминг
                    byte[] startCmd = Encoding.UTF8.GetBytes("START_STREAM\n");
                    stream.Write(startCmd, 0, startCmd.Length);

                    // Ждем ответ
                    Thread.Sleep(500);

                    // Читаем несколько значений
                    for (int i = 0; i < 5; i++)
                    {
                        if (stream.DataAvailable)
                        {
                            byte[] buffer = new byte[1024];
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            Console.WriteLine($"Данные {i + 1}: {data}");
                        }
                        Thread.Sleep(100);
                    }

                    // Останавливаем стриминг
                    byte[] stopCmd = Encoding.UTF8.GetBytes("STOP_STREAM\n");
                    stream.Write(stopCmd, 0, stopCmd.Length);
                    Log("Тест стриминга завершен", "SUCCESS");
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка тестирования стриминга: {e.Message}", "ERROR");
        }
    }

    // ---------- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ----------
    static void ShowHeader(Config config)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║       ESP32 Data Streamer - WiFi Manager        ║");
        Console.WriteLine("║         (Обновлено для ESP32 кода)              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"\nТекущее состояние:");

        // Показываем состояние подключения
        if (IsConnectedToEspNetwork(config.EspNetworkName))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Подключен к сети ESP: {config.EspNetworkName}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ✗ Не подключен к сети ESP");
            Console.ResetColor();
        }

        Console.WriteLine($"\nКонфигурация:");
        Console.WriteLine($"  Домашняя WiFi: {config.PcWifiSsid}");
        Console.WriteLine($"  IP ESP (AP режим): {config.EspApIp}:{config.EspPort}");

        if (!string.IsNullOrEmpty(config.EspHomeIp))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  IP ESP (Домашняя сеть): {config.EspHomeIp}:{config.EspPort}");
            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 50));
    }

    static string GetMenuChoice()
    {
        Console.WriteLine("\nГЛАВНОЕ МЕНЮ:");
        Console.WriteLine("1. Настроить ESP на домашнюю WiFi (команда: SET)");
        Console.WriteLine("2. Начать стриминг данных (команда: START_STREAM)");
        Console.WriteLine("3. Изменить конфигурацию");
        Console.WriteLine("4. Найти ESP в домашней сети");
        Console.WriteLine("5. Показать текущую конфигурацию");
        Console.WriteLine("6. Выход");
        Console.WriteLine("7. Тестирование всех команд ESP");
        Console.WriteLine("\n[h] Помощь по командам ESP");

        Console.Write("\nВаш выбор (1-7): ");
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
        Console.WriteLine("=== СПРАВКА ПО КОМАНДАМ ESP32 ===");
        Console.ResetColor();

        Console.WriteLine("\nКод ESP32 поддерживает следующие команды:");
        Console.WriteLine("\nSET");
        Console.WriteLine("  Настройка WiFi. Формат:");
        Console.WriteLine("  SET");
        Console.WriteLine("  SSID_вашей_сети");
        Console.WriteLine("  пароль");
        Console.WriteLine("  Пример: SET\nMyWiFi\nMyPassword123");

        Console.WriteLine("\nSTATUS");
        Console.WriteLine("  Получить текущий статус ESP32");
        Console.WriteLine("  Показывает: IP адрес, статус WiFi, RSSI, LED состояние");

        Console.WriteLine("\nSTART_STREAM");
        Console.WriteLine("  Начать стриминг данных косинуса");
        Console.WriteLine("  Данные отправляются непрерывно до команды STOP_STREAM");

        Console.WriteLine("\nSTOP_STREAM");
        Console.WriteLine("  Остановить стриминг данных");

        Console.WriteLine("\nCLEAR");
        Console.WriteLine("  Очистить сохраненные WiFi данные");
        Console.WriteLine("  ESP32 вернется в режим только точки доступа");

        Console.WriteLine("\n" + new string('═', 60));
        Console.WriteLine("\nПрограмма автоматически отправляет правильные команды.");
        Console.WriteLine("Если нужно протестировать команды вручную - используйте пункт 7.");

        WaitForKey();
    }

    static void ShowCurrentConfig(Config config)
    {
        Console.Clear();
        Console.WriteLine("=== ТЕКУЩАЯ КОНФИГУРАЦИЯ ===");
        Console.WriteLine($"\nДомашняя WiFi:");
        Console.WriteLine($"  SSID: {config.PcWifiSsid}");
        Console.WriteLine($"  Пароль: {new string('*', config.PcWifiPassword.Length)}");

        Console.WriteLine($"\nСеть ESP:");
        Console.WriteLine($"  Имя: {config.EspNetworkName}");
        Console.WriteLine($"  Пароль: {new string('*', config.EspNetworkPassword.Length)}");
        Console.WriteLine($"  IP (AP): {config.EspApIp}");
        Console.WriteLine($"  Порт: {config.EspPort}");

        Console.WriteLine($"\nСохраненные данные:");
        Console.WriteLine($"  IP ESP (домашняя): {config.EspHomeIp ?? "не задан"}");
        Console.WriteLine($"  Файл конфигурации: {Path.GetFullPath(ConfigFile)}");

        Console.WriteLine($"\nПоддерживаемые команды ESP:");
        Console.WriteLine($"  SET, STATUS, START_STREAM, STOP_STREAM, CLEAR");

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
}