using System.Drawing.Drawing2D;

namespace ESP32StreamManager
{
    public class RoundedPanel : Panel
    {
        public int BorderRadius { get; set; } = 18;
        public Color BorderColor { get; set; } = Color.FromArgb(226, 232, 240);
        public int BorderSize { get; set; } = 1;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using GraphicsPath path = GetRoundedPath(ClientRectangle, BorderRadius);
            Region = new Region(path);

            using Pen pen = new Pen(BorderColor, BorderSize);
            e.Graphics.DrawPath(pen, path);
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}