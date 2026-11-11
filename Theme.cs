using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GitHubTrafficTray;

internal static class Theme
{
    public static readonly Color Back = ColorTranslator.FromHtml("#0d1117");
    public static readonly Color Panel = ColorTranslator.FromHtml("#161b22");
    public static readonly Color Button = ColorTranslator.FromHtml("#21262d");
    public static readonly Color Border = ColorTranslator.FromHtml("#30363d");
    public static readonly Color Grid = ColorTranslator.FromHtml("#21262d");
    public static readonly Color Text = ColorTranslator.FromHtml("#e6edf3");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8b949e");
    public static readonly Color Accent = ColorTranslator.FromHtml("#2ea043");
    public static readonly Color Selection = ColorTranslator.FromHtml("#1f6feb");
    public static readonly Color Danger = ColorTranslator.FromHtml("#da3633");

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dunkle Titelleiste (Windows 10 ab 1809 / Windows 11). Fehler werden ignoriert.</summary>
    public static void ApplyDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // nicht unterstützt
        }
    }
}

internal static class IconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Theme.Panel);
            using var border = new Pen(Theme.Border, 1f);
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, 31f, 31f), 6f);
            g.FillPath(bg, path);
            g.DrawPath(border, path);
            using var pen = new Pen(Theme.Accent, 3f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(5, 24), new PointF(12, 15), new PointF(18, 20), new PointF(27, 7)
            });
        }
        // Handle bleibt für die Laufzeit des Programms bestehen (ein einziges Icon).
        return Icon.FromHandle(bmp.GetHicon());
    }
}
