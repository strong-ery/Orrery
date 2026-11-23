using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Orrery.App.Views;

/// <summary>Draws the logo (three orbits, a brass core, one planet per bucket) into a bitmap for the tray and window icon.</summary>
public static class AppIcon
{
    public static WindowIcon Create(int size = 64)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            var k = size / 64.0;
            Point C(double v) => new(v * k, v * k);
            var ring = new Pen(new SolidColorBrush(Color.Parse("#B07C8CFF")), 2.4 * k);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#0B0D1A")), null, C(32), 32 * k, 32 * k);
            foreach (var r in new[] { 26.0, 18.0, 10.5 })
                ctx.DrawEllipse(null, ring, C(32), r * k, r * k);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#E8B64C")), null, C(32), 5.5 * k, 5.5 * k);
            void Planet(double x, double y, double rad, string color) =>
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse(color)), null, new Point(x * k, y * k), rad * k, rad * k);
            Planet(52, 14, 5.5, "#FF7A5C");
            Planet(14, 38, 5, "#9B7CFF");
            Planet(38, 40, 4.5, "#3CC8B4");
        }
        return new WindowIcon(bmp);
    }
}
