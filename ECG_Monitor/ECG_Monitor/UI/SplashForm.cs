namespace ESP32StreamManager
{
    public class SplashForm : Form
    {
        private System.Windows.Forms.Timer timer;

        public SplashForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            if (File.Exists("app.ico"))
            {
                Icon = new Icon("app.ico");
            }

            Text = "Загрузка";
            Size = new Size(520, 320);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.White;

            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(30)
            };

            var iconLabel = new Label
            {
                Text = "❤",
                Font = new Font("Segoe UI Emoji", 54, FontStyle.Regular),
                ForeColor = Color.FromArgb(37, 99, 235),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 5),
                Size = new Size(520, 90)
            };

            var titleLabel = new Label
            {
                Text = "ECG Stream Monitor",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 125),
                Size = new Size(520, 40)
            };

            var subtitleLabel = new Label
            {
                Text = "Система регистрации биосигналов",
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 170),
                Size = new Size(520, 30)
            };

            var loadingLabel = new Label
            {
                Text = "Загрузка приложения...",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 235),
                Size = new Size(520, 25)
            };

            var progress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(130, 265),
                Size = new Size(260, 8)
            };

            panel.Controls.Add(iconLabel);
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(subtitleLabel);
            panel.Controls.Add(loadingLabel);
            panel.Controls.Add(progress);

            Controls.Add(panel);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1800;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Close();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }
    }
}