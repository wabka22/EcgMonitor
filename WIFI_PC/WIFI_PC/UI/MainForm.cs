using System.Text.Json;
using System.Text;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ESP32StreamManager
{
    public partial class MainForm : Form
    {
        private const string ConfigFile = "config.json";
        public List<StreamWorker> _activeWorkers = new List<StreamWorker>();
        private Config _config;
        private NetworkManager _networkManager;

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

        private List<DataPoint> sinData = new List<DataPoint>();
        private List<DataPoint> cosData = new List<DataPoint>();
        private const int MaxDataPoints = 1250;
        private LineSeries sinSeries;
        private LineSeries cosSeries;
        private DateTime _plotStartTime;
        private double _timeWindow = 30.0;

        private List<string> _pendingLogs = new List<string>();
        private bool _isFormLoaded = false;
        private System.Windows.Forms.Timer _statusUpdateTimer;
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
            this.Size = new System.Drawing.Size(1400, 1000);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(28, 29, 33);

            _statusUpdateTimer = new System.Windows.Forms.Timer();
            _statusUpdateTimer.Interval = 5000; // 5 секунд
            _statusUpdateTimer.Tick += StatusUpdateTimer_Tick;

            panelTop = new Panel();
            panelTop.Dock = DockStyle.Top;
            panelTop.Height = 90;
            panelTop.BackColor = Color.FromArgb(40, 42, 46);

            lblStatus = new Label();
            lblStatus.ForeColor = Color.White;
            lblStatus.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblStatus.Text = "Статус: Готов к работе";
            lblStatus.Location = new Point(20, 12);
            lblStatus.Size = new Size(500, 25);

            lblSinStatus = new Label();
            lblSinStatus.ForeColor = Color.MediumAquamarine;
            lblSinStatus.Font = new Font("Segoe UI", 9);
            lblSinStatus.Text = "ESP32_Sin: Отключено";
            lblSinStatus.Location = new Point(20, 40);

            lblCosStatus = new Label();
            lblCosStatus.ForeColor = Color.MediumAquamarine;
            lblCosStatus.Font = new Font("Segoe UI", 9);
            lblCosStatus.Text = "ESP32_Cos: Отключено";
            lblCosStatus.Location = new Point(20, 60);

            panelTop.Controls.AddRange(new Control[] { lblStatus, lblSinStatus, lblCosStatus });

            panelCenter = new Panel();
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.BackColor = Color.FromArgb(32, 34, 38);
            panelCenter.Padding = new Padding(10);

            TableLayoutPanel tableLayout = new TableLayoutPanel();
            tableLayout.Dock = DockStyle.Fill;
            tableLayout.RowCount = 2;
            tableLayout.ColumnCount = 1;
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            plotSin = new PlotView();
            plotSin.Dock = DockStyle.Fill;
            plotSin.BackColor = Color.FromArgb(44, 46, 52);

            plotCos = new PlotView();
            plotCos.Dock = DockStyle.Fill;
            plotCos.BackColor = Color.FromArgb(44, 46, 52);

            tableLayout.Controls.Add(plotSin, 0, 0);
            tableLayout.Controls.Add(plotCos, 0, 1);
            panelCenter.Controls.Add(tableLayout);

            panelBottom = new Panel();
            panelBottom.Dock = DockStyle.Bottom;
            panelBottom.Height = 350;
            panelBottom.BackColor = Color.FromArgb(36, 38, 42);

            controlPanel = new Panel();
            controlPanel.Dock = DockStyle.Left;
            controlPanel.Width = 320;
            controlPanel.BackColor = Color.FromArgb(48, 50, 55);
            controlPanel.Padding = new Padding(10);
            controlPanel.AutoScroll = true;


            Label lblSingle = new Label()
            {
                Text = "ОДИНОЧНЫЕ ОПЕРАЦИИ",
                ForeColor = Color.MediumTurquoise,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 10),
                Size = new Size(280, 20)
            };

            btnConnectSingle = CreateButton("Настроить ESP", 10, 40);
            btnConnectSingle.Click += (s, e) => ConfigureSingleEsp();

            btnStreamSingle = CreateButton("Стриминг с одной ESP", 10, 75);
            btnStreamSingle.Click += (s, e) => StreamFromSingleEsp();

            btnStopSingle = CreateButton("Остановить стриминг", 10, 110);
            btnStopSingle.Click += (s, e) => StopSingleStream();
            btnStopSingle.BackColor = Color.FromArgb(90, 0, 0);

            btnFindESP = CreateButton("Найти ESP в сети", 10, 145);
            btnFindESP.Click += (s, e) => FindEspInHotspotNetwork();

            Label lblParallel = new Label()
            {
                Text = "ПАРАЛЛЕЛЬНЫЕ ОПЕРАЦИИ",
                ForeColor = Color.MediumTurquoise,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 185),
                Size = new Size(280, 20)
            };

            btnParallelConfig = CreateButton("Настройка двух ESP", 10, 215);
            btnParallelConfig.Click += (s, e) => ConfigureTwoEspParallel();

            btnParallelStream = CreateButton("Параллельный стриминг", 10, 250);
            btnParallelStream.Click += (s, e) => StreamFromTwoEspParallel();

            Label lblUtility = new Label()
            {
                Text = "УТИЛИТЫ",
                ForeColor = Color.MediumTurquoise,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 290),
                Size = new Size(280, 20)
            };

            btnStopAll = CreateButton("Остановить все стримы", 10, 320);
            btnStopAll.Click += (s, e) => StopAllStreamsMenu();

            btnClearPlots = CreateButton("Очистить графики", 10, 355);
            btnClearPlots.Click += (s, e) => ClearPlots();
            btnClearPlots.BackColor = Color.FromArgb(80, 80, 0);


            btnExit = CreateButton("Выход", 10, 395);
            btnExit.BackColor = Color.FromArgb(80, 0, 0);
            btnExit.Click += (s, e) => Close();

            controlPanel.Controls.AddRange(new Control[] {
        lblSingle, btnConnectSingle, btnStreamSingle, btnStopSingle, btnFindESP,
        lblParallel, btnParallelConfig, btnParallelStream,
        lblUtility, btnStopAll, btnClearPlots, btnDiagnose, btnExit
    });

            Panel logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(5);

            txtLog = new RichTextBox();
            txtLog.Multiline = true;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.FromArgb(30, 31, 34);
            txtLog.ForeColor = Color.White;
            txtLog.Font = new Font("Consolas", 9);
            txtLog.ReadOnly = true;

            logPanel.Controls.Add(txtLog);

            panelBottom.Controls.Add(logPanel);
            panelBottom.Controls.Add(controlPanel);

            Controls.Add(panelCenter);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);

            Load += MainForm_Load;
        }

        private void StatusUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!_isFormLoaded) return;

            // Запускаем в фоновом потоке, чтобы не блокировать UI
            Task.Run(() => CheckEspStatuses());
        }

        private void CheckEspStatuses()
        {
            try
            {
                // Проверка на null
                if (_networkManager == null || _config?.EspDevices == null) return;

                foreach (var device in _config.EspDevices)
                {
                    bool isStreaming = _activeWorkers.Any(w => w.Device.Name == device.Name);

                    if (!isStreaming && !string.IsNullOrEmpty(device.HotspotIp))
                    {
                        bool isAvailable = _networkManager.CheckEspAvailability(
                            device.HotspotIp,
                            device.Port,
                            1000); 

                        this.Invoke(new Action(() =>
                        {
                            UpdateDeviceStatusLabel(device, isAvailable, isStreaming);
                        }));
                    }
                    else if (isStreaming)
                    {
                        this.Invoke(new Action(() =>
                        {
                            UpdateDeviceStatusLabel(device, true, true);
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке статусов: {ex.Message}", "ERROR");
            }
        }

        private void UpdateDeviceStatusLabel(EspDeviceConfig device, bool isAvailable, bool isStreaming)
        {
            if (device.Name.Contains("Sin"))
            {
                lblSinStatus.Text = $"ESP32_Sin: {(isStreaming ? "Стриминг" : (isAvailable ? "Доступен" : "Отключено"))} | IP: {device.HotspotIp ?? "Нет IP"}";
                lblSinStatus.ForeColor = isStreaming ? Color.Yellow : (isAvailable ? Color.LightGreen : Color.LightGray);
            }
            else if (device.Name.Contains("Cos"))
            {
                lblCosStatus.Text = $"ESP32_Cos: {(isStreaming ? "Стриминг" : (isAvailable ? "Доступен" : "Отключено"))} | IP: {device.HotspotIp ?? "Нет IP"}";
                lblCosStatus.ForeColor = isStreaming ? Color.Yellow : (isAvailable ? Color.LightGreen : Color.LightGray);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _isFormLoaded = true;
            _networkManager = new NetworkManager(this);
            _plotStartTime = DateTime.Now;
            _statusUpdateTimer.Start();

            if (_pendingLogs.Count > 0)
            {
                foreach (var log in _pendingLogs)
                {
                    AddLogToTextBox(log, Color.White);
                }
                _pendingLogs.Clear();
            }

            UpdateUI();

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

            if (plotSin.Model != null)
            {
                bool sinStreaming = _activeWorkers.Any(w => w.Device.Name.Contains("Sin"));
                plotSin.Model.Title = sinStreaming ?
                    "ESP32_Sin - АКТИВНЫЙ СТРИМИНГ" :
                    "ESP32_ECG1 - Сигнал ЭКГ (AD8232)";
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

        private class LowPassFilter
        {
            public double Prev;
            public bool HasPrev;
        }

        private readonly Dictionary<string, LowPassFilter> _filters
    = new Dictionary<string, LowPassFilter>();

        private readonly Dictionary<string, double> _prevStage2 = new Dictionary<string, double>();

        private double ApplyDoubleLowPass(string deviceName, double value)
        {
            double alpha1 = deviceName.Contains("Sin") ? 0.12 : 0.08;
            if (!_filters.TryGetValue(deviceName, out var filter))
            {
                filter = new LowPassFilter();
                _filters[deviceName] = filter;
            }

            if (!filter.HasPrev)
            {
                filter.Prev = value;
                filter.HasPrev = true;
            }
            else
            {
                filter.Prev = filter.Prev + alpha1 * (value - filter.Prev);
            }

            double stage1 = filter.Prev;

            double alpha2 = deviceName.Contains("Sin") ? 0.25 : 0.2;

            if (!_prevStage2.TryGetValue(deviceName, out double stage2Prev))
                stage2Prev = stage1;

            double stage2 = stage2Prev + alpha2 * (stage1 - stage2Prev);
            _prevStage2[deviceName] = stage2;

            return stage2;
        }

        private void AddDataPoint(string deviceName, string data)
        {
            try
            {
                if (!_isFormLoaded)
                    return;

                string s = data.Trim().Replace(',', '.');

                if (!double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double value))
                {
                    Log($"Не удалось распарсить: {data}", "WARN", deviceName);
                    return;
                }

                if (value < 0 || value > 4095)
                {
                    Log($"Значение вне диапазона АЦП: {value}", "WARN", deviceName);
                    return;
                }

                double filteredValue = value;

                double timestamp =
                    (DateTime.Now - _plotStartTime).TotalSeconds;

                if (InvokeRequired)
                {
                    Invoke(new Action(() =>
                        UpdatePlotData(deviceName, timestamp, filteredValue)));
                }
                else
                {
                    UpdatePlotData(deviceName, timestamp, filteredValue);
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
                        double minTime = timestamp - _timeWindow; 
                        double maxTime = timestamp;
                        if (minTime < 0) minTime = 0;
                        plotSin.Model.Axes[1].Minimum = minTime;
                        plotSin.Model.Axes[1].Maximum = maxTime;
                    }


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
                        double maxTime = Math.Max(timestamp + 1, minTime + 1);

                        plotCos.Model.Axes[1].Minimum = minTime;
                        plotCos.Model.Axes[1].Maximum = maxTime;
                    }

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

            double minY = data.Min(p => p.Y);
            double maxY = data.Max(p => p.Y);

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

                _plotStartTime = DateTime.Now;

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

                bool connected = _networkManager.IsConnectedToNetwork(device.ApSsid);
                if (!connected)
                {
                    Log($"Не подключен к сети {device.ApSsid}. Пытаюсь подключиться...", "WARN", device.Name);

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

                if (!_networkManager.CheckEspAvailability(device.ApIp, device.Port, 3000))
                {
                    Log($"{device.Name} недоступен", "ERROR", device.Name);
                    return;
                }

                Log($"{device.Name} доступен", "SUCCESS", device.Name);

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

        private List<string> GetHotspotClientsIps()
        {
            var ips = new List<string>();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var regex = new Regex(@"\b192\.168\.137\.\d+\b");

            foreach (Match m in regex.Matches(output))
                ips.Add(m.Value);

            return ips.Distinct().ToList();
        }

        private bool FindAndSaveEspInNetwork(EspDeviceConfig device)
        {
            int deviceIndex = _config.EspDevices.IndexOf(device);
            string deviceType = deviceIndex == 0 ? "Cos" : "Sin";

            Log($"Поиск {device.Name} ({deviceType}) в домашней сети...", "INFO", device.Name);

            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                if (_networkManager.CheckEspAvailability(device.HotspotIp, device.Port, 1000))
                {
                    Log($"{device.Name} найден по сохраненному IP: {device.HotspotIp}", "SUCCESS", device.Name);
                    return true;
                }
            }

            Log("Получение списка устройств хот-спота...", "INFO", device.Name);

            var hotspotIps = GetHotspotClientsIps();

            foreach (var ip in hotspotIps)
            {
                if (_networkManager.CheckEspAvailability(ip, device.Port, 500))
                {
                    device.HotspotIp = ip;
                    Log($"{device.Name} найден через хот-спот: {ip}", "SUCCESS", device.Name);
                    SaveConfig();
                    UpdateUI();
                    return true;
                }
            }

            Log("ESP не найден в хот-споте, fallback-скан...", "WARN", device.Name);

            for (int i = 1; i <= 254; i++)
            {
                string testIp = $"192.168.137.{i}";
                if (_networkManager.CheckEspAvailability(testIp, device.Port, 100))
                {
                    device.HotspotIp = testIp;
                    Log($"{device.Name} найден fallback-сканом: {testIp}", "SUCCESS", device.Name);
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
                            return;
                        }
                    }
                }

                StopAllStreamsMenu();

                foreach (var device in _config.EspDevices)
                {
                    Log($"Настройка {device.Name}...", "INFO", device.Name);
                    ConfigureEspDevice(device);
                    Thread.Sleep(1000);
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

            ClearPlots();

            StopAllStreamsMenu();

            bool allConnected = true;
            var workers = new List<StreamWorker>();

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
                await Task.Delay(500);
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