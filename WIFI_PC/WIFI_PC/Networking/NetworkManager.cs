using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace ESP32StreamManager
{
    public class NetworkManager
    {
        private MainForm _mainForm;

        public NetworkManager(MainForm mainForm)
        {
            _mainForm = mainForm;
        }

        private void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            _mainForm.Log(msg, level, deviceTag);
        }

        // ---------- ПРОВЕРКА ДОСТУПНОСТИ ESP ----------
        public bool CheckEspAvailability(string ip, int port, int timeout = 1000)
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
        public string SendCommandToEsp(string ip, int port, string command, int timeout = 3000)
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

        // ---------- УЛУЧШЕННАЯ ПРОВЕРКА ПОДКЛЮЧЕНИЯ К СЕТИ ----------
        public bool IsConnectedToNetwork(string networkName)
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

                if (output.Contains($" {networkName} ") ||
                    output.Contains($": {networkName}") ||
                    output.Contains($"SSID : {networkName}") ||
                    output.Contains($"SSID : {networkName}"))
                {
                    string[] connectedMarkers = { "State : connected", "Состояние : подключено",
                                                  "State             : connected", "Состояние         : подключено" };
                    foreach (string marker in connectedMarkers)
                    {
                        if (output.Contains(marker))
                        {
                            return true;
                        }
                    }

                    int ssidIndex = output.IndexOf(networkName, StringComparison.OrdinalIgnoreCase);
                    if (ssidIndex >= 0)
                    {
                        int start = Math.Max(0, ssidIndex - 100);
                        int length = Math.Min(200, output.Length - start);
                        string context = output.Substring(start, length);

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

        // ---------- ВЫПОЛНЕНИЕ КОМАНД netsh ----------
        public string RunNetshCommand(string arguments, bool showErrors = true)
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

        // ---------- ПОДКЛЮЧЕНИЕ К СЕТИ ESP ----------
        public bool ConnectToEspNetwork(string networkName, string password)
        {
            Log($"Подключение к сети ESP: {networkName}...", "INFO");

            try
            {
                RunNetshCommand("wlan disconnect", false);
                Thread.Sleep(2000);

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

                string tempFile = Path.Combine(Path.GetTempPath(), $"wifi_{Guid.NewGuid().ToString("N").Substring(0, 8)}.xml");
                File.WriteAllText(tempFile, profileXml, Encoding.UTF8);

                RunNetshCommand($"wlan delete profile name=\"{networkName}\"", false);
                Thread.Sleep(500);

                string addResult = RunNetshCommand($"wlan add profile filename=\"{tempFile}\"");
                if (addResult.Contains("added") || addResult.Contains("добавлен"))
                {
                    Log("Профиль WiFi успешно добавлен", "SUCCESS");
                }
                else
                {
                    Log($"Ошибка добавления профиля: {addResult}", "WARN");
                }

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

                try { File.Delete(tempFile); } catch { }

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    Log($"Попытка подключения {attempt}/3...", "INFO");

                    for (int i = 0; i < 15; i++)
                    {
                        Thread.Sleep(1000);
                        if (IsConnectedToNetwork(networkName))
                        {
                            Log($"Успешно подключен к сети {networkName}!", "SUCCESS");
                            Thread.Sleep(2000);
                            return true;
                        }

                        if (i % 5 == 0)
                        {
                            Log($"Ожидание подключения... {i + 1}/15 секунд", "INFO");
                        }
                    }

                    if (attempt < 3)
                    {
                        Log("Повторная попытка подключения через 2 секунды...", "INFO");
                        Thread.Sleep(2000);
                    }
                }

                Log("Не удалось подключиться к сети ESP", "ERROR");

                Log("Доступные сети WiFi:", "INFO");
                string networks = RunNetshCommand("wlan show networks");
                Log(networks, "DIAG");

                return false;
            }
            catch (Exception e)
            {
                Log($"Ошибка подключения: {e.Message}", "ERROR");
                return false;
            }
        }

        // ---------- УПРОЩЕННОЕ ПОДКЛЮЧЕНИЕ ----------
        public bool SimpleConnectToEspNetwork(string networkName, string password)
        {
            try
            {
                Log($"Пытаемся подключиться к {networkName}...", "INFO");

                string result = RunNetshCommand($"wlan connect name=\"{networkName}\" ssid=\"{networkName}\" key=\"{password}\"");

                if (result.Contains("successfully") || result.Contains("успешно") ||
                    result.Contains("requested") || result.Contains("выполнен"))
                {
                    Log($"Запрос на подключение отправлен", "SUCCESS");

                    for (int i = 0; i < 10; i++)
                    {
                        Thread.Sleep(1000);
                        if (IsConnectedToNetwork(networkName))
                        {
                            Log($"Успешно подключен к {networkName}!", "SUCCESS");
                            return true;
                        }
                    }
                }

                Log("Не удалось подключиться. Попробуйте подключиться вручную через WiFi настройки Windows.", "WARN");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}", "ERROR");
                return false;
            }
        }

        // ---------- ОТПРАВКА ДАННЫХ WIFI НА ESP ----------
        public bool SendWifiCredentialsToEsp(string espIp, int espPort, string ssid, string pass, string deviceTag)
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

        // ---------- ПИНГ УСТРОЙСТВ В СЕТИ ----------
        public void PingDevicesInNetwork()
        {
            try
            {
                Log("Пинг устройств в сети...", "INFO");

                string[] ipsToPing = {
                    "192.168.137.102", "192.168.137.173",
                    "192.168.137.1", "192.168.137.100",
                    "192.168.1.102", "192.168.1.173"
                };

                foreach (string ip in ipsToPing)
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ping",
                            Arguments = $"-n 1 -w 500 {ip}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (output.Contains("TTL=") || output.Contains("Ответ"))
                    {
                        Log($"✓ Устройство доступно: {ip}", "SUCCESS");
                    }
                    else
                    {
                        Log($"✗ Устройство недоступно: {ip}", "WARN");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка пинга: {ex.Message}", "ERROR");
            }
        }

        // ---------- НАЙТИ IP ПО MAC-АДРЕСУ ----------
        public string FindIpByMacAddress(string macAddress)
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
                    string normalizedMac = macAddress.Replace(':', '-').ToLower();

                    if (line.ToLower().Contains(normalizedMac))
                    {
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
    }
}