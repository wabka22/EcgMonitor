using System.Diagnostics;
using System.Text;

namespace ESP32StreamManager
{
    public class NetworkScanner
    {
        private readonly Action<string, string, string> _log;

        public NetworkScanner(Action<string, string, string> log)
        {
            _log = log;
        }

        private void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            _log(msg, level, deviceTag);
        }

        public void PingDevicesInNetwork()
        {
            try
            {
                Log("Пинг устройств в сети...", "INFO");

                string[] ipsToPing =
                {
                    "192.168.137.102",
                    "192.168.137.173",
                    "192.168.137.1",
                    "192.168.137.100",
                    "192.168.1.102",
                    "192.168.1.173"
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
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.GetEncoding(866)
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

        public string? FindIpByMacAddress(string macAddress)
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

                string normalizedMac = macAddress
                    .Replace(':', '-')
                    .ToLower();

                string[] lines = output.Split('\n');

                foreach (string line in lines)
                {
                    if (line.ToLower().Contains(normalizedMac))
                    {
                        string[] parts = line.Split(
                            new[] { ' ', '\t' },
                            StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 1)
                            return parts[0];
                    }
                }
            }
            catch { }

            return null;
        }
    }
}