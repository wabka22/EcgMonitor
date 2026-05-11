using System.Net.Sockets;
using System.Text;

namespace ESP32StreamManager
{
    public class EspTcpClient
    {
        private readonly Action<string, string, string> _log;

        public EspTcpClient(Action<string, string, string> log)
        {
            _log = log;
        }

        private void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            _log(msg, level, deviceTag);
        }

        public bool CheckEspAvailability(string ip, int port, int timeout = 1000)
        {
            try
            {
                using var client = new TcpClient();

                IAsyncResult result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeout);

                if (!success)
                    return false;

                client.EndConnect(result);

                using var stream = client.GetStream();

                stream.WriteTimeout = timeout;
                stream.ReadTimeout = timeout;

                byte[] ping = Encoding.UTF8.GetBytes("PING\n");
                stream.Write(ping, 0, ping.Length);

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                return !string.IsNullOrWhiteSpace(response);
            }
            catch
            {
                return false;
            }
        }

        public string SendCommandToEsp(
            string ip,
            int port,
            string command,
            int timeout = 3000)
        {
            try
            {
                using var client = new TcpClient();

                IAsyncResult result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeout);

                if (!success)
                    return "ERROR: Connection failed";

                client.EndConnect(result);

                using var stream = client.GetStream();

                stream.WriteTimeout = timeout;
                stream.ReadTimeout = timeout;

                byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
                stream.Write(cmdBytes, 0, cmdBytes.Length);

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            catch (Exception e)
            {
                return $"ERROR: {e.Message}";
            }
        }

        public bool SendWifiCredentialsToEsp(
            string espIp,
            int espPort,
            string ssid,
            string pass,
            string deviceTag)
        {
            Log($"Отправка данных Wi-Fi на {deviceTag}...", "INFO", deviceTag);

            try
            {
                using var client = new TcpClient();
                client.Connect(espIp, espPort);

                using var stream = client.GetStream();

                string data = $"SET\n{ssid}\n{pass}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(data);

                stream.Write(bytes, 0, bytes.Length);

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (response.Contains("OK"))
                {
                    Log("Данные Wi-Fi отправлены успешно. ESP перезагрузится.",
                        "SUCCESS",
                        deviceTag);

                    return true;
                }

                Log($"Ошибка: {response.Trim()}", "WARN", deviceTag);
                return false;
            }
            catch (Exception e)
            {
                Log($"Ошибка: {e.Message}", "ERROR", deviceTag);
                return false;
            }
        }
    }
}