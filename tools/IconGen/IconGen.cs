#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false

// Draws the app icon (vector, WPF) and writes a multi-size .ico plus a 256 px PNG.
//   dotnet run tools/IconGen/IconGen.cs -- <output folder>
// Two stacked photos (the copies) being sucked toward a vacuum nozzle.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

var t = new Thread(() =>
{
    var pngs = sizes.Select(s => (Size: s, Png: Render(s))).ToList();
    File.WriteAllBytes(Path.Combine(outDir, "app-256.png"), pngs.Last().Png);
    foreach (var (s, png) in pngs) File.WriteAllBytes(Path.Combine(outDir, $"preview-{s}.png"), png);
    WriteIco(Path.Combine(outDir, "app.ico"), pngs);
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
Console.WriteLine($"Wrote {Path.GetFullPath(Path.Combine(outDir, "app.ico"))}");

static byte[] Render(int size)
{
    var small = size <= 24; // fewer details where they'd turn to mush
    var v = new DrawingVisual();
    using (var dc = v.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));

        // Background tile; everything else is clipped to it
        var tile = new RectangleGeometry(new Rect(8, 8, 240, 240), 56, 56);
        var bg = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7E, 0xA8), Color.FromRgb(0x7B, 0x4F, 0xE8), 50);
        dc.DrawGeometry(bg, null, tile);
        dc.PushClip(tile);

        // Back photo (the copy), tilted
        if (!small)
        {
            dc.PushTransform(new RotateTransform(-14, 90, 90));
            Photo(dc, new Rect(26, 40, 124, 96), Color.FromRgb(0xC9, 0xE8, 0xFF), muted: true);
            dc.Pop();
        }

        // Front photo, tilted the other way
        dc.PushTransform(new RotateTransform(small ? -6 : 7, 104, 104));
        Photo(dc, small ? new Rect(22, 30, 150, 116) : new Rect(42, 56, 128, 98), Color.FromRgb(0x8F, 0xD3, 0xFF), muted: false);
        dc.Pop();

        // Canister vacuum in the corner: body, wheel, hose, floor nozzle facing the photos
        var dark = new SolidColorBrush(Color.FromRgb(0x24, 0x26, 0x3A));
        var mid = new SolidColorBrush(Color.FromRgb(0x4B, 0x50, 0x78));
        var body = small ? new Rect(150, 168, 92, 70) : new Rect(164, 176, 76, 56);
        dc.DrawRoundedRectangle(dark, null, body, 24, 24);
        dc.DrawRoundedRectangle(mid, null, new Rect(body.X + 10, body.Y + 9, body.Width - 26, body.Height * 0.32), 8, 8);
        var wheel = new Point(body.X + body.Width * 0.3, body.Bottom - 2);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x22)), new Pen(mid, small ? 6 : 4), wheel, small ? 16 : 12, small ? 16 : 12);

        var hosePen = new Pen(dark, small ? 16 : 11) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, hosePen, Geometry.Parse(small
            ? "M 166,182 C 140,190 132,196 118,196"
            : "M 176,190 C 156,204 150,214 136,214"));
        // Floor nozzle: a wide head whose mouth faces up-left, toward the photos
        dc.PushTransform(new RotateTransform(-32, small ? 112 : 128, small ? 196 : 214));
        dc.DrawRoundedRectangle(dark, null, small ? new Rect(78, 186, 52, 22) : new Rect(102, 205, 44, 18), 6, 6);
        dc.Pop();

        // Suction lines between photos and nozzle
        if (!small)
        {
            var line = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(line, new Point(92, 186), new Point(84, 170));
            dc.DrawLine(line, new Point(112, 182), new Point(110, 166));
            dc.DrawLine(line, new Point(76, 200), new Point(62, 190));
        }
        dc.Pop(); // clip
        dc.Pop(); // scale
    }

    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(v);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}

// A white photo card with a little landscape: sky, sun, two hills.
static void Photo(DrawingContext dc, Rect r, Color sky, bool muted)
{
    var frame = new SolidColorBrush(muted ? Color.FromRgb(0xEE, 0xEE, 0xF4) : Colors.White);
    var shadow = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
    dc.DrawRoundedRectangle(shadow, null, new Rect(r.X + 5, r.Y + 7, r.Width, r.Height), 10, 10);
    dc.DrawRoundedRectangle(frame, null, r, 10, 10);
    var inner = new Rect(r.X + 10, r.Y + 10, r.Width - 20, r.Height - 20);
    dc.PushClip(new RectangleGeometry(inner, 4, 4));
    dc.DrawRectangle(new SolidColorBrush(sky), null, inner);
    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x3C)), null, new Point(inner.Right - inner.Width * 0.24, inner.Top + inner.Height * 0.28), inner.Width * 0.11, inner.Width * 0.11);
    var hills = new StreamGeometry();
    using (var g = hills.Open())
    {
        g.BeginFigure(new Point(inner.Left, inner.Bottom), true, true);
        g.LineTo(new Point(inner.Left, inner.Bottom - inner.Height * 0.30), true, false);
        g.BezierTo(new Point(inner.Left + inner.Width * 0.25, inner.Bottom - inner.Height * 0.75),
                   new Point(inner.Left + inner.Width * 0.45, inner.Bottom - inner.Height * 0.55),
                   new Point(inner.Left + inner.Width * 0.6, inner.Bottom - inner.Height * 0.38), true, false);
        g.BezierTo(new Point(inner.Left + inner.Width * 0.75, inner.Bottom - inner.Height * 0.55),
                   new Point(inner.Right - inner.Width * 0.1, inner.Bottom - inner.Height * 0.5),
                   new Point(inner.Right, inner.Bottom - inner.Height * 0.36), true, false);
        g.LineTo(new Point(inner.Right, inner.Bottom), true, false);
    }
    dc.DrawGeometry(new SolidColorBrush(muted ? Color.FromRgb(0x8C, 0xC9, 0x8F) : Color.FromRgb(0x3F, 0xA3, 0x4D)), null, hills);
    dc.Pop();
}

// ICO with PNG-compressed entries (supported since Windows Vista).
static void WriteIco(string path, List<(int Size, byte[] Png)> images)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(png.Length); w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images) w.Write(png);
}
