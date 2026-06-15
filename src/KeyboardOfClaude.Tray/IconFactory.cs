using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KeyboardOfClaude.Tray;

/// <summary>
/// Builds tray icons depicting a small robot head tinted with the session
/// colour, so the system-tray glyph mirrors the keyboard at a glance.
/// Icons are produced as PNG-backed .ico streams (fully managed, disposable)
/// rather than via GetHicon, which would leak a native HICON per icon.
/// </summary>
internal static class IconFactory
{
    // Sizes baked into each .ico so Windows always has a crisp native bitmap
    // to pick from rather than downscaling one source.
    private static readonly int[] Sizes = { 16, 32, 48 };

    public static Icon RobotIcon(Color colour)
    {
        var images = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
            images[i] = RenderPng(Sizes[i], colour);

        using var ico = new MemoryStream();
        WriteIco(ico, images);
        ico.Position = 0;
        return new Icon(ico);
    }

    private static byte[] RenderPng(int size, Color colour)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawRobot(g, size, colour);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void DrawRobot(Graphics g, int size, Color colour)
    {
        // Lay the robot out in a 0..1 space scaled to the bitmap, so the
        // proportions hold at every size.
        float s = size;
        float Px(float f) => f * s;

        var outline = Shade(colour, 0.55f);
        var faceColour = Color.FromArgb(40, 44, 52); // dark slate for eyes/mouth
        using var body = new SolidBrush(colour);
        using var face = new SolidBrush(faceColour);
        using var pen = new Pen(outline, Math.Max(1f, s * 0.05f));

        // Antenna: stalk plus a ball on top.
        float cx = Px(0.5f);
        g.DrawLine(pen, cx, Px(0.21f), cx, Px(0.08f));
        float ball = Px(0.11f);
        g.FillEllipse(body, cx - ball / 2, Px(0.03f), ball, ball);
        g.DrawEllipse(pen, cx - ball / 2, Px(0.03f), ball, ball);

        // Head.
        var head = new RectangleF(Px(0.16f), Px(0.21f), Px(0.68f), Px(0.62f));
        using (var path = RoundedRect(head, Px(0.16f)))
        {
            g.FillPath(body, path);
            g.DrawPath(pen, path);
        }

        // Eyes.
        float eyeR = Px(0.13f);
        float eyeY = Px(0.40f);
        g.FillEllipse(face, Px(0.34f) - eyeR / 2, eyeY, eyeR, eyeR);
        g.FillEllipse(face, Px(0.66f) - eyeR / 2, eyeY, eyeR, eyeR);

        // Mouth: a small grille slit for character.
        var mouth = new RectangleF(Px(0.36f), Px(0.63f), Px(0.28f), Px(0.08f));
        using var mouthPath = RoundedRect(mouth, Px(0.04f));
        g.FillPath(face, mouthPath);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
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

    private static Color Shade(Color c, float factor) =>
        Color.FromArgb(c.A, (int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));

    private static void WriteIco(Stream stream, byte[][] images)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // ICONDIR header.
        w.Write((short)0);             // reserved
        w.Write((short)1);             // type: 1 = icon
        w.Write((short)images.Length); // image count

        // ICONDIRENTRY per image; PNG payloads follow the directory.
        int offset = 6 + 16 * images.Length;
        for (int i = 0; i < images.Length; i++)
        {
            int dim = Sizes[i];
            w.Write((byte)(dim >= 256 ? 0 : dim)); // width  (0 means 256)
            w.Write((byte)(dim >= 256 ? 0 : dim)); // height (0 means 256)
            w.Write((byte)0);                       // palette count
            w.Write((byte)0);                       // reserved
            w.Write((short)1);                      // colour planes
            w.Write((short)32);                     // bits per pixel
            w.Write(images[i].Length);              // bytes in payload
            w.Write(offset);                        // payload offset
            offset += images[i].Length;
        }

        foreach (var img in images)
            w.Write(img);
    }
}
