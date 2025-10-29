using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

class Program
{
    const string ConfigFile = "config.json";

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

    // ---------- ЗАГРУЗКА КОНФИГА ----------
    static dynamic LoadConfig()
    {
        try
        {
            string json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<dynamic>(json);
        }
        catch (Exception e)
        {
            Log($"Ошибка при загрузке конфига: {e}", "ERROR");
            Environment.Exit(1);
            return null;
        }
    }

    // ---------- СКАНИРОВАНИЕ Wi-Fi (только Windows) ----------
    static List<string> ScanNetworks()
    {
        try
        {
            List<string> networks = new List<string>();

            // Используем netsh, вывод в CP866
            ProcessStartInfo psi = new ProcessStartInfo("netsh", "wlan show networks mode=Bssid")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(866) // после регистрации
            };

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.StartsWith("SSID"))
                    {
                        var parts = trimmed.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            string ssid = parts[1].Trim();
                            if (!string.IsNullOrEmpty(ssid))
                                networks.Add(ssid);
                        }
                    }
                }
            }

            return networks.Distinct().ToList();
        }
        catch (Exception e)
        {
            Log($"Ошибка при сканировании Wi-Fi: {e}", "ERROR");
            return new List<string>();
        }
    }

    // ---------- ПОДКЛЮЧЕНИЕ ----------
    static void ConnectToNetwork(string ssid)
    {
        Log($"Подключение к сети {ssid}...", "INFO");

        try
        {
            // Подключаемся напрямую через netsh
            Process.Start("netsh", $"wlan connect ssid=\"{ssid}\" name=\"{ssid}\"").WaitForExit();
            Thread.Sleep(5000); // ждём, чтобы соединение успело установиться
            Log($"Подключение к {ssid} выполнено (или в процессе)...", "SUCCESS");
        }
        catch (Exception e)
        {
            Log($"Ошибка подключения: {e}", "ERROR");
        }
    }

    // ---------- ОТПРАВКА ДАННЫХ НА ESP ----------
    static void SendWifiCredentialsToEsp(string pcSsid, string pcPassword)
    {
        Log("Отправка данных на ESP...", "INFO");
        string espIp = "192.168.4.1";
        int espPort = 8888;

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(espIp, espPort);
                using (NetworkStream stream = client.GetStream())
                {
                    string data = $"SET\n{pcSsid}\n{pcPassword}\n";
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
                    stream.Write(bytes, 0, bytes.Length);

                    byte[] buffer = new byte[1024];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    string response = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                    Log($"Ответ от ESP: {response.Trim()}", "SUCCESS");
                }
            }
        }
        catch (Exception e)
        {
            Log($"Ошибка при отправке данных на ESP: {e}", "ERROR");
        }
    }

    // ---------- ОСНОВНОЙ ЦИКЛ ----------
    static void Main()
    {
        // 🔹 Обязательно для CP866 на Windows
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var config = LoadConfig();
        string espName = config.GetProperty("esp_network_name").GetString();
        string espPass = config.GetProperty("esp_network_password").GetString();
        string pcSsid = config.GetProperty("pc_wifi_ssid").GetString();
        string pcPass = config.GetProperty("pc_wifi_password").GetString();

        Log("=== ESP32 Auto-Connector ===", "INFO");

        while (true)
        {
            var networks = ScanNetworks();
            if (networks.Count == 0)
            {
                Log("Нет доступных сетей. Повтор через 5 секунд...", "WARN");
                Thread.Sleep(5000);
                continue;
            }

            if (networks.Contains(espName))
            {
                Log($"Обнаружена ESP-сеть: {espName}", "SUCCESS");
                ConnectToNetwork(espName);
                SendWifiCredentialsToEsp(pcSsid, pcPass);
                Log("Ожидание 10 секунд перед повторной проверкой...", "INFO");
                Thread.Sleep(10000);
            }
            else
            {
                Log($"ESP-сеть '{espName}' не найдена. Повтор через 5 секунд...", "WARN");
                Thread.Sleep(5000);
            }
        }
    }
}
