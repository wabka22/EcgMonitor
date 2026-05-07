using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using NumSharp;
using ESP32StreamManager.ML;

namespace ESP32StreamManager
{
    public partial class MainForm : Form
    {
        private const string ConfigFile = "config.json";

        public List<StreamWorker> _activeWorkers = new();

        private Config _config;
        private NetworkManager _networkManager;

        private Panel panelTop;
        private Panel panelMain;
        private Panel panelBottom;
        private Panel controlPanel;

        private PlotView plotEcg;

        private Button btnConfigureEsp;
        private Button btnStartStream;
        private Button btnStopStream;
        private Button btnFindEsp;
        private Button btnClearPlot;
        private Button btnTogglePrediction;
        private Button btnOpenConfig;
        private Button btnHotspot;
        private Button btnSaveRecord;

        private Label lblTitle;
        private Label lblStatus;

        private Label lblDeviceValue;
        private Label lblIpValue;
        private Label lblQualityValue;
        private Label lblRecordValue;

        private EcgSegmenter _segmenter;
        private EcgPredictionState _predictionState;

        private bool _predictionEnabled = false;

        private ListBox listLog;

        private readonly List<DataPoint> ecgData = new();

        private const int MaxDataPoints = 3000;
        private LineSeries ecgSeries;

        private ScatterSeries qrsSeries;
        private ScatterSeries spikeSeries;
        private ScatterSeries qrsAfterSpikeSeries;

        private SegmentType _lastDetectedType = SegmentType.Background;

        private DateTime _plotStartTime;
        private DateTime? _recordingStartTime = null;

        private int _receivedPoints = 0;
        private double _timeWindow = 30.0;

        private readonly List<string> _pendingLogs = new();
        private bool _isFormLoaded = false;

        private System.Windows.Forms.Timer _statusUpdateTimer;

        public MainForm()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            InitializeComponent();
            LoadConfig();
            SetupPlots();
            LoadModel();
        }

        private void LoadModel()
        {
            string modelPath = Path.Combine("ML", "best_model.onnx");

            if (File.Exists(modelPath))
            {
                _segmenter = new EcgSegmenter(modelPath);
                Log("Нейросетевая модель загружена", "SUCCESS");
            }
            else
            {
                Log("Модель best_model.onnx не найдена", "WARN");
            }
        }

        private void InitializeComponent()
        {
            Text = "Система регистрации биосигналов";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            BackColor = AppTheme.MainBackColor;
            MinimumSize = new Size(1200, 760);

            _statusUpdateTimer = new System.Windows.Forms.Timer();
            _statusUpdateTimer.Interval = 5000;
            _statusUpdateTimer.Tick += StatusUpdateTimer_Tick;
            
            if (File.Exists("app.ico"))
            {
                Icon = new Icon("app.ico");
            }

            panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 135,
                BackColor = AppTheme.MainBackColor,
                Padding = new Padding(20)
            };

            var topLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = AppTheme.MainBackColor
            };

            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));

            var titleCard = UiFactory.CreateRoundedPanel(
                20,
                new Padding(20),
                new Padding(0, 0, 14, 0));

            lblTitle = new Label
            {
                Text = "ECG Stream Monitor",
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 42
            };

            lblStatus = new Label
            {
                Text = "Система регистрации биосигналов",
                ForeColor = AppTheme.MutedTextColor,
                Font = new Font("Segoe UI", 11),
                Dock = DockStyle.Top,
                Height = 30
            };

            titleCard.Controls.Add(lblStatus);
            titleCard.Controls.Add(lblTitle);

            var deviceCard = UiFactory.CreateStatusCard("Устройство", "Не проверено", out lblDeviceValue);
            var ipCard = UiFactory.CreateStatusCard("IP-адрес", "Не задан", out lblIpValue);
            var qualityCard = UiFactory.CreateStatusCard("Качество", "Не определено", out lblQualityValue);
            var recordCard = UiFactory.CreateStatusCard("Запись", "00:00 | 0 точек", out lblRecordValue);

            topLayout.Controls.Add(titleCard, 0, 0);
            topLayout.Controls.Add(deviceCard, 1, 0);
            topLayout.Controls.Add(ipCard, 2, 0);
            topLayout.Controls.Add(qualityCard, 3, 0);
            topLayout.Controls.Add(recordCard, 4, 0);

            panelTop.Controls.Add(topLayout);

            panelMain = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.MainBackColor,
                Padding = new Padding(20)
            };

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.MainBackColor
            };

            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            var graphCard = UiFactory.CreateRoundedPanel(
                22,
                new Padding(14),
                new Padding(0, 0, 15, 0));

            plotEcg = new PlotView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            graphCard.Controls.Add(plotEcg);

            controlPanel = UiFactory.CreateRoundedPanel(
                22,
                new Padding(24),
                new Padding(0));

            var controlsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 12,
                BackColor = AppTheme.PanelBackColor
            };

            controlsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            controlsLayout.RowStyles.Clear();

            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); // Заголовок
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Подсказка
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Найти
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Wi-Fi
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Хот-спот
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); // Старт
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); // Стоп
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Очистить
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Сохранить
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Предсказания
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); // Настройки
            controlsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblControl = new Label
            {
                Text = "Управление",
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblHint = new Label
            {
                Text = "Пошаговое подключение устройства и регистрация сигнала.",
                ForeColor = AppTheme.MutedTextColor,
                Font = new Font("Segoe UI", 11),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            };

            btnFindEsp = UiFactory.CreateSecondaryButton("🔍  Найти устройство в сети");
            btnFindEsp.Click += (s, e) => FindEspInHotspotNetwork();

            btnConfigureEsp = UiFactory.CreateSecondaryButton("📶  Настроить Wi-Fi  ");
            btnConfigureEsp.Click += (s, e) => ConfigureSingleEsp();

            btnHotspot = UiFactory.CreateSecondaryButton("📡  Хот-спот Windows");
            btnHotspot.Click += (s, e) => OpenMobileHotspotSettings();

            btnStartStream = UiFactory.CreatePrimaryButton("▶  НАЧАТЬ ЗАПИСЬ");
            btnStartStream.Click += (s, e) => StreamFromSingleEsp();

            btnStopStream = UiFactory.CreateDangerButton("■  СТОП");
            btnStopStream.Click += (s, e) => StopSingleStream();

            btnClearPlot = UiFactory.CreateSecondaryButton("🧹  Очистить график");
            btnClearPlot.Click += (s, e) => ClearPlot();

            btnSaveRecord = UiFactory.CreateSecondaryButton("💾  Сохранить запись");
            btnSaveRecord.Click += (s, e) => SaveRecordingToCsv();

            btnTogglePrediction = UiFactory.CreateSecondaryButton("🧠  Предсказания: ВЫКЛ");
            btnTogglePrediction.Click += (s, e) => TogglePrediction();

            btnOpenConfig = UiFactory.CreateSecondaryButton("⚙  Настройки");
            btnOpenConfig.Click += (s, e) => OpenConfigEditor();

            controlsLayout.Controls.Add(lblControl, 0, 0);
            controlsLayout.Controls.Add(lblHint, 0, 1);
            controlsLayout.Controls.Add(btnFindEsp, 0, 2);
            controlsLayout.Controls.Add(btnConfigureEsp, 0, 3);
            controlsLayout.Controls.Add(btnHotspot, 0, 4);
            controlsLayout.Controls.Add(btnStartStream, 0, 5);
            controlsLayout.Controls.Add(btnStopStream, 0, 6);
            controlsLayout.Controls.Add(btnClearPlot, 0, 7);
            controlsLayout.Controls.Add(btnSaveRecord, 0, 8);
            controlsLayout.Controls.Add(btnTogglePrediction, 0, 9);
            controlsLayout.Controls.Add(btnOpenConfig, 0, 10);

            controlPanel.Controls.Add(controlsLayout);

            mainLayout.Controls.Add(graphCard, 0, 0);
            mainLayout.Controls.Add(controlPanel, 1, 0);

            panelMain.Controls.Add(mainLayout);

            panelBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 155,
                BackColor = AppTheme.MainBackColor,
                Padding = new Padding(20, 0, 20, 20)
            };

            var logPanel = UiFactory.CreateRoundedPanel(
                20,
                new Padding(14),
                new Padding(0));

            var lblLog = new Label
            {
                Text = "Журнал событий",
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32
            };

            listLog = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 26
            };

            listLog.DrawItem += ListLog_DrawItem;

            logPanel.Controls.Add(listLog);
            logPanel.Controls.Add(lblLog);

            panelBottom.Controls.Add(logPanel);

            Controls.Add(panelMain);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);

            Load += MainForm_Load;
        }

        private void SetupPlots()
        {
            var model = new PlotModel
            {
                Title = "Сигнал ЭКГ",
                TitleColor = OxyColor.FromRgb(30, 41, 59),
                TitleFontSize = 16,
                Background = OxyColors.White,
                PlotAreaBackground = OxyColor.FromRgb(250, 252, 255),
                PlotAreaBorderColor = OxyColor.FromRgb(203, 213, 225),
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = OxyColor.FromRgb(51, 65, 85)
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Амплитуда АЦП",
                TextColor = OxyColor.FromRgb(51, 65, 85),
                TitleColor = OxyColor.FromRgb(51, 65, 85),
                TicklineColor = OxyColor.FromRgb(148, 163, 184),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromRgb(226, 232, 240),
                MinorGridlineColor = OxyColor.FromRgb(241, 245, 249),
                Minimum = -2500,
                Maximum = 2500
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Время, с",
                TextColor = OxyColor.FromRgb(51, 65, 85),
                TitleColor = OxyColor.FromRgb(51, 65, 85),
                TicklineColor = OxyColor.FromRgb(148, 163, 184),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromRgb(226, 232, 240),
                MinorGridlineColor = OxyColor.FromRgb(241, 245, 249),
                Minimum = 0,
                Maximum = 30
            });

            ecgSeries = new LineSeries
            {
                Title = "ECG",
                Color = OxyColor.FromRgb(37, 99, 235),
                StrokeThickness = 2
            };

            qrsSeries = new ScatterSeries
            {
                Title = "QRS",
                MarkerType = MarkerType.Circle,
                MarkerSize = 4,
                MarkerFill = OxyColor.FromRgb(37, 99, 235)
            };

            spikeSeries = new ScatterSeries
            {
                Title = "SPIKE",
                MarkerType = MarkerType.Diamond,
                MarkerSize = 5,
                MarkerFill = OxyColor.FromRgb(220, 38, 38)
            };

            qrsAfterSpikeSeries = new ScatterSeries
            {
                Title = "QRS after spike",
                MarkerType = MarkerType.Triangle,
                MarkerSize = 5,
                MarkerFill = OxyColor.FromRgb(147, 51, 234)
            };

            model.Series.Add(ecgSeries);
            model.Series.Add(qrsSeries);
            model.Series.Add(spikeSeries);
            model.Series.Add(qrsAfterSpikeSeries);

            plotEcg.Model = model;
            _predictionState = new EcgPredictionState(model, sampleRate: 500.0);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _isFormLoaded = true;
            _networkManager = new NetworkManager(this);
            _plotStartTime = DateTime.Now;
            _statusUpdateTimer.Start();

            foreach (var log in _pendingLogs)
                AddLogToTextBox(log, AppTheme.TextColor);

            _pendingLogs.Clear();

            UpdateUI();
            Task.Run(() => CheckEspOnStartup());
        }

        private void StatusUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!_isFormLoaded) return;
            Task.Run(() => CheckEspStatuses());
        }

        private EspDevice GetMainDevice()
        {
            return _config?.EspDevices?.FirstOrDefault();
        }

        private void CheckEspStatuses()
        {
            try
            {
                if (_networkManager == null || _config?.EspDevices == null)
                    return;

                var device = GetMainDevice();
                if (device == null)
                    return;

                bool isStreaming = _activeWorkers.Any(w => w.Device.Name == device.Name);
                bool isAvailable = false;

                if (!string.IsNullOrEmpty(device.HotspotIp))
                {
                    isAvailable = _networkManager.CheckEspAvailability(
                        device.HotspotIp,
                        device.Port,
                        1000);
                }

                Invoke(new Action(() =>
                {
                    UpdateDeviceStatusLabel(device, isAvailable, isStreaming);
                }));
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке статуса: {ex.Message}", "ERROR");
            }
        }

        private void UpdateDeviceStatusLabel(EspDevice device, bool isAvailable, bool isStreaming)
        {
            if (isStreaming)
            {
                lblDeviceValue.Text = "Регистрация";
                lblDeviceValue.ForeColor = AppTheme.AccentColor;
            }
            else if (isAvailable)
            {
                lblDeviceValue.Text = "Доступно";
                lblDeviceValue.ForeColor = AppTheme.SuccessColor;
            }
            else
            {
                lblDeviceValue.Text = "Недоступно";
                lblDeviceValue.ForeColor = AppTheme.DangerColor;
            }

            lblIpValue.Text = device.HotspotIp ?? "Не задан";
        }

        public void UpdateUI()
        {
            if (!_isFormLoaded) return;

            if (InvokeRequired)
            {
                Invoke(new Action(UpdateUI));
                return;
            }

            var device = GetMainDevice();
            bool isStreaming = device != null && _activeWorkers.Any(w => w.Device.Name == device.Name);

            lblStatus.Text = isStreaming
                ? "Регистрация сигнала активна"
                : "Система регистрации биосигналов";

            btnStartStream.Enabled = !isStreaming;
            btnStopStream.Enabled = isStreaming;

            btnStartStream.Text = ecgData.Count > 0
        ? "▶  ПРОДОЛЖИТЬ ЗАПИСЬ"
        : "▶  НАЧАТЬ ЗАПИСЬ";

            btnSaveRecord.Enabled = ecgData.Count > 0;

            if (device != null)
                lblIpValue.Text = device.HotspotIp ?? "Не задан";

            if (plotEcg?.Model != null)
            {
                plotEcg.Model.Title = isStreaming
                    ? "Сигнал ЭКГ — активная регистрация"
                    : "Сигнал ЭКГ";

                plotEcg.InvalidatePlot(true);
            }
        }

        public void Log(string msg, string level = "INFO", string deviceTag = "")
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string tag = string.IsNullOrEmpty(deviceTag) ? "" : $"[{deviceTag}] ";

            string icon = level switch
            {
                "SUCCESS" => "✓",
                "ERROR" => "!",
                "WARN" => "⚠",
                "INFO" => "i",
                "DATA" => "•",
                _ => "•"
            };

            string logMsg = $"{icon}  {ts}  {tag}{msg}";

            Color color = level switch
            {
                "INFO" => AppTheme.AccentColor,
                "SUCCESS" => AppTheme.SuccessColor,
                "WARN" => Color.FromArgb(202, 138, 4),
                "ERROR" => AppTheme.DangerColor,
                "DIAG" => AppTheme.MutedTextColor,
                "DATA" => AppTheme.TextColor,
                _ => AppTheme.TextColor
            };

            if (!_isFormLoaded)
            {
                _pendingLogs.Add(logMsg);
                return;
            }

            if (InvokeRequired)
                Invoke(new Action(() => AddLogToTextBox(logMsg, color)));
            else
                AddLogToTextBox(logMsg, color);

            if (level == "ERROR" || level == "SUCCESS")
                UpdateUI();
        }

        private void AddLogToTextBox(string logMsg, Color color)
        {
            try
            {
                listLog.Items.Add(logMsg);

                if (listLog.Items.Count > 200)
                    listLog.Items.RemoveAt(0);

                listLog.TopIndex = listLog.Items.Count - 1;
            }
            catch { }
        }

        private void ListLog_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            string text = listLog.Items[e.Index].ToString() ?? "";

            Color color = AppTheme.TextColor;

            if (text.StartsWith("✓"))
                color = AppTheme.SuccessColor;
            else if (text.StartsWith("!"))
                color = AppTheme.DangerColor;
            else if (text.StartsWith("⚠"))
                color = Color.FromArgb(202, 138, 4);
            else if (text.StartsWith("i"))
                color = AppTheme.AccentColor;

            using var brush = new SolidBrush(color);

            e.Graphics.DrawString(
                text,
                e.Font ?? listLog.Font,
                brush,
                e.Bounds.Left + 8,
                e.Bounds.Top + 4);

            e.DrawFocusRectangle();
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
                        HotspotPassword = "mypassword123",
                        EspDevices = new List<EspDevice>
                        {
                            new EspDevice
                            {
                                Name = "ESP32_ECG",
                                ApSsid = "ESP32_ECG_Streamer",
                                ApPassword = "12345678",
                                ApIp = "192.168.4.1",
                                Port = 8888,
                                HotspotIp = "192.168.137.191",
                                MacAddress = "c4:de:e2:19:2b:6c"
                            }
                        }
                    };

                    SaveConfig();
                    Log($"Создан новый файл конфигурации: {ConfigFile}", "INFO");
                }

                _config.EspDevices ??= new List<EspDevice>();

                if (_config.EspDevices.Count == 0)
                {
                    _config.EspDevices.Add(new EspDevice
                    {
                        Name = "ESP32_ECG",
                        ApSsid = "ESP32_ECG_Streamer",
                        ApPassword = "12345678",
                        ApIp = "192.168.4.1",
                        Port = 8888,
                        HotspotIp = "",
                        MacAddress = ""
                    });

                    SaveConfig();
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
                string json = JsonSerializer.Serialize(
                    _config,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(ConfigFile, json);
                Log("Конфигурация сохранена", "SUCCESS");
            }
            catch (Exception e)
            {
                Log($"Ошибка сохранения конфигурации: {e.Message}", "ERROR");
            }
        }

        private void CheckEspOnStartup()
        {
            Log("Проверка доступности устройства при запуске...", "INFO");

            var device = GetMainDevice();
            if (device == null)
            {
                Log("Устройство не задано в конфигурации", "ERROR");
                return;
            }

            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                bool isAvailable = _networkManager.CheckEspAvailability(
                    device.HotspotIp,
                    device.Port,
                    1000);

                Log(
                    $"{device.Name} ({device.HotspotIp}): {(isAvailable ? "доступно" : "недоступно")}",
                    isAvailable ? "SUCCESS" : "WARN",
                    device.Name);

                Invoke(new Action(() =>
                {
                    UpdateDeviceStatusLabel(device, isAvailable, false);
                }));
            }
            else
            {
                Log("IP устройства не задан. Выполните поиск устройства.", "WARN");
            }

            UpdateUI();
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
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value))
                {
                    Log($"Не удалось распарсить значение: {data}", "WARN", deviceName);
                    return;
                }

                if (value < -6000 || value > 6000)
                {
                    Log($"Значение вне диапазона АЦП: {value}", "WARN", deviceName);
                    return;
                }

                double timestamp = (DateTime.Now - _plotStartTime).TotalSeconds;

                EcgPrediction prediction = null;

                if (_predictionEnabled && _segmenter != null)
                {
                    if (_segmenter.AddSample((float)value, out var currentPrediction))
                    {
                        prediction = currentPrediction;
                        Log($"ML: {prediction.Type}, p={prediction.Probability:0.000}", "INFO", deviceName);
                    }
                }

                if (InvokeRequired)
                    Invoke(new Action(() => UpdatePlotData(deviceName, timestamp, value, prediction)));
                else
                    UpdatePlotData(deviceName, timestamp, value, prediction);
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки данных: {ex.Message}", "ERROR", deviceName);
            }
        }

        private void UpdatePlotData(string deviceName, double timestamp,double value, EcgPrediction prediction)
        {
            try
            {
                lock (ecgData)
                {
                    ecgData.Add(new DataPoint(timestamp, value));

                    if (ecgData.Count > MaxDataPoints)
                        ecgData.RemoveAt(0);

                    ecgSeries.Points.Clear();
                    ecgSeries.Points.AddRange(ecgData);
                }

                if (prediction != null)
                {
                    AddPredictionPoint(timestamp, value, prediction);
                }

                TrimPredictionSeries(timestamp);

                _receivedPoints++;

                UpdateRecordingInfo();
                UpdateSignalQuality();

                if (plotEcg?.Model?.Axes != null && plotEcg.Model.Axes.Count > 1)
                {
                    double minTime = Math.Max(0, timestamp - _timeWindow);
                    double maxTime = Math.Max(timestamp + 1, minTime + 1);

                    plotEcg.Model.Axes[1].Minimum = minTime;
                    plotEcg.Model.Axes[1].Maximum = maxTime;
                }

                AutoScaleYAxis(plotEcg, ecgData);
                plotEcg.InvalidatePlot(true);
            }
            catch (Exception ex)
            {
                Log($"Ошибка обновления графика: {ex.Message}", "ERROR", deviceName);
            }
        }

        private void AddPredictionPoint(double timestamp, double value, EcgPrediction prediction)
        {
            if (prediction.Type == SegmentType.Background)
                return;

            if (prediction.Probability < 0.55f)
                return;

            var type = _predictionState.Update(timestamp, prediction);

            lblStatus.Text = type switch
            {
                SegmentType.Qrs => $"Распознано: QRS | {prediction.Probability:0.00}",
                SegmentType.Spike => $"Распознано: SPIKE | {prediction.Probability:0.00}",
                SegmentType.QrsAfterSpike => $"Распознано: QRS after spike | {prediction.Probability:0.00}",
                _ => lblStatus.Text
            };
        }

        private void TrimPredictionSeries(double timestamp)
        {
            double minTime = Math.Max(0, timestamp - _timeWindow);

            RemoveOldPoints(qrsSeries, minTime);
            RemoveOldPoints(spikeSeries, minTime);
            RemoveOldPoints(qrsAfterSpikeSeries, minTime);

            _predictionState?.Trim(minTime);
        }
        private void RemoveOldPoints(ScatterSeries series, double minTime)
        {
            while (series.Points.Count > 0 && series.Points[0].X < minTime)
            {
                series.Points.RemoveAt(0);
            }
        }

        private void UpdateRecordingInfo()
        {
            if (_recordingStartTime == null)
            {
                lblRecordValue.Text = $"00:00 | {_receivedPoints} точек";
                return;
            }

            TimeSpan elapsed = DateTime.Now - _recordingStartTime.Value;
            lblRecordValue.Text = $"{elapsed:mm\\:ss} | {_receivedPoints} точек";
        }

        private void UpdateSignalQuality()
        {
            if (ecgData.Count < 30)
            {
                lblQualityValue.Text = "Анализ...";
                lblQualityValue.ForeColor = AppTheme.MutedTextColor;
                return;
            }

            var lastPoints = ecgData
                .TakeLast(100)
                .Select(p => p.Y)
                .ToList();

            double min = lastPoints.Min();
            double max = lastPoints.Max();
            double range = max - min;

            if (range < 5)
            {
                lblQualityValue.Text = "Слабый";
                lblQualityValue.ForeColor = Color.FromArgb(202, 138, 4);
            }
            else if (range > 3000)
            {
                lblQualityValue.Text = "Помехи";
                lblQualityValue.ForeColor = AppTheme.DangerColor;
            }
            else
            {
                lblQualityValue.Text = "Хорошее";
                lblQualityValue.ForeColor = AppTheme.SuccessColor;
            }
        }

        private void AutoScaleYAxis(PlotView plot, List<DataPoint> data)
        {
            if (plot?.Model?.Axes == null || plot.Model.Axes.Count == 0)
                return;

            var yAxis = plot.Model.Axes[0] as LinearAxis;
            if (yAxis == null) return;

            yAxis.Minimum = -2500;
            yAxis.Maximum = 2500;
        }

        private void ClearPlot()
        {
            try
            {
                lock (ecgData)
                    ecgData.Clear();

                ecgSeries.Points.Clear();

                qrsSeries?.Points.Clear();
                spikeSeries?.Points.Clear();
                qrsAfterSpikeSeries?.Points.Clear();

                _predictionState?.Clear();
                _lastDetectedType = SegmentType.Background;

                _plotStartTime = DateTime.Now;
                _receivedPoints = 0;

                lblRecordValue.Text = "00:00 | 0 точек";
                lblQualityValue.Text = "Не определено";
                lblQualityValue.ForeColor = AppTheme.MutedTextColor;

                if (plotEcg?.Model?.Axes != null && plotEcg.Model.Axes.Count > 1)
                {
                    plotEcg.Model.Axes[1].Minimum = 0;
                    plotEcg.Model.Axes[1].Maximum = 30;
                    plotEcg.Model.Axes[0].Minimum = -10;
                    plotEcg.Model.Axes[0].Maximum = 10;
                }

                plotEcg.InvalidatePlot(true);
                Log("График очищен", "SUCCESS");
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log($"Ошибка очистки графика: {ex.Message}", "ERROR");
            }
        }

        private void ConfigureEspDevice(EspDevice device)
        {
            try
            {
                Log($"Начинаю настройку {device.Name}...", "INFO", device.Name);

                bool connected = _networkManager.IsConnectedToNetwork(device.ApSsid);

                if (!connected)
                {
                    Log($"Не подключен к сети {device.ApSsid}", "WARN", device.Name);

                    Invoke(new Action(() =>
                    {
                        var result = MessageBox.Show(
                            $"Необходимо подключиться к сети {device.ApSsid}.\n\n" +
                            $"Выполнить автоматическое подключение?",
                            "Подключение к Wi-Fi",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            bool success = _networkManager.ConnectToEspNetwork(
                                device.ApSsid,
                                device.ApPassword);

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

        private void SendWifiCredentialsToEsp(EspDevice device)
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
                Log("Отправляю параметры Wi-Fi...", "INFO", device.Name);

                bool success = _networkManager.SendWifiCredentialsToEsp(
                    device.ApIp,
                    device.Port,
                    _config.HotspotSsid,
                    _config.HotspotPassword,
                    device.Name);

                if (success)
                {
                    Log($"{device.Name} перезагружается и подключается к сети...", "SUCCESS", device.Name);

                    Invoke(new Action(() =>
                    {
                        MessageBox.Show(
                            $"{device.Name} перезагружается и подключится к сети {_config.HotspotSsid}.\n" +
                            "После этого можно выполнить поиск устройства.",
                            "Перезагрузка ESP32",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }));

                    Thread.Sleep(7000);
                    FindAndSaveEspInNetwork(device);
                }
                else
                {
                    Log("Не удалось отправить параметры Wi-Fi", "ERROR", device.Name);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}", "ERROR", device.Name);
            }
        }

        private void StreamFromSingleEsp()
        {
            var device = GetMainDevice();

            if (device == null)
            {
                MessageBox.Show("Устройство не задано в конфигурации.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string ip = !string.IsNullOrEmpty(device.HotspotIp)
                ? device.HotspotIp
                : device.ApIp;

            if (!_networkManager.CheckEspAvailability(ip, device.Port))
            {
                MessageBox.Show($"{device.Name} недоступен по адресу {ip}.",
                    "Ошибка подключения",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            StopAllStreams();

            bool continueRecording = ecgData.Count > 0;

            if (!continueRecording)
            {
                ClearPlot();
                _plotStartTime = DateTime.Now;
                _receivedPoints = 0;
            }

            _recordingStartTime = DateTime.Now;

            var worker = new StreamWorker(this, device, ip);

            worker.DataReceived += (devName, data) =>
            {
                AddDataPoint(devName, data);
            };

            worker.Start();

            Log(
                continueRecording
                    ? $"Регистрация продолжена: {device.Name}"
                    : $"Регистрация сигнала начата: {device.Name}",
                "SUCCESS",
                device.Name);

            UpdateUI();
        }

        private void StopSingleStream()
        {
            var device = GetMainDevice();

            if (device == null)
                return;

            var worker = _activeWorkers.FirstOrDefault(w => w.Device.Name == device.Name);

            if (worker == null)
            {
                MessageBox.Show("Нет активной регистрации для остановки.",
                    "Информация",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            worker.Stop();

            _recordingStartTime = null;
            lblRecordValue.Text = $"Остановлена | {_receivedPoints} точек";

            Log($"Регистрация остановлена: {device.Name}", "SUCCESS", device.Name);
            UpdateUI();
        }

        private void StopAllStreams()
        {
            lock (_activeWorkers)
            {
                foreach (var worker in _activeWorkers.ToList())
                    worker.Stop();

                _activeWorkers.Clear();
            }

            UpdateUI();
        }

        private List<string> GetHotspotClientsIps()
        {
            var ips = new List<string>();

            try
            {
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
            }
            catch (Exception ex)
            {
                Log($"Ошибка получения ARP-таблицы: {ex.Message}", "ERROR");
            }

            return ips.Distinct().ToList();
        }


        private bool FindAndSaveEspInNetwork(EspDevice device)
        {
            Log($"Поиск {device.Name} в сети...", "INFO", device.Name);

            if (!string.IsNullOrEmpty(device.HotspotIp))
            {
                if (_networkManager.CheckEspAvailability(device.HotspotIp, device.Port, 1000))
                {
                    Log($"{device.Name} найден по сохраненному IP: {device.HotspotIp}", "SUCCESS", device.Name);
                    return true;
                }
            }

            Log("Проверяю устройства в сети хот-спота...", "INFO", device.Name);

            var hotspotIps = GetHotspotClientsIps();

            foreach (var ip in hotspotIps)
            {
                if (_networkManager.CheckEspAvailability(ip, device.Port, 500))
                {
                    device.HotspotIp = ip;

                    Log($"{device.Name} найден: {ip}", "SUCCESS", device.Name);
                    SaveConfig();

                    Invoke(new Action(() =>
                    {
                        UpdateDeviceStatusLabel(device, true, false);
                    }));

                    return true;
                }
            }

            Log("Быстрый поиск не дал результата, выполняю сканирование...", "WARN", device.Name);

            for (int i = 1; i <= 254; i++)
            {
                string testIp = $"192.168.137.{i}";

                if (_networkManager.CheckEspAvailability(testIp, device.Port, 100))
                {
                    device.HotspotIp = testIp;

                    Log($"{device.Name} найден: {testIp}", "SUCCESS", device.Name);
                    SaveConfig();

                    Invoke(new Action(() =>
                    {
                        UpdateDeviceStatusLabel(device, true, false);
                    }));

                    return true;
                }
            }

            Log($"{device.Name} не найден в сети", "WARN", device.Name);
            return false;
        }
    }
}