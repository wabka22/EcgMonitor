using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace ESP32StreamManager
{
    public partial class MainForm : Form
    {
        private const string ConfigFile = "config.json";
        private List<StreamWorker> _activeWorkers = new List<StreamWorker>();
        private Config _config;
        private NetworkManager _networkManager;

        // UI Controls
        private Panel panelTop;
        private Panel panelCenter;
        private Panel panelBottom;
        private PlotView plotSin;
        private PlotView plotCos;
        private Button btnConnectSingle;
        private Button btnStreamSingle;
        private Button btnStopSingle;
        private Button btnFindESP;
        private Button btnParallelConfig;
        private Button btnParallelStream;
        private Button btnStopAll;
        private Button btnClearPlots;
        private Button btnDiagnose;
        private Button btnExit;
        private Label lblStatus;
        private Label lblSinStatus;
        private Label lblCosStatus;
        private RichTextBox txtLog;
        private Panel controlPanel;

        // Data for plotting
        private List<DataPoint> sinData = new List<DataPoint>();
        private List<DataPoint> cosData = new List<DataPoint>();
        private const int MaxDataPoints = 500;
        private LineSeries sinSeries;
        private LineSeries cosSeries;
        private DateTime _plotStartTime;
        private double _timeWindow = 30.0;

        private List<string> _pendingLogs = new List<string>();
        private bool _isFormLoaded = false;

        public MainForm()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            InitializeComponent();
            LoadConfig();
            SetupPlots();
        }

        private void InitializeComponent()
        {
            this.Text = "ESP32 Dual Stream Manager";
            this.Size = new System.Drawing.Size(1400, 1000); // Увеличили высоту окна
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 30);

            // Top Panel - Status
            panelTop = new Panel();
            panelTop.Dock = DockStyle.Top;
            panelTop.Height = 100;
            panelTop.BackColor = Color.FromArgb(45, 45, 48);

            lblStatus = new Label();
            lblStatus.ForeColor = Color.White;
            lblStatus.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            lblStatus.Text = "Статус: Готов к работе";
            lblStatus.Location = new Point(20, 15);
            lblStatus.Size = new Size(500, 25);

            lblSinStatus = new Label();
            lblSinStatus.ForeColor = Color.LightGreen;
            lblSinStatus.Font = new Font("Segoe UI", 9);
            lblSinStatus.Text = "ESP32_Sin: Отключено";
            lblSinStatus.Location = new Point(20, 45);
            lblSinStatus.Size = new Size(400, 20);

            lblCosStatus = new Label();
            lblCosStatus.ForeColor = Color.LightGreen;
            lblCosStatus.Font = new Font("Segoe UI", 9);
            lblCosStatus.Text = "ESP32_Cos: Отключено";
            lblCosStatus.Location = new Point(20, 65);
            lblCosStatus.Size = new Size(400, 20);

            panelTop.Controls.Add(lblStatus);
            panelTop.Controls.Add(lblSinStatus);
            panelTop.Controls.Add(lblCosStatus);

            // Center Panel - Plots
            panelCenter = new Panel();
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.BackColor = Color.FromArgb(25, 25, 25);
            panelCenter.Padding = new Padding(10);

            // Используем TableLayoutPanel для одинаковых графиков
            TableLayoutPanel tableLayout = new TableLayoutPanel();
            tableLayout.Dock = DockStyle.Fill;
            tableLayout.RowCount = 2;
            tableLayout.ColumnCount = 1;
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tableLayout.Padding = new Padding(0);

            // Sin Plot
            plotSin = new PlotView();
            plotSin.Dock = DockStyle.Fill;
            plotSin.BackColor = Color.FromArgb(40, 40, 40);
            plotSin.Margin = new Padding(0, 0, 0, 5);

            // Cos Plot
            plotCos = new PlotView();
            plotCos.Dock = DockStyle.Fill;
            plotCos.BackColor = Color.FromArgb(40, 40, 40);
            plotCos.Margin = new Padding(0, 5, 0, 0);

            // Добавляем графики в TableLayoutPanel
            tableLayout.Controls.Add(plotSin, 0, 0);
            tableLayout.Controls.Add(plotCos, 0, 1);

            panelCenter.Controls.Add(tableLayout);

            // Bottom Panel - Controls and Log
            panelBottom = new Panel();
            panelBottom.Dock = DockStyle.Bottom;
            panelBottom.Height = 350;
            panelBottom.BackColor = Color.FromArgb(37, 37, 38);
            panelBottom.Padding = new Padding(0);

            // Control buttons panel
            controlPanel = new Panel();
            controlPanel.Dock = DockStyle.Left;
            controlPanel.Width = 320;
            controlPanel.BackColor = Color.FromArgb(55, 55, 58);
            controlPanel.Padding = new Padding(10);
            controlPanel.AutoScroll = true;

            // Single operations
            Label lblSingle = new Label();
            lblSingle.Text = "ОДИНОЧНЫЕ ОПЕРАЦИИ";
            lblSingle.ForeColor = Color.Cyan;
            lblSingle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            lblSingle.Location = new Point(10, 10);
            lblSingle.Size = new Size(280, 20);

            btnConnectSingle = CreateButton("Настроить ESP", 10, 40);
            btnConnectSingle.Click += (s, e) => ConfigureSingleEsp();

            btnStreamSingle = CreateButton("Стриминг с одной ESP", 10, 75);
            btnStreamSingle.Click += (s, e) => StreamFromSingleEsp();

            btnStopSingle = CreateButton("Остановить стриминг", 10, 110);
            btnStopSingle.Click += (s, e) => StopSingleStream();
            btnStopSingle.BackColor = Color.FromArgb(80, 0, 0);

            btnFindESP = CreateButton("Найти ESP в сети", 10, 145);
            btnFindESP.Click += (s, e) => FindEspInHotspotNetwork();

            // Parallel operations
            Label lblParallel = new Label();
            lblParallel.Text = "ПАРАЛЛЕЛЬНЫЕ ОПЕРАЦИИ";
            lblParallel.ForeColor = Color.Cyan;
            lblParallel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            lblParallel.Location = new Point(10, 185);
            lblParallel.Size = new Size(280, 20);

            btnParallelConfig = CreateButton("Настройка двух ESP", 10, 215);
            btnParallelConfig.Click += (s, e) => ConfigureTwoEspParallel();

            btnParallelStream = CreateButton("Параллельный стриминг", 10, 250);
            btnParallelStream.Click += (s, e) => StreamFromTwoEspParallel();

            // Utility buttons
            Label lblUtility = new Label();
            lblUtility.Text = "УТИЛИТЫ";
            lblUtility.ForeColor = Color.Cyan;
            lblUtility.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            lblUtility.Location = new Point(10, 290);
            lblUtility.Size = new Size(280, 20);

            btnStopAll = CreateButton("Остановить все стримы", 10, 320);
            btnStopAll.Click += (s, e) => StopAllStreamsMenu();

            btnClearPlots = CreateButton("Очистить графики", 10, 355);
            btnClearPlots.Click += (s, e) => ClearPlots();
            btnClearPlots.BackColor = Color.FromArgb(80, 80, 0);

            controlPanel.Controls.AddRange(new Control[] {
                lblSingle, btnConnectSingle, btnStreamSingle, btnStopSingle, btnFindESP,
                lblParallel, btnParallelConfig, btnParallelStream,
                lblUtility, btnStopAll, btnClearPlots, btnDiagnose, btnExit
            });

            // Log panel
            Panel logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(5);

            txtLog = new RichTextBox();
            txtLog.Multiline = true;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = Color.White;
            txtLog.Font = new Font("Consolas", 9);
            txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtLog.ReadOnly = true;

            logPanel.Controls.Add(txtLog);

            panelBottom.Controls.Add(logPanel);
            panelBottom.Controls.Add(controlPanel);

            // Add all panels to form
            this.Controls.Add(panelCenter);
            this.Controls.Add(panelBottom);
            this.Controls.Add(panelTop);

            // Подписываемся на событие Load
            this.Load += MainForm_Load;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _isFormLoaded = true;
            _networkManager = new NetworkManager(this);
            _plotStartTime = DateTime.Now;

            // Выводим накопленные логи
            if (_pendingLogs.Count > 0)
            {
                foreach (var log in _pendingLogs)
                {
                    AddLogToTextBox(log, Color.White);
                }
                _pendingLogs.Clear();
            }

            UpdateUI();

            // Проверяем доступность ESP при старте
            Task.Run(() => CheckEspOnStartup());
        }

        private void CheckEspOnStartup()
        {
            Log("=== ПРОВЕРКА ДОСТУПНОСТИ ESP ПРИ СТАРТЕ ===", "INFO");

            foreach (var device in _config.EspDevices)
            {
                if (!string.IsNullOrEmpty(device.HotspotIp))
                {
                    bool isAvailable = _networkManager.CheckEspAvailability(device.HotspotIp, device.Port, 1000);
                    Log($"{device.Name} ({device.HotspotIp}): {(isAvailable ? "✓ Доступна" : "✗ Недоступна")}",
                        isAvailable ? "SUCCESS" : "WARN");
                }
            }
            UpdateUI();
        }

        private Button CreateButton(string text, int x, int y)
        {
            var btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
            btn.Size = new Size(300, 30);
            btn.BackColor = Color.FromArgb(62, 62, 66);
            btn.ForeColor = Color.White;
            btn.FlatStyle = FlatStyle.Flat;
            btn.Font = new Font("Segoe UI", 9);
            return btn;
        }

        private void SetupPlots()
        {
            // Setup Sin Plot
            var sinModel = new PlotModel
            {
                Title = "ESP32_Sin - Синусоидальный сигнал",
                TitleColor = OxyColors.Cyan,
                TitleFontSize = 14,
                PlotAreaBorderColor = OxyColors.Gray,
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = OxyColors.White,
                Background = OxyColors.Black
            };

            sinModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Амплитуда",
                TitleColor = OxyColors.White,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.Gray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkGray,
                Minimum = -1.5,
                Maximum = 1.5
            });

            sinModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Время (с)",
                TitleColor = OxyColors.White,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.Gray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkGray
            });

            sinSeries = new LineSeries
            {
                Title = "Sin",
                Color = OxyColors.LightGreen,
                StrokeThickness = 2
            };

            sinModel.Series.Add(sinSeries);
            plotSin.Model = sinModel;

            // Setup Cos Plot
            var cosModel = new PlotModel
            {
                Title = "ESP32_Cos - Косинусоидальный сигнал",
                TitleColor = OxyColors.Cyan,
                TitleFontSize = 14,
                PlotAreaBorderColor = OxyColors.Gray,
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = OxyColors.White,
                Background = OxyColors.Black
            };

            cosModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Амплитуда",
                TitleColor = OxyColors.White,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.Gray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkGray,
                Minimum = -1.5,
                Maximum = 1.5
            });

            cosModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Время (с)",
                TitleColor = OxyColors.White,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.Gray,
                MajorGridlineColor = OxyColors.Gray,
                MinorGridlineColor = OxyColors.DarkGray
            });

            cosSeries = new LineSeries
            {
                Title = "Cos",
                Color = OxyColors.LightCoral,
                StrokeThickness = 2
            };

            cosModel.Series.Add(cosSeries);
            plotCos.Model = cosModel;
        }

        public void UpdateUI()
        {
            if (!_isFormLoaded) return;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateUI));
                return;
            }

            lblStatus.Text = $"Статус: Активных стримов: {_activeWorkers.Count}";

            var sinDevice = _config?.EspDevices.FirstOrDefault(d => d.Name.Contains("Sin"));
            var cosDevice = _config?.EspDevices.FirstOrDefault(d => d.Name.Contains("Cos"));

            if (sinDevice != null)
            {
                bool isStreaming = _activeWorkers.Any(w => w.Device.Name == sinDevice.Name);
                bool hasIP = !string.IsNullOrEmpty(sinDevice.HotspotIp);
                lblSinStatus.Text = $"ESP32_Sin: {(isStreaming ? "Стриминг" : "Отключено")} | IP: {(hasIP ? sinDevice.HotspotIp : "Нет IP")}";
                lblSinStatus.ForeColor = isStreaming ? Color.Yellow : (hasIP ? Color.LightGreen : Color.LightGray);
            }

            if (cosDevice != null)
            {
                bool isStreaming = _activeWorkers.Any(w => w.Device.Name == cosDevice.Name);
                bool hasIP = !string.IsNullOrEmpty(cosDevice.HotspotIp);
                lblCosStatus.Text = $"ESP32_Cos: {(isStreaming ? "Стриминг" : "Отключено")} | IP: {(hasIP ? cosDevice.HotspotIp : "Нет IP")}";
                lblCosStatus.ForeColor = isStreaming ? Color.Yellow : (hasIP ? Color.LightGreen : Color.LightGray);
            }

            // Update plot titles if streaming
            if (plotSin.Model != null)
            {
                bool sinStreaming = _activeWorkers.Any(w => w.Device.Name.Contains("Sin"));
                plotSin.Model.Title = sinStreaming ?
                    "ESP32_Sin - АКТИВНЫЙ СТРИМИНГ" :
                    "ESP32_Sin - Синусоидальный сигнал";
                plotSin.InvalidatePlot(true);
            }

            if (plotCos.Model != null)
            {
                bool cosStreaming = _activeWorkers.Any(w => w.Device.Name.Contains("Cos"));
                plotCos.Model.Title = cosStreaming ?
                    "ESP32_Cos - АКТИВНЫЙ СТРИМИНГ" :
                    "ESP32_Cos - Косинусоидальный сигнал";
                plotCos.InvalidatePlot(true);
            }
        }

        public void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string tag = string.IsNullOrEmpty(deviceTag) ? "" : $"[{deviceTag}] ";
            string logMsg = $"[{ts}] [{level}] {tag}{msg}";

            Color color = level switch
            {
                "INFO" => Color.Cyan,
                "SUCCESS" => Color.LightGreen,
                "WARN" => Color.Yellow,
                "ERROR" => Color.Red,
                "DIAG" => Color.Gray,
                "DATA" => Color.White,
                _ => Color.White
            };

            if (!_isFormLoaded)
            {
                // Сохраняем логи до загрузки формы
                _pendingLogs.Add(logMsg);
                return;
            }

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AddLogToTextBox(logMsg, color)));
            }
            else
            {
                AddLogToTextBox(logMsg, color);
            }

            // Also update UI status for important messages
            if (level == "ERROR" || level == "SUCCESS")
            {
                UpdateUI();
            }
        }

        private void AddLogToTextBox(string logMsg, Color color)
        {
            try
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.SelectionLength = 0;
                txtLog.SelectionColor = color;
                txtLog.AppendText(logMsg + Environment.NewLine);
                txtLog.SelectionColor = txtLog.ForeColor;
                txtLog.ScrollToCaret();
            }
            catch { }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    _config = JsonSerializer.Deserialize<Config>(json);
                    Log("Конфигурация загружена", "SUCCESS");
                }
                else
                {
                    _config = new Config
                    {
                        HotspotSsid = "MyHomeWiFi",
                        HotspotPassword = "mypassword122",
                        EspDevices = new List<EspDeviceConfig>
                        {
                            new EspDeviceConfig
                            {
                                Name = "ESP32_Cos",
                                ApSsid = "ESP32_Cos_Streamer",
                                ApPassword = "12345678",
                                ApIp = "192.168.4.1",
                                Port = 8888,
                                HotspotIp = "192.168.137.102",
                                MacAddress = "c4:de:e2:19:2b:6c"
                            },
                            new EspDeviceConfig
                            {
                                Name = "ESP32_Sin",
                                ApSsid = "ESP32_Sin_Streamer",
                                ApPassword = "12345678",
                                ApIp = "192.168.4.1",
                                Port = 8888,
                                HotspotIp = "192.168.137.173",
                                MacAddress = "cc:7b:5c:34:cc:f8"
                            }
                        }
                    };
                    SaveConfig();
                    Log($"Создан новый файл конфигурации: {ConfigFile}", "INFO");
                }
            }
            catch (Exception e)
            {
                Log($"Ошибка загрузки конфигурации: {e.Message}", "ERROR");
                _config = new Config();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
                Log("Конфигурация сохранена", "SUCCESS");
            }
            catch (Exception e)
            {
                Log($"Ошибка сохранения конфигурации: {e.Message}", "ERROR");
            }
        }

        // ============= STREAM WORKER CLASS =============
        class StreamWorker
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

        // ============= PLOTTING METHODS =============
        private void AddDataPoint(string deviceName, string data)
        {
            try
            {
                if (!_isFormLoaded) return;

                // Упрощенный парсинг - ищем последнее числовое значение в строке
                string[] parts = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Ищем последний элемент, который можно распарсить как double
                double value = 0;
                bool parsed = false;

                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    // Убираем возможные символы форматирования
                    string cleanPart = parts[i].TrimEnd(']', ')', '}', '>');
                    cleanPart = cleanPart.TrimStart('[', '(', '{', '<');

                    if (double.TryParse(cleanPart, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                    {
                        parsed = true;

                        // ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА: данные ESP32 обычно в диапазоне 0-200
                        // Если значение не в этом диапазоне, пропускаем его
                        if (value < 0 || value > 250)
                        {
                            Log($"Пропущено неверное значение: {value}", "WARN", deviceName);
                            return;
                        }

                        break;
                    }
                }

                if (parsed)
                {
                    // Преобразуем значения из диапазона 0-200 в -1.5..1.5
                    // ESP32 отправляет синусоиду в диапазоне 0-200
                    // Центр = 100, амплитуда = 100
                    double normalizedValue = (value - 100) / 100.0 * 1.5;

                    // Используем время относительно начала графика
                    TimeSpan elapsed = DateTime.Now - _plotStartTime;
                    double timestamp = elapsed.TotalSeconds;

                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() => UpdatePlotData(deviceName, timestamp, normalizedValue)));
                    }
                    else
                    {
                        UpdatePlotData(deviceName, timestamp, normalizedValue);
                    }

                    // Для отладки
                    Log($"Данные получены: исходное={value}, нормализованное={normalizedValue:F3}", "DATA", deviceName);
                }
                else
                {
                    Log($"Не удалось распарсить данные: {data}", "WARN", deviceName);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки данных: {ex.Message}", "ERROR", deviceName);
            }
        }

        private void UpdatePlotData(string deviceName, double timestamp, double value)
        {
            try
            {
                if (deviceName.Contains("Sin"))
                {
                    lock (sinData)
                    {
                        sinData.Add(new DataPoint(timestamp, value));
                        if (sinData.Count > MaxDataPoints)
                            sinData.RemoveAt(0);

                        sinSeries.Points.Clear();
                        sinSeries.Points.AddRange(sinData);
                    }

                    if (plotSin?.Model?.Axes != null && plotSin.Model.Axes.Count > 1)
                    {
                        double minTime = Math.Max(0, timestamp - _timeWindow);
                        double maxTime = Math.Max(timestamp + 1, minTime + 1); // Минимум 1 секунда окна

                        plotSin.Model.Axes[1].Minimum = minTime;
                        plotSin.Model.Axes[1].Maximum = maxTime;
                    }

                    // Автоматическое масштабирование по Y
                    AutoScaleYAxis(plotSin, sinData);

                    plotSin.InvalidatePlot(true);
                }
                else if (deviceName.Contains("Cos"))
                {
                    lock (cosData)
                    {
                        cosData.Add(new DataPoint(timestamp, value));
                        if (cosData.Count > MaxDataPoints)
                            cosData.RemoveAt(0);

                        cosSeries.Points.Clear();
                        cosSeries.Points.AddRange(cosData);
                    }

                    if (plotCos?.Model?.Axes != null && plotCos.Model.Axes.Count > 1)
                    {
                        double minTime = Math.Max(0, timestamp - _timeWindow);
                        double maxTime = Math.Max(timestamp + 1, minTime + 1); // Минимум 1 секунда окна

                        plotCos.Model.Axes[1].Minimum = minTime;
                        plotCos.Model.Axes[1].Maximum = maxTime;
                    }

                    // Автоматическое масштабирование по Y
                    AutoScaleYAxis(plotCos, cosData);

                    plotCos.InvalidatePlot(true);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обновления графика: {ex.Message}", "ERROR", deviceName);
            }
        }

        private void AutoScaleYAxis(PlotView plot, List<DataPoint> data)
        {
            if (plot?.Model?.Axes == null || plot.Model.Axes.Count == 0 || data.Count == 0)
                return;

            var yAxis = plot.Model.Axes[0] as LinearAxis;
            if (yAxis == null) return;

            // Находим мин и макс значения
            double minY = data.Min(p => p.Y);
            double maxY = data.Max(p => p.Y);

            // Добавляем небольшой запас (10%)
            double range = maxY - minY;
            double padding = range * 0.1;

            yAxis.Minimum = minY - padding;
            yAxis.Maximum = maxY + padding;
        }

        private void ClearPlots()
        {
            try
            {
                lock (sinData) sinData.Clear();
                lock (cosData) cosData.Clear();

                sinSeries.Points.Clear();
                cosSeries.Points.Clear();

                // Сбрасываем время начала графика
                _plotStartTime = DateTime.Now;

                // Сбрасываем оси к начальным значениям
                if (plotSin?.Model?.Axes != null && plotSin.Model.Axes.Count > 1)
                {
                    plotSin.Model.Axes[1].Minimum = 0;
                    plotSin.Model.Axes[1].Maximum = 20;
                    plotSin.Model.Axes[0].Minimum = -1.5;
                    plotSin.Model.Axes[0].Maximum = 1.5;
                }

                if (plotCos?.Model?.Axes != null && plotCos.Model.Axes.Count > 1)
                {
                    plotCos.Model.Axes[1].Minimum = 0;
                    plotCos.Model.Axes[1].Maximum = 20;
                    plotCos.Model.Axes[0].Minimum = -1.5;
                    plotCos.Model.Axes[0].Maximum = 1.5;
                }

                // Принудительно обновляем графики
                plotSin.InvalidatePlot(true);
                plotCos.InvalidatePlot(true);

                Log("Графики очищены", "SUCCESS");
            }
            catch (Exception ex)
            {
                Log($"Ошибка очистки графиков: {ex.Message}", "ERROR");
            }
        }

        // ============= UI BUTTON HANDLERS =============
        private void ConfigureSingleEsp()
        {
            if (_config.EspDevices.Count == 0)
            {
                MessageBox.Show("Нет настроенных устройств.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var dialog = new DeviceSelectionDialog(_config.EspDevices, "Выберите устройство для настройки:");
            if (dialog.ShowDialog() == DialogResult.OK && dialog.SelectedDevice != null)
            {
                var device = dialog.SelectedDevice;

                // Показываем инструкцию
                MessageBox.Show(
                    $"Убедитесь, что {device.Name} включен и светодиод мигает.\n" +
                    $"Для настройки необходимо подключиться к WiFi сети:\n" +
                    $"SSID: {device.ApSsid}\n" +
                    $"Пароль: {device.ApPassword}",
                    "Подготовка к настройке",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Task.Run(() => ConfigureEspDevice(device));
            }
        }

        private void ConfigureEspDevice(EspDeviceConfig device)
        {
            try
            {
                Log($"Начинаю настройку {device.Name}...", "INFO", device.Name);

                // Проверяем подключение к сети ESP
                bool connected = _networkManager.IsConnectedToNetwork(device.ApSsid);
                if (!connected)
                {
                    Log($"Не подключен к сети {device.ApSsid}. Пытаюсь подключиться...", "WARN", device.Name);

                    // Пытаемся подключиться
                    this.Invoke(new Action(() =>
                    {
                        var result = MessageBox.Show(
                            $"Необходимо подключиться к сети {device.ApSsid}\n" +
                            $"Автоматически подключиться к WiFi?",
                            "Подключение к WiFi",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            bool success = _networkManager.ConnectToEspNetwork(device.ApSsid, device.ApPassword);
                            if (success)
                            {
                                Thread.Sleep(3000);
                                SendWifiCredentialsToEsp(device);
                            }
                        }
                    }));
                }
                else
                {
                    Log($"Уже подключен к сети {device.ApSsid}", "INFO", device.Name);
                    Thread.Sleep(3000);
                    SendWifiCredentialsToEsp(device);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка настройки: {ex.Message}", "ERROR", device.Name);
            }
        }

        private void SendWifiCredentialsToEsp(EspDeviceConfig device)
        {
            try
            {
                Log($"Проверяю доступность {device.Name}...", "INFO", device.Name);

                // Проверяем доступность ESP
                if (!_networkManager.CheckEspAvailability(device.ApIp, device.Port, 3000))
                {
                    Log($"{device.Name} недоступен", "ERROR", device.Name);
                    return;
                }

                Log($"{device.Name} доступен", "SUCCESS", device.Name);

                // Отправляем данные WiFi
                Log($"Отправляю данные WiFi на {device.Name}...", "INFO", device.Name);

                bool success = _networkManager.SendWifiCredentialsToEsp(
                    device.ApIp,
                    device.Port,
                    _config.HotspotSsid,
                    _config.HotspotPassword,
                    device.Name);

                if (success)
                {
                    Log($"Данные WiFi отправлены. {device.Name} перезагружается...", "SUCCESS", device.Name);

                    this.Invoke(new Action(() =>
                    {
                        MessageBox.Show(
                            $"{device.Name} перезагружается и подключится к сети {_config.HotspotSsid}\n" +
                            $"Это займет 5-10 секунд.",
                            "Перезагрузка ESP",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }));

                    Thread.Sleep(7000);

                    FindAndSaveEspInNetwork(device);
                }
                else
                {
                    Log($"Не удалось отправить данные WiFi", "ERROR", device.Name);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}", "ERROR", device.Name);
            }
        }

        private bool FindAndSaveEspInNetwork(EspDeviceConfig device)
        {
            Log($"Поиск {device.Name} в домашней сети...", "INFO", device.Name);

            // 1. Проверяем сохраненный IP
            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                if (_networkManager.CheckEspAvailability(device.HotspotIp, device.Port, 1000))
                {
                    Log($"{device.Name} найден по сохраненному IP: {device.HotspotIp}", "SUCCESS", device.Name);
                    SaveConfig();
                    UpdateUI();
                    return true;
                }
            }

            // 2. Поиск по известным адресам
            string[] knownIps = {
                "192.168.137.102", "192.168.137.173",
                "192.168.137.1", "192.168.137.100",
                "192.168.1.102", "192.168.1.173",
                "192.168.0.102", "192.168.0.173"
            };

            foreach (string ip in knownIps)
            {
                if (_networkManager.CheckEspAvailability(ip, device.Port, 500))
                {
                    device.HotspotIp = ip;
                    Log($"{device.Name} найден: {ip}", "SUCCESS", device.Name);
                    SaveConfig();
                    UpdateUI();
                    return true;
                }
            }

            // 3. Сканирование подсети
            Log($"Сканирование подсети 192.168.137.*", "INFO", device.Name);

            for (int i = 1; i < 255; i++)
            {
                string testIp = $"192.168.137.{i}";
                if (_networkManager.CheckEspAvailability(testIp, device.Port, 100))
                {
                    device.HotspotIp = testIp;
                    Log($"{device.Name} найден: {testIp}", "SUCCESS", device.Name);
                    SaveConfig();
                    UpdateUI();
                    return true;
                }
            }

            Log($"{device.Name} не найден в сети", "WARN", device.Name);
            return false;
        }

        private void StreamFromSingleEsp()
        {
            if (_config.EspDevices.Count == 0)
            {
                MessageBox.Show("Нет настроенных устройств.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var dialog = new DeviceSelectionDialog(_config.EspDevices, "Выберите устройство для стриминга:");
            if (dialog.ShowDialog() == DialogResult.OK && dialog.SelectedDevice != null)
            {
                var device = dialog.SelectedDevice;

                // Проверяем доступность
                string ip = !string.IsNullOrEmpty(device.HotspotIp) ? device.HotspotIp : device.ApIp;

                if (_networkManager.CheckEspAvailability(ip, device.Port))
                {
                    StopAllStreamsMenu();

                    var worker = new StreamWorker(this, device, ip);
                    worker.DataReceived += (devName, data) =>
                    {
                        Log(data, "DATA", devName);
                        AddDataPoint(devName, data);
                        UpdateUI();
                    };

                    worker.Start();
                    Log($"Стриминг начат с {device.Name}", "SUCCESS", device.Name);
                }
                else
                {
                    MessageBox.Show($"{device.Name} недоступен по адресу {ip}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void StopSingleStream()
        {
            if (_config.EspDevices.Count == 0)
            {
                MessageBox.Show("Нет настроенных устройств.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Фильтруем только активные устройства
            var activeDevices = _config.EspDevices
                .Where(d => _activeWorkers.Any(w => w.Device.Name == d.Name))
                .ToList();

            if (activeDevices.Count == 0)
            {
                MessageBox.Show("Нет активных стримов для остановки.", "Информация",
                               MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var dialog = new DeviceSelectionDialog(activeDevices, "Выберите устройство для остановки стриминга:");
            if (dialog.ShowDialog() == DialogResult.OK && dialog.SelectedDevice != null)
            {
                var device = dialog.SelectedDevice;

                var worker = _activeWorkers.FirstOrDefault(w => w.Device.Name == device.Name);
                if (worker != null)
                {
                    worker.Stop();
                    Log($"Стриминг остановлен для {device.Name}", "SUCCESS", device.Name);
                }
                else
                {
                    MessageBox.Show($"Стриминг для {device.Name} не найден.", "Ошибка",
                                   MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void FindEspInHotspotNetwork()
        {
            Log("Поиск ESP в домашней сети...", "INFO");

            Task.Run(() =>
            {
                bool foundAny = false;
                foreach (var device in _config.EspDevices)
                {
                    if (FindAndSaveEspInNetwork(device))
                    {
                        foundAny = true;
                    }
                }

                if (foundAny)
                {
                    SaveConfig();
                    UpdateUI();
                    Log("Поиск завершен успешно", "SUCCESS");
                }
                else
                {
                    Log("ESP не найдены в сети", "ERROR");
                }
            });
        }

        private void ConfigureTwoEspParallel()
        {
            Log("Параллельная настройка двух ESP...", "INFO");

            if (_config.EspDevices.Count < 2)
            {
                MessageBox.Show("Требуется минимум 2 устройства.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                "ПАРАЛЛЕЛЬНАЯ НАСТРОЙКА ДВУХ ESP\n\n" +
                $"1. {_config.EspDevices[0].Name} -> {_config.EspDevices[0].ApSsid}\n" +
                $"2. {_config.EspDevices[1].Name} -> {_config.EspDevices[1].ApSsid}\n\n" +
                "Убедитесь, что ESP включены и мигают светодиоды.\n" +
                "Продолжить?",
                "Параллельная настройка",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                // Запрашиваем данные домашней сети, если не заданы
                if (string.IsNullOrEmpty(_config.HotspotSsid) || string.IsNullOrEmpty(_config.HotspotPassword))
                {
                    using (var form = new SimpleNetworkDialog(_config))
                    {
                        if (form.ShowDialog() == DialogResult.OK)
                        {
                            _config.HotspotSsid = form.Ssid;
                            _config.HotspotPassword = form.Password;
                            SaveConfig();
                        }
                        else
                        {
                            return; // Пользователь отменил
                        }
                    }
                }

                // Останавливаем все стримы перед настройкой
                StopAllStreamsMenu();

                // Настраиваем каждое устройство последовательно
                foreach (var device in _config.EspDevices)
                {
                    Log($"Настройка {device.Name}...", "INFO", device.Name);
                    ConfigureEspDevice(device);
                    Thread.Sleep(1000); // Небольшая задержка между настройками
                }

                Log("Параллельная настройка завершена", "INFO");
            }
        }

        private async void StreamFromTwoEspParallel()
        {
            if (_config.EspDevices.Count < 2)
            {
                MessageBox.Show("Требуется минимум 2 устройства.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log("Запуск параллельного стриминга...", "INFO");

            // Очищаем графики
            ClearPlots();

            StopAllStreamsMenu();

            bool allConnected = true;
            var workers = new List<StreamWorker>();

            // Проверяем доступность всех устройств
            foreach (var device in _config.EspDevices)
            {
                string ip = !string.IsNullOrEmpty(device.HotspotIp) ? device.HotspotIp : device.ApIp;

                if (!_networkManager.CheckEspAvailability(ip, device.Port, 2000))
                {
                    Log($"{device.Name} недоступен по адресу {ip}", "ERROR", device.Name);
                    allConnected = false;
                    break;
                }
            }

            if (!allConnected)
            {
                MessageBox.Show("Не все ESP доступны. Проверьте подключение.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Запускаем стриминг со всех устройств
            foreach (var device in _config.EspDevices)
            {
                string ip = !string.IsNullOrEmpty(device.HotspotIp) ? device.HotspotIp : device.ApIp;

                var worker = new StreamWorker(this, device, ip);
                worker.DataReceived += (devName, data) =>
                {
                    Log(data, "DATA", devName);
                    AddDataPoint(devName, data);
                    UpdateUI();
                };

                workers.Add(worker);
                worker.Start();
                await Task.Delay(500); // Небольшая задержка между запусками
            }

            Log("Параллельный стриминг запущен!", "SUCCESS");
            UpdateUI();
        }

        private void StopAllStreamsMenu()
        {
            Log("Останавливаю все стримы...", "INFO");

            lock (_activeWorkers)
            {
                foreach (var worker in _activeWorkers.ToList())
                {
                    worker.Stop();
                }
                _activeWorkers.Clear();
            }

            Log("Все стримы остановлены", "SUCCESS");
            UpdateUI();
        }
    }
}