using System.Net.Sockets;
using System.Text;

namespace ESP32StreamManager
{
    public class StreamWorker
    {
        public EspDevice Device { get; }
        public string Ip { get; }
        private volatile bool _running = false;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private Thread? _workerThread;
        private readonly MainForm _mainForm;
        private bool _stopped = false;

        public event Action<string, string> DataReceived;

        public StreamWorker(MainForm mainForm, EspDevice device, string ip)
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
                _stream.ReadTimeout = 1000;
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
                        string data = _reader.ReadLine();

                        if (!string.IsNullOrWhiteSpace(data))
                        {
                            DataReceived?.Invoke(Device.Name, data);
                        }
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch
                    {
                        break;
                    }
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
            if (_stopped)
                return;

            _stopped = true;
            _running = false;

            try
            {
                if (_stream != null && _client != null && _client.Connected)
                {
                    byte[] stopBytes = Encoding.UTF8.GetBytes("STOP_STREAM\n");
                    _stream.Write(stopBytes, 0, stopBytes.Length);
                    _stream.Flush();
                    Thread.Sleep(100);
                }
            }
            catch { }

            try
            {
                _reader?.Close();
                _stream?.Close();
                _client?.Close();
            }
            catch { }

            lock (_mainForm._activeWorkers)
            {
                _mainForm._activeWorkers.Remove(this);
            }

            try
            {
                if (!_mainForm.IsDisposed && _mainForm.IsHandleCreated)
                {
                    _mainForm.Invoke(new Action(() =>
                    {
                        _mainForm.Log("СТРИМИНГ ОСТАНОВЛЕН", "INFO", Device.Name);
                        _mainForm.UpdateUI();
                    }));
                }
            }
            catch { }
        }
    }
}
