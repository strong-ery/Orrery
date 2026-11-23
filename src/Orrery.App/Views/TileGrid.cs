using Avalonia;
using Avalonia.Controls;

namespace Orrery.App.Views;

/// <summary>
/// A row of equal tiles that wraps onto more rows when the space gets narrow, instead of squeezing every tile until
/// its text breaks. As the window is dragged, the number of tiles per row changes and the tiles fill the width.
/// </summary>
public sealed class TileGrid : Panel
{
    public static readonly StyledProperty<double> MinTileWidthProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(MinTileWidth), 150);
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(Spacing), 8);

    static TileGrid() => AffectsMeasure<TileGrid>(MinTileWidthProperty, SpacingProperty);

    public double MinTileWidth { get => GetValue(MinTileWidthProperty); set => SetValue(MinTileWidthProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private (int Columns, double TileWidth) Fit(double width, int count)
    {
        if (double.IsInfinity(width) || width <= 0)
            width = MinTileWidth * count + Spacing * (count - 1);
        var columns = Math.Clamp((int)((width + Spacing) / (MinTileWidth + Spacing)), 1, Math.Max(1, count));
        // Never leave a lone tile on the last row if an even split is possible.
        while (columns > 1 && count % columns == 1 && count > columns)
            columns--;
        return (columns, (width - Spacing * (columns - 1)) / columns);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Children.Where(c => c.IsVisible).ToList();
        if (items.Count == 0)
            return default;
        var (columns, tileWidth) = Fit(availableSize.Width, items.Count);
        var total = 0.0;
        for (var start = 0; start < items.Count; start += columns)
        {
            var rowHeight = 0.0;
            foreach (var child in items.Skip(start).Take(columns))
            {
                child.Measure(new Size(tileWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            total += rowHeight + (start > 0 ? Spacing : 0);
        }
        return new Size(columns * tileWidth + (columns - 1) * Spacing, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Children.Where(c => c.IsVisible).ToList();
        if (items.Count == 0)
            return finalSize;
        var (columns, tileWidth) = Fit(finalSize.Width, items.Count);
        var y = 0.0;
        for (var start = 0; start < items.Count; start += columns)
        {
            var row = items.Skip(start).Take(columns).ToList();
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (tileWidth + Spacing), y, tileWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
