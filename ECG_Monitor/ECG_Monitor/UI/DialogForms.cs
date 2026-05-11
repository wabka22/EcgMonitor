using System.Text.Json;

namespace ESP32StreamManager
{
    public class DeviceSelectionDialog : Form
    {
        public EspDevice SelectedDevice { get; private set; }

        public DeviceSelectionDialog(List<EspDevice> devices, string title)
        {
            this.Text = title;
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var label = new Label
            {
                Text = "Выберите устройство:",
                Location = new Point(10, 20),
                Size = new Size(300, 20)
            };

            var comboBox = new ComboBox
            {
                Location = new Point(10, 50),
                Size = new Size(350, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (var device in devices)
            {
                string status = !string.IsNullOrEmpty(device.HotspotIp) ?
                    $"✓ {device.HotspotIp}" : "✗ Нет IP";
                comboBox.Items.Add($"{device.Name} ({device.ApSsid}) - {status}");
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;

            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(200, 100),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(290, 100),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            btnOk.Click += (s, e) =>
            {
                if (comboBox.SelectedIndex >= 0)
                    SelectedDevice = devices[comboBox.SelectedIndex];
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            panel.Controls.AddRange(new Control[] { label, comboBox, btnOk, btnCancel });
            this.Controls.Add(panel);
        }
    }

    public class ConfigForm : Form
    {
        public Config Config { get; private set; }
        private TextBox txtSsid;
        private TextBox txtPassword;
        private DataGridView gridDevices;

        public ConfigForm(Config config)
        {
            Config = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config));
            InitializeComponent();
            LoadConfig();
        }

        private void InitializeComponent()
        {
            this.Text = "Редактирование конфигурации";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var lblNetwork = new Label
            {
                Text = "Настройки домашней сети:",
                Location = new Point(10, 10),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            var lblSsid = new Label { Text = "SSID:", Location = new Point(20, 40), Size = new Size(100, 20) };
            txtSsid = new TextBox { Location = new Point(120, 40), Size = new Size(300, 25) };

            var lblPassword = new Label { Text = "Пароль:", Location = new Point(20, 70), Size = new Size(100, 20) };
            txtPassword = new TextBox { Location = new Point(120, 70), Size = new Size(300, 25), UseSystemPasswordChar = true };

            var lblDevices = new Label
            {
                Text = "Устройства ESP:",
                Location = new Point(10, 110),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            gridDevices = new DataGridView
            {
                Location = new Point(10, 140),
                Size = new Size(560, 250),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false
            };

            gridDevices.Columns.Add("Name", "Имя");
            gridDevices.Columns.Add("ApSsid", "SSID AP");
            gridDevices.Columns.Add("ApIp", "IP AP");
            gridDevices.Columns.Add("Port", "Порт");
            gridDevices.Columns.Add("HotspotIp", "IP в сети");
            gridDevices.Columns.Add("MacAddress", "MAC-адрес");

            foreach (DataGridViewColumn column in gridDevices.Columns)
            {
                column.Width = 90;
            }

            var btnSave = new Button
            {
                Text = "Сохранить",
                Location = new Point(400, 400),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(490, 400),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            btnSave.Click += (s, e) => SaveConfig();

            panel.Controls.AddRange(new Control[] {
                lblNetwork, lblSsid, txtSsid, lblPassword, txtPassword,
                lblDevices, gridDevices, btnSave, btnCancel
            });

            this.Controls.Add(panel);
        }

        private void LoadConfig()
        {
            txtSsid.Text = Config.HotspotSsid;
            txtPassword.Text = Config.HotspotPassword;

            gridDevices.Rows.Clear();
            foreach (var device in Config.EspDevices)
            {
                gridDevices.Rows.Add(
                    device.Name,
                    device.ApSsid,
                    device.ApIp,
                    device.Port,
                    device.HotspotIp,
                    device.MacAddress
                );
            }
        }

        private void SaveConfig()
        {
            Config.HotspotSsid = txtSsid.Text;
            Config.HotspotPassword = txtPassword.Text;

            Config.EspDevices.Clear();
            foreach (DataGridViewRow row in gridDevices.Rows)
            {
                if (!row.IsNewRow)
                {
                    Config.EspDevices.Add(new EspDevice
                    {
                        Name = row.Cells[0].Value?.ToString() ?? "",
                        ApSsid = row.Cells[1].Value?.ToString() ?? "",
                        ApIp = row.Cells[2].Value?.ToString() ?? "192.168.4.1",
                        Port = int.TryParse(row.Cells[3].Value?.ToString(), out int port) ? port : 8888,
                        HotspotIp = row.Cells[4].Value?.ToString(),
                        MacAddress = row.Cells[5].Value?.ToString() ?? ""
                    });
                }
            }
        }
    }
}