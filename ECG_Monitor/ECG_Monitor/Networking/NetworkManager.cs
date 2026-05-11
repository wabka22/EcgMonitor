namespace ESP32StreamManager
{
    public class NetworkManager
    {
        private readonly MainForm _mainForm;
        private readonly EspTcpClient _espClient;
        private readonly WifiProfileManager _wifiManager;
        private readonly NetworkScanner _scanner;

        public NetworkManager(MainForm mainForm)
        {
            _mainForm = mainForm;
            _espClient = new EspTcpClient(Log);
            _wifiManager = new WifiProfileManager(Log);
            _scanner = new NetworkScanner(Log);
        }

        private void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            _mainForm.Log(msg, level, deviceTag);
        }

        public bool CheckEspAvailability(string ip, int port, int timeout = 1000)
        {
            return _espClient.CheckEspAvailability(ip, port, timeout);
        }

        public string SendCommandToEsp(string ip, int port, string command, int timeout = 3000)
        {
            return _espClient.SendCommandToEsp(ip, port, command, timeout);
        }

        public bool SendWifiCredentialsToEsp(
            string espIp,
            int espPort,
            string ssid,
            string pass,
            string deviceTag)
        {
            return _espClient.SendWifiCredentialsToEsp(
                espIp,
                espPort,
                ssid,
                pass,
                deviceTag);
        }

        public bool IsConnectedToNetwork(string networkName)
        {
            return _wifiManager.IsConnectedToNetwork(networkName);
        }

        public string RunNetshCommand(string arguments, bool showErrors = true)
        {
            return _wifiManager.RunNetshCommand(arguments, showErrors);
        }

        public bool ConnectToEspNetwork(string networkName, string password)
        {
            return _wifiManager.ConnectToEspNetwork(networkName, password);
        }

        public bool SimpleConnectToEspNetwork(string networkName, string password)
        {
            return _wifiManager.SimpleConnectToEspNetwork(networkName, password);
        }

        public void PingDevicesInNetwork()
        {
            _scanner.PingDevicesInNetwork();
        }

        public string? FindIpByMacAddress(string macAddress)
        {
            return _scanner.FindIpByMacAddress(macAddress);
        }
    }
}