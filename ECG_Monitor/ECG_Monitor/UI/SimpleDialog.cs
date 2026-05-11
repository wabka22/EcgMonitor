
namespace ESP32StreamManager
{
    public class SimpleDialog : Form
    {
        public int SelectedIndex { get; private set; } = -1;

        public SimpleDialog(string title, string description, string[] options)
        {
            this.Text = title;
            this.Size = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var lblDesc = new Label
            {
                Text = description,
                Location = new Point(10, 10),
                Size = new Size(350, 40)
            };

            int y = 60;
            for (int i = 0; i < options.Length; i++)
            {
                var button = new Button
                {
                    Text = options[i],
                    Location = new Point(10, y),
                    Size = new Size(350, 40),
                    Tag = i
                };

                button.Click += (s, e) =>
                {
                    SelectedIndex = (int)((Button)s).Tag;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                };

                panel.Controls.Add(button);
                y += 50;
            }

            this.Controls.Add(panel);
        }
    }
}