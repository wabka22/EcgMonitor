
namespace ESP32StreamManager
{
    public class SimpleNetworkDialog : Form
    {
        public string Ssid { get; private set; }
        public string Password { get; private set; }

        private TextBox txtSsid;
        private TextBox txtPassword;

        public SimpleNetworkDialog(Config config)
        {
            Ssid = config.HotspotSsid;
            Password = config.HotspotPassword;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Настройки домашней сети";
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var lblSsid = new Label
            {
                Text = "SSID домашней сети:",
                Location = new Point(10, 20),
                Size = new Size(200, 20)
            };

            txtSsid = new TextBox
            {
                Location = new Point(10, 45),
                Size = new Size(350, 25),
                Text = Ssid
            };

            var lblPassword = new Label
            {
                Text = "Пароль:",
                Location = new Point(10, 80),
                Size = new Size(200, 20)
            };

            txtPassword = new TextBox
            {
                Location = new Point(10, 105),
                Size = new Size(350, 25),
                UseSystemPasswordChar = true,
                Text = Password
            };

            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(200, 140),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(290, 140),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            btnOk.Click += (s, e) =>
            {
                Ssid = txtSsid.Text;
                Password = txtPassword.Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            panel.Controls.AddRange(new Control[] { lblSsid, txtSsid, lblPassword, txtPassword, btnOk, btnCancel });
            this.Controls.Add(panel);
        }
    }
}