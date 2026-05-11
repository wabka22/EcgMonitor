namespace ESP32StreamManager
{
    public static class UiFactory
    {
        public static RoundedPanel CreateRoundedPanel(
            int radius = 20,
            Padding? padding = null,
            Padding? margin = null)
        {
            return new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.PanelBackColor,
                BorderRadius = radius,
                BorderColor = AppTheme.BorderColor,
                Padding = padding ?? new Padding(14),
                Margin = margin ?? new Padding(0)
            };
        }

        public static RoundedPanel CreateStatusCard(
            string title,
            string value,
            out Label valueLabel)
        {
            var card = CreateRoundedPanel(
                20,
                new Padding(16),
                new Padding(0, 0, 14, 0));

            var titleLabel = new Label
            {
                Text = title,
                ForeColor = AppTheme.MutedTextColor,
                Font = new Font("Segoe UI", 10),
                Dock = DockStyle.Top,
                Height = 28
            };

            valueLabel = new Label
            {
                Text = value,
                ForeColor = AppTheme.TextColor,
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);

            return card;
        }

        public static Button CreatePrimaryButton(string text)
        {
            var btn = CreateBaseButton(text);
            ApplyButtonColors(btn, AppTheme.AccentColor, AppTheme.AccentHoverColor, Color.White);
            return btn;
        }

        public static Button CreateSecondaryButton(string text)
        {
            var btn = CreateBaseButton(text);
            ApplyButtonColors(btn, AppTheme.SecondaryButtonColor, AppTheme.SecondaryButtonHoverColor, AppTheme.TextColor);
            return btn;
        }

        public static Button CreateDangerButton(string text)
        {
            var btn = CreateBaseButton(text);
            ApplyButtonColors(btn, AppTheme.DangerColor, AppTheme.DangerHoverColor, Color.White);
            return btn;
        }

        private static Button CreateBaseButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 8),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Emoji", 14, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };

            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = AppTheme.BorderColor;

            return btn;
        }

        private static void ApplyButtonColors(Button btn, Color normal, Color hover, Color textColor)
        {
            btn.BackColor = normal;
            btn.ForeColor = textColor;

            btn.MouseEnter += (s, e) => btn.BackColor = hover;
            btn.MouseLeave += (s, e) => btn.BackColor = normal;
        }
    }
}