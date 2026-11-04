using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PromptEditor;

internal static class ToolbarIcons
{
    public static Bitmap Create(string name)
    {
        var image = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(image);
        graphics.ScaleTransform(32f / 24, 32f / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var pen = new Pen(Color.FromArgb(40, 40, 40), 1.7f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(pen.Color);

        switch (name)
        {
            case "Bold":
            case "Italic":
            case "Heading":
                using (var font = new Font("Segoe UI", 18, name == "Italic" ? FontStyle.Italic
                               : name is "Bold" or "Heading" ? FontStyle.Bold : FontStyle.Regular,
                           GraphicsUnit.Pixel))
                using (var format = new StringFormat
                       { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(name[..1], font, brush, new RectangleF(0, 0, 24, 24), format);
                break;
            case "Bullets":
            case "Numbers":
                using (var font = new Font("Segoe UI", 7, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    for (var row = 0; row < 3; row++)
                    {
                        var y = 5 + row * 7;
                        if (name == "Bullets")
                            graphics.FillEllipse(brush, 2, y, 3, 3);
                        else
                            graphics.DrawString((row + 1).ToString(), font, brush, 0, y - 3);
                        graphics.DrawLine(pen, 9, y + 1.5f, 21, y + 1.5f);
                    }
                }
                break;
            case "Link":
                graphics.TranslateTransform(12, 12);
                graphics.DrawRoundedLink(pen, -10, -4);
                graphics.DrawRoundedLink(pen, 0, -4);
                graphics.DrawLine(pen, -4, 0, 4, 0);
                break;
            case "Quote":
                graphics.DrawLine(pen, 4, 4, 4, 20);
                graphics.DrawLine(pen, 9, 6, 21, 6);
                graphics.DrawLine(pen, 9, 12, 21, 12);
                graphics.DrawLine(pen, 9, 18, 17, 18);
                break;
            case "Code":
                graphics.DrawLines(pen, [new PointF(7, 6), new PointF(2, 12), new PointF(7, 18)]);
                graphics.DrawLines(pen, [new PointF(17, 6), new PointF(22, 12), new PointF(17, 18)]);
                graphics.DrawLine(pen, 14, 4, 10, 20);
                break;
            case "Code block":
                graphics.DrawRectangle(pen, 2, 3, 20, 18);
                graphics.DrawLines(pen, [new PointF(8, 9), new PointF(5, 12), new PointF(8, 15)]);
                graphics.DrawLines(pen, [new PointF(16, 9), new PointF(19, 12), new PointF(16, 15)]);
                graphics.DrawLine(pen, 13, 8, 11, 16);
                break;
            case "Undo":
            case "Redo":
                if (name == "Redo")
                {
                    graphics.TranslateTransform(24, 0);
                    graphics.ScaleTransform(-1, 1);
                }
                graphics.DrawBezier(pen, 5, 8, 22, 0, 26, 23, 10, 20);
                graphics.DrawLines(pen, [new PointF(5, 2), new PointF(5, 8), new PointF(11, 8)]);
                break;
            case "Find":
                graphics.DrawEllipse(pen, 3, 2, 13, 13);
                graphics.DrawLine(pen, 14.5f, 14.5f, 21, 21);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown toolbar icon.");
        }
        return image;
    }

    private static void DrawRoundedLink(this Graphics graphics, Pen pen, float x, float y)
    {
        using var path = new GraphicsPath();
        path.AddArc(x, y, 8, 8, 90, 180);
        path.AddArc(x + 2, y, 8, 8, 270, 180);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
