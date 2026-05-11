using System.Diagnostics;
using System.Text;

namespace ESP32StreamManager
{
    public class WifiProfileManager
    {
        private readonly Action<string, string, string> _log;

        public WifiProfileManager(Action<string, string, string> log)
        {
            _log = log;
        }

        private void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            _log(msg, level, deviceTag);
        }

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

                bool containsSsid =
                    output.Contains($" {networkName} ") ||
                    output.Contains($": {networkName}") ||
                    output.Contains($"SSID : {networkName}");

                if (!containsSsid)
                    return false;

                string[] connectedMarkers =
                {
                    "State : connected",
                    "State             : connected",
                    "Состояние : подключено",
                    "Состояние         : подключено"
                };

                foreach (string marker in connectedMarkers)
                {
                    if (output.Contains(marker))
                        return true;
                }

                int ssidIndex = output.IndexOf(
                    networkName,
                    StringComparison.OrdinalIgnoreCase);

                if (ssidIndex >= 0)
                {
                    int start = Math.Max(0, ssidIndex - 100);
                    int length = Math.Min(200, output.Length - start);

                    string context = output.Substring(start, length);

                    string[] contextMarkers =
                    {
                        "connected",
                        "подключено",
                        "authenticated",
                        "аутентифицирован"
                    };

                    foreach (string marker in contextMarkers)
                    {
                        if (context.Contains(
                            marker,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
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

                if (!string.IsNullOrWhiteSpace(error) && showErrors)
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

        public bool ConnectToEspNetwork(string networkName, string password)
        {
            Log($"Подключение к сети ESP: {networkName}...", "INFO");

            try
            {
                RunNetshCommand("wlan disconnect", false);
                Thread.Sleep(2000);

                string profileXml = CreateWifiProfileXml(networkName, password);
                string tempFile = Path.Combine(
                    Path.GetTempPath(),
                    $"wifi_{Guid.NewGuid():N}.xml");

                File.WriteAllText(tempFile, profileXml, Encoding.UTF8);

                RunNetshCommand($"wlan delete profile name=\"{networkName}\"", false);
                Thread.Sleep(500);

                string addResult = RunNetshCommand(
                    $"wlan add profile filename=\"{tempFile}\"");

                if (addResult.Contains("added") ||
                    addResult.Contains("добавлен"))
                {
                    Log("Профиль Wi-Fi успешно добавлен", "SUCCESS");
                }
                else
                {
                    Log($"Ошибка добавления профиля: {addResult}", "WARN");
                }

                Log($"Подключаюсь к сети {networkName}...", "INFO");

                string connectResult = RunNetshCommand(
                    $"wlan connect name=\"{networkName}\"");

                if (connectResult.Contains("requested") ||
                    connectResult.Contains("выполнен") ||
                    connectResult.Contains("connected") ||
                    connectResult.Contains("подключен"))
                {
                    Log($"Запрос на подключение к {networkName} отправлен",
                        "SUCCESS");
                }
                else
                {
                    Log($"Результат подключения: {connectResult}", "WARN");
                }

                try
                {
                    File.Delete(tempFile);
                }
                catch { }

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    Log($"Попытка подключения {attempt}/3...", "INFO");

                    for (int i = 0; i < 15; i++)
                    {
                        Thread.Sleep(1000);

                        if (IsConnectedToNetwork(networkName))
                        {
                            Log($"Успешно подключен к сети {networkName}!",
                                "SUCCESS");

                            Thread.Sleep(2000);
                            return true;
                        }

                        if (i % 5 == 0)
                        {
                            Log($"Ожидание подключения... {i + 1}/15 секунд",
                                "INFO");
                        }
                    }

                    if (attempt < 3)
                    {
                        Log("Повторная попытка подключения через 2 секунды...",
                            "INFO");

                        Thread.Sleep(2000);
                    }
                }

                Log("Не удалось подключиться к сети ESP", "ERROR");

                Log("Доступные сети Wi-Fi:", "INFO");
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

        public bool SimpleConnectToEspNetwork(string networkName, string password)
        {
            try
            {
                Log($"Пытаемся подключиться к {networkName}...", "INFO");

                string result = RunNetshCommand(
                    $"wlan connect name=\"{networkName}\" ssid=\"{networkName}\" key=\"{password}\"");

                if (result.Contains("successfully") ||
                    result.Contains("успешно") ||
                    result.Contains("requested") ||
                    result.Contains("выполнен"))
                {
                    Log("Запрос на подключение отправлен", "SUCCESS");

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

                Log("Не удалось подключиться. Попробуйте подключиться вручную через настройки Wi-Fi Windows.",
                    "WARN");

                return false;
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}", "ERROR");
                return false;
            }
        }

        private string CreateWifiProfileXml(string networkName, string password)
        {
            return $@"<?xml version=""1.0""?>
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
        }
    }
}