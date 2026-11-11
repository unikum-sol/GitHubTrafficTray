using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace GitHubTrafficTray;

public readonly record struct ChartPoint(DateTime Date, int Value);

/// <summary>Einfaches Liniendiagramm im GitHub-Stil (ohne externe Abhängigkeiten).</summary>
internal sealed class LineChart : Control
{
    private string _title = "";
    private string _subtitle = "";
    private string _yLabel = "";
    private IReadOnlyList<ChartPoint> _points = Array.Empty<ChartPoint>();

    public LineChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
    }

    public void SetData(string title, string subtitle, string yLabel, IReadOnlyList<ChartPoint> points)
    {
        _title = title;
        _subtitle = subtitle;
        _yLabel = yLabel;
        _points = points;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var borderPen = new Pen(Theme.Border);
        using (var frame = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6f))
            g.DrawPath(borderPen, frame);

        using var titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var subFont = new Font("Segoe UI", 8.5f);
        using var smallFont = new Font("Segoe UI", 7.5f);
        using var valueFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var gridPen = new Pen(Theme.Grid);
        using var linePen = new Pen(Theme.Accent, 2f) { LineJoin = LineJoin.Round };

        g.DrawString(_title, titleFont, textBrush, 14, 10);
        g.DrawString(_subtitle, subFont, mutedBrush, 14, 32);

        int n = _points.Count;
        int max = n == 0 ? 0 : _points.Max(p => p.Value);
        var (axisMax, step) = NiceScale(max);

        float yLabelWidth = 0;
        for (int v = 0; v <= axisMax; v += step)
            yLabelWidth = Math.Max(yLabelWidth, g.MeasureString(v.ToString("N0"), smallFont).Width);

        float top = 70;
        float bottom = Height - 32;
        float left = 32 + yLabelWidth + 4;
        float right = Width - 20;
        if (right - left < 40 || bottom - top < 30) return;

        // Y-Achsentitel (gedreht)
        var state = g.Save();
        g.TranslateTransform(18, (top + bottom) / 2);
        g.RotateTransform(-90);
        var ySize = g.MeasureString(_yLabel, smallFont);
        g.DrawString(_yLabel, smallFont, mutedBrush, -ySize.Width / 2, -ySize.Height / 2);
        g.Restore(state);

        float Y(int value) => bottom - (float)value / axisMax * (bottom - top);

        // Horizontale Gitterlinien + Y-Beschriftung
        for (int v = 0; v <= axisMax; v += step)
        {
            float y = Y(v);
            g.DrawLine(gridPen, left, y, right, y);
            string s = v.ToString("N0");
            var size = g.MeasureString(s, smallFont);
            g.DrawString(s, smallFont, mutedBrush, left - 4 - size.Width, y - size.Height / 2);
        }

        if (n == 0)
        {
            const string empty = "Keine Daten";
            var size = g.MeasureString(empty, subFont);
            g.DrawString(empty, subFont, mutedBrush, (left + right - size.Width) / 2, (top + bottom - size.Height) / 2);
            return;
        }

        float X(int index) => n == 1 ? (left + right) / 2 : left + index * (right - left) / (n - 1);

        // X-Beschriftung + vertikale Gitterlinien
        bool longSpan = (_points[n - 1].Date - _points[0].Date).TotalDays > 365;
        string fmt = longSpan ? "dd.MM.yy" : "dd.MM.";
        float labelWidth = g.MeasureString(new DateTime(2000, 12, 28).ToString(fmt, CultureInfo.InvariantCulture), smallFont).Width + 10;
        float spacing = n > 1 ? (right - left) / (n - 1) : right - left;
        int every = Math.Max(1, (int)Math.Ceiling(labelWidth / Math.Max(1f, spacing)));

        for (int i = 0; i < n; i += every)
        {
            float x = X(i);
            g.DrawLine(gridPen, x, top, x, bottom);
            string s = _points[i].Date.ToString(fmt, CultureInfo.InvariantCulture);
            var size = g.MeasureString(s, smallFont);
            g.DrawString(s, smallFont, mutedBrush, x - size.Width / 2, bottom + 4);
        }

        // Linie
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = new PointF(X(i), Y(_points[i].Value));
        if (n >= 2) g.DrawLines(linePen, pts);

        // Punkte und Werte (nur wenn genug Platz)
        if (n <= 60)
        {
            foreach (var p in pts) g.FillEllipse(accentBrush, p.X - 2.5f, p.Y - 2.5f, 5f, 5f);
        }
        if (n <= 31 && spacing >= 14)
        {
            for (int i = 0; i < n; i++)
            {
                string s = _points[i].Value.ToString("N0");
                var size = g.MeasureString(s, valueFont);
                g.DrawString(s, valueFont, textBrush, pts[i].X - size.Width / 2, pts[i].Y - size.Height - 3);
            }
        }
    }

    private static (int AxisMax, int Step) NiceScale(int max)
    {
        if (max <= 0) return (1, 1);
        double raw = max / 4.0;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / exp;
        double nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        int step = Math.Max(1, (int)Math.Round(nf * exp));
        int axisMax = (int)Math.Ceiling(max / (double)step) * step;
        return (axisMax, step);
    }
}
