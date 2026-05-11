namespace ESP32StreamManager
{
    public class ConfigEditorForm : Form
    {
        public Config Config { get; private set; }

        private TextBox txtHotspotSsid;
        private TextBox txtHotspotPassword;

        private TextBox txtDeviceName;
        private TextBox txtApSsid;
        private TextBox txtApPassword;
        private TextBox txtApIp;
        private TextBox txtPort;
        private TextBox txtHotspotIp;
        private TextBox txtMacAddress;

        public ConfigEditorForm(Config config)
        {
            Config = CloneConfig(config);
            InitializeComponent();
            LoadValues();
        }

        private void InitializeComponent()
        {
            Text = "Настройки конфигурации";
            Size = new Size(560, 620);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = AppTheme.MainBackColor;

            if (File.Exists("app.ico"))
                Icon = new Icon("app.ico");

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                BackColor = AppTheme.MainBackColor
            };

            var card = UiFactory.CreateRoundedPanel(
                22,
                new Padding(24),
                new Padding(0));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 12,
                BackColor = AppTheme.PanelBackColor
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

            for (int i = 0; i < 10; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            txtHotspotSsid = AddRow(layout, 0, "Домашняя Wi-Fi сеть:");
            txtHotspotPassword = AddRow(layout, 1, "Пароль Wi-Fi:", true);

            AddSectionLabel(layout, 2, "Настройки ESP32");

            txtDeviceName = AddRow(layout, 3, "Имя устройства:");
            txtApSsid = AddRow(layout, 4, "AP SSID:");
            txtApPassword = AddRow(layout, 5, "AP пароль:", true);
            txtApIp = AddRow(layout, 6, "AP IP:");
            txtPort = AddRow(layout, 7, "Порт:");
            txtHotspotIp = AddRow(layout, 8, "IP в сети ПК:");
            txtMacAddress = AddRow(layout, 9, "MAC-адрес:");

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 55,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = AppTheme.PanelBackColor,
                Padding = new Padding(0, 10, 0, 0)
            };

            var btnSave = new Button
            {
                Text = "Сохранить",
                Size = new Size(120, 36),
                BackColor = AppTheme.AccentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Отмена",
                Size = new Size(120, 36),
                BackColor = AppTheme.SecondaryButtonColor,
                ForeColor = AppTheme.TextColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                DialogResult = DialogResult.Cancel
            };

            btnSave.FlatAppearance.BorderSize = 0;
            btnCancel.FlatAppearance.BorderSize = 0;

            btnSave.Click += (s, e) =>
            {
                if (SaveValues())
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            buttonsPanel.Controls.Add(btnSave);
            buttonsPanel.Controls.Add(btnCancel);

            card.Controls.Add(layout);
            card.Controls.Add(buttonsPanel);

            mainPanel.Controls.Add(card);
            Controls.Add(mainPanel);
        }

        private TextBox AddRow(
            TableLayoutPanel layout,
            int row,
            string labelText,
            bool password = false)
        {
            var label = new Label
            {
                Text = labelText,
                ForeColor = AppTheme.MutedTextColor,
                Font = new Font("Segoe UI", 10),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = password,
                Margin = new Padding(0, 7, 0, 7)
            };

            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(textBox, 1, row);

            return textBox;
        }

        private void AddSectionLabel(TableLayoutPanel layout, int row, string text)
        {
            var label = new Label
            {
                Text = text,
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 2);
        }

        private void LoadValues()
        {
            txtHotspotSsid.Text = Config.HotspotSsid;
            txtHotspotPassword.Text = Config.HotspotPassword;

            var device = Config.EspDevices.FirstOrDefault();

            if (device == null)
            {
                device = new EspDevice
                {
                    Name = "ESP32_ECG",
                    ApSsid = "ESP32_ECG_Streamer",
                    ApPassword = "12345678",
                    ApIp = "192.168.4.1",
                    Port = 8888,
                    HotspotIp = "",
                    MacAddress = ""
                };

                Config.EspDevices.Add(device);
            }

            txtDeviceName.Text = device.Name;
            txtApSsid.Text = device.ApSsid;
            txtApPassword.Text = device.ApPassword;
            txtApIp.Text = device.ApIp;
            txtPort.Text = device.Port.ToString();
            txtHotspotIp.Text = device.HotspotIp ?? "";
            txtMacAddress.Text = device.MacAddress;
        }

        private bool SaveValues()
        {
            if (string.IsNullOrWhiteSpace(txtHotspotSsid.Text))
            {
                MessageBox.Show("Введите SSID домашней сети.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtDeviceName.Text))
            {
                MessageBox.Show("Введите имя устройства.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtApSsid.Text))
            {
                MessageBox.Show("Введите AP SSID устройства.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!int.TryParse(txtPort.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт от 1 до 65535.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Config.HotspotSsid = txtHotspotSsid.Text.Trim();
            Config.HotspotPassword = txtHotspotPassword.Text;

            if (Config.EspDevices.Count == 0)
                Config.EspDevices.Add(new EspDevice());

            var device = Config.EspDevices[0];

            device.Name = txtDeviceName.Text.Trim();
            device.ApSsid = txtApSsid.Text.Trim();
            device.ApPassword = txtApPassword.Text;
            device.ApIp = txtApIp.Text.Trim();
            device.Port = port;
            device.HotspotIp = string.IsNullOrWhiteSpace(txtHotspotIp.Text)
                ? ""
                : txtHotspotIp.Text.Trim();
            device.MacAddress = txtMacAddress.Text.Trim();

            return true;
        }

        private Config CloneConfig(Config source)
        {
            return new Config
            {
                HotspotSsid = source.HotspotSsid,
                HotspotPassword = source.HotspotPassword,
                EspDevices = source.EspDevices
                    .Select(d => new EspDevice
                    {
                        Name = d.Name,
                        ApSsid = d.ApSsid,
                        ApPassword = d.ApPassword,
                        ApIp = d.ApIp,
                        Port = d.Port,
                        HotspotIp = d.HotspotIp,
                        MacAddress = d.MacAddress
                    })
                    .ToList()
            };
        }
    }
}