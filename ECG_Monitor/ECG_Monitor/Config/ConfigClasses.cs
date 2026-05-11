namespace ESP32StreamManager
{
    public class Config
    {
        public string HotspotSsid { get; set; } = "MyHomeWiFi";
        public string HotspotPassword { get; set; } = "mypassword123";

        public string HotspotSubnet { get; set; } = "192.168.137";
        public List<EspDevice> EspDevices { get; set; } = new();
    }

    public class EspDevice
    {
        public string Name { get; set; } = "";
        public string ApSsid { get; set; } = "";
        public string ApPassword { get; set; } = "";
        public string ApIp { get; set; } = "192.168.4.1";
        public int Port { get; set; } = 8888;
        public string? HotspotIp { get; set; }
        public string MacAddress { get; set; } = "";
    }
}