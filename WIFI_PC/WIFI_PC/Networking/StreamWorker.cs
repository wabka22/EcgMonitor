using ESP32StreamManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ESP32StreamManager
{
    public class StreamWorker
    {
        public EspDeviceConfig Device { get; }
        public string Ip { get; }
        private volatile bool _running = false;
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private Thread _workerThread;
        private MainForm _mainForm;

        public event Action<string, string> DataReceived;

        public StreamWorker(MainForm mainForm, EspDeviceConfig device, string ip)
        {
            _mainForm = mainForm;
            Device = device;
            Ip = ip;
        }

        public void Start()
        {
            _workerThread = new Thread(WorkerLoop);
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void WorkerLoop()
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

                lock (_mainForm._activeWorkers)
                {
                    _mainForm._activeWorkers.Add(this);
                }

                _mainForm.Invoke(new Action(() =>
                {
                    _mainForm.Log("СТРИМИНГ НАЧАТ", "SUCCESS", Device.Name);
                    _mainForm.UpdateUI();
                }));

                while (_running && _client.Connected)
                {
                    try
                    {
                        if (_stream.DataAvailable)
                        {
                            string data = _reader.ReadLine();
                            if (!string.IsNullOrEmpty(data))
                            {
                                DataReceived?.Invoke(Device.Name, data);
                            }
                        }
                        Thread.Sleep(10);
                    }
                    catch { break; }
                }
            }
            catch (Exception ex)
            {
                _mainForm.Invoke(new Action(() =>
                {
                    _mainForm.Log($"ОШИБКА: {ex.Message}", "ERROR", Device.Name);
                }));
            }
            finally
            {
                Stop();
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

                lock (_mainForm._activeWorkers)
                {
                    _mainForm._activeWorkers.Remove(this);
                }

                _mainForm.Invoke(new Action(() =>
                {
                    _mainForm.Log("СТРИМИНГ ОСТАНОВЛЕН", "INFO", Device.Name);
                    _mainForm.UpdateUI();
                }));
            }
            catch { }
        }
    }
}
