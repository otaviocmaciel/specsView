using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SpecsView.App.Theming;
using SpecsView.Core.Config;
using SpecsView.Core.Formatting;
using SpecsView.Core.Metrics;

namespace SpecsView.App.Rendering;

internal sealed record RenderRequest(
    AppConfig Config,
    MetricSnapshot Snapshot,
    MetricHistory History,
    Palette Palette,
    float Scale,
    int? FixedHeight);

/// <summary>
/// Draws the configured items into a 32bpp bitmap. Items flow column-major: with two rows, item 1
/// is top-left, item 2 bottom-left, item 3 top of the second column, and so on.
/// </summary>
internal sealed class WidgetRenderer : IDisposable
{
    private const float GraphicWidth = 32;
    private const float Padding = 4;
    private const float Gap = 4;

    private static readonly StringFormat MeasureFormat = CreateFormat(StringAlignment.Near);
    private static readonly StringFormat ValueFormat = CreateFormat(StringAlignment.Far);

    private Font? _font;
    private (string Family, float Pixels, bool Bold) _fontKey;

    /// <summary>Where each item was drawn in the last bitmap, for hit testing (tooltips).</summary>
    public IReadOnlyList<(RectangleF Bounds, ItemConfig Item)> LastLayout { get; private set; } = [];

    public Bitmap Render(RenderRequest request)
    {
        var hitAreas = new List<(RectangleF, ItemConfig)>();
        var scale = request.Scale;
        var layout = request.Config.Layout;
        var font = GetFont(request.Config.Theme.Font, scale);
        var cells = request.Config.Items.Select(item => CreateCell(item, request.Snapshot)).ToList();

        using var measureBitmap = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(measureBitmap);
        Configure(measure);

        var lineHeight = (float)Math.Ceiling(font.GetHeight(measure));
        var rows = Math.Max(1, layout.Rows);
        var columns = cells
            .Chunk(rows)
            .Select(column => MeasureColumn(column, rows, measure, font, scale))
            .ToList();

        var spacing = layout.ItemSpacing * scale;
        var width = Padding * scale * 2 + columns.Sum(c => c.Width) + spacing * Math.Max(0, columns.Count - 1);
        var height = request.FixedHeight ?? (int)Math.Ceiling(rows * lineHeight + Padding * scale * 2);

        var bitmap = new Bitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Configure(g);
        g.Clear(request.Palette.Background);

        var usedRows = Math.Clamp(columns.Select(c => c.Rows.Length).DefaultIfEmpty(1).Max(), 1, rows);
        var top = (height - usedRows * lineHeight) / 2;
        var x = Padding * scale;
        foreach (var column in columns)
        {
            for (var row = 0; row < column.Rows.Length; row++)
            {
                var bounds = new RectangleF(x, top + row * lineHeight, column.Width, lineHeight);
                var cell = row < column.Cells.Length ? column.Cells[row] : null;
                if (cell is not null)
                {
                    DrawCell(g, cell, column, bounds, font, scale, request);
                    hitAreas.Add((bounds, cell.Item));
                }

                // Hovering a value stacked under its item (e.g. CPU power on the RAM row) describes its own item.
                foreach (var (slot, owner) in DrawSecondaries(g, column.Rows[row], column, bounds, font, scale, request.Palette))
                {
                    if (owner != cell?.Item)
                    {
                        hitAreas.Insert(0, (slot, owner));
                    }
                }
            }

            x += column.Width + spacing;
        }

        LastLayout = hitAreas;
        return bitmap;
    }

    private static Cell CreateCell(ItemConfig item, MetricSnapshot snapshot)
    {
        var descriptor = snapshot.GetDescriptor(item.Metric);
        var value = snapshot.GetValue(item.Metric);
        var valueText = descriptor is null ? MetricFormatter.Unavailable : MetricFormatter.Format(value, descriptor.Unit);
        var widest = descriptor is null ? MetricFormatter.Unavailable : MetricFormatter.WidestSample(descriptor.Unit);

        var secondaries = (item.Secondary ?? [])
            .Select(id => snapshot.GetDescriptor(id) is { } d
                ? new SecondaryText(MetricFormatter.Format(snapshot.GetValue(id), d.Unit), MetricFormatter.WidestSample(d.Unit))
                : new SecondaryText(MetricFormatter.Unavailable, MetricFormatter.Unavailable))
            .ToList();

        return new Cell(item, descriptor?.Unit, value, item.Label, valueText, widest, secondaries);
    }

    private static Column MeasureColumn(Cell[] cells, int rows, Graphics g, Font font, float scale)
    {
        float Measure(string? text) => string.IsNullOrEmpty(text) ? 0 : g.MeasureString(text, font, PointF.Empty, MeasureFormat).Width;

        var labelWidth = cells.Max(c => Measure(c.Label));
        var valueWidth = cells.Max(c => Measure(c.WidestText));
        var graphicWidth = cells.Any(c => c.Item.Style != ItemStyle.Text) ? GraphicWidth * scale : 0;
        var placed = PlaceSecondaries(cells, rows);

        // Secondary values are aligned by position: every first secondary shares a slot, and so on.
        var secondaryWidths = Enumerable
            .Range(0, placed.Max(r => r.Count))
            .Select(i => placed.Max(r => i < r.Count ? Measure(r[i].Text.Widest) : 0))
            .ToArray();

        // Same advance as DrawCell: label, value, graphic, each followed by a gap.
        var secondaryX = (labelWidth > 0 ? labelWidth + Gap * scale : 0) + valueWidth + Gap * scale +
            (graphicWidth > 0 ? graphicWidth + Gap * scale : 0);

        var width = labelWidth + valueWidth;
        foreach (var slot in secondaryWidths.Prepend(graphicWidth).Prepend(labelWidth))
        {
            if (slot > 0)
            {
                width += Gap * scale;
            }
        }

        width += graphicWidth + secondaryWidths.Sum();
        return new Column(cells, placed, labelWidth, valueWidth, graphicWidth, secondaryWidths, secondaryX, width);
    }

    /// <summary>
    /// Decides which row of the column draws each secondary value. An item's first secondary stays
    /// on its own row; further ones move to the next free row below (CPU temperature, then CPU power
    /// under it on the RAM row), and stay beside the first only when no row below is free.
    /// </summary>
    private static List<PlacedSecondary>[] PlaceSecondaries(Cell[] cells, int rows)
    {
        var placed = Enumerable.Range(0, Math.Max(rows, cells.Length)).Select(_ => new List<PlacedSecondary>()).ToArray();
        for (var i = 0; i < cells.Length; i++)
        {
            placed[i].AddRange(cells[i].Secondaries.Take(1).Select(s => new PlacedSecondary(s, cells[i].Item)));
        }

        bool IsFree(int row) => placed[row].Count == 0 && (row >= cells.Length || cells[row].Secondaries.Count == 0);
        for (var i = 0; i < cells.Length; i++)
        {
            foreach (var secondary in cells[i].Secondaries.Skip(1))
            {
                var row = Enumerable.Range(i + 1, placed.Length - i - 1).Where(IsFree).DefaultIfEmpty(i).First();
                placed[row].Add(new PlacedSecondary(secondary, cells[i].Item));
            }
        }

        // Rows past the last item are only kept when something was stacked into them.
        var used = Math.Max(cells.Length, Array.FindLastIndex(placed, r => r.Count > 0) + 1);
        return placed[..used];
    }

    private static void DrawCell(Graphics g, Cell cell, Column column, RectangleF bounds, Font font, float scale, RenderRequest request)
    {
        var palette = request.Palette;
        var baseColor = Palette.ParseColor(cell.Item.Color, palette.Foreground);
        var valueColor = cell.Unit == MetricUnit.Percent ? palette.ForPercent(cell.Value, baseColor) : baseColor;

        var x = bounds.X;
        if (column.LabelWidth > 0)
        {
            using var labelBrush = new SolidBrush(palette.Foreground);
            g.DrawString(cell.Label, font, labelBrush, new PointF(x, bounds.Y), MeasureFormat);
            x += column.LabelWidth + Gap * scale;
        }

        using (var valueBrush = new SolidBrush(valueColor))
        {
            g.DrawString(cell.ValueText, font, valueBrush, new RectangleF(x, bounds.Y, column.ValueWidth, bounds.Height), ValueFormat);
        }

        x += column.ValueWidth + Gap * scale;
        if (column.GraphicWidth > 0)
        {
            if (cell.Item.Style != ItemStyle.Text)
            {
                var graphicColor = valueColor == baseColor ? palette.Accent : valueColor;
                DrawGraphic(g, cell, new RectangleF(x, bounds.Y, column.GraphicWidth, bounds.Height), graphicColor, scale, request);
            }

            // Text-only items still reserve the graphic slot so secondary values stay aligned.
            x += column.GraphicWidth + Gap * scale;
        }

    }

    private static List<(RectangleF Slot, ItemConfig Owner)> DrawSecondaries(
        Graphics g, List<PlacedSecondary> secondaries, Column column, RectangleF bounds, Font font, float scale, Palette palette)
    {
        var drawn = new List<(RectangleF, ItemConfig)>();
        using var brush = new SolidBrush(palette.Foreground);
        var x = bounds.X + column.SecondaryX;
        for (var i = 0; i < secondaries.Count; i++)
        {
            var slot = new RectangleF(x, bounds.Y, column.SecondaryWidths[i], bounds.Height);
            g.DrawString(secondaries[i].Text.Text, font, brush, slot, ValueFormat);
            drawn.Add((slot, secondaries[i].Owner));
            x += slot.Width + Gap * scale;
        }

        return drawn;
    }

    private static void DrawGraphic(Graphics g, Cell cell, RectangleF slot, Color color, float scale, RenderRequest request)
    {
        var history = request.History.Get(cell.Item.Metric);
        var max = cell.Unit == MetricUnit.Percent ? 100 : Math.Max(1, history.DefaultIfEmpty(0).Max());

        if (cell.Item.Style == ItemStyle.TextBar)
        {
            var barHeight = slot.Height * 0.45f;
            var track = slot with { Y = slot.Y + (slot.Height - barHeight) / 2, Height = barHeight };
            DrawBar(g, track, (cell.Value ?? 0) / max, request.Palette.Foreground, color);
        }
        else
        {
            var chartHeight = slot.Height * 0.8f;
            var area = slot with { Y = slot.Y + (slot.Height - chartHeight) / 2, Height = chartHeight };
            DrawSparkline(g, area, history, max, request.History.Capacity, color, scale);
        }
    }

    private static void DrawBar(Graphics g, RectangleF track, double fraction, Color foreground, Color fill)
    {
        using var trackBrush = new SolidBrush(Color.FromArgb(60, foreground));
        g.FillRectangle(trackBrush, track);

        var filled = track with { Width = (float)(track.Width * Math.Clamp(fraction, 0, 1)) };
        using var fillBrush = new SolidBrush(fill);
        g.FillRectangle(fillBrush, filled);
    }

    private static void DrawSparkline(Graphics g, RectangleF area, double[] values, double max, int capacity, Color color, float scale)
    {
        using var baseline = new Pen(Color.FromArgb(60, color), 1);
        g.DrawLine(baseline, area.Left, area.Bottom, area.Right, area.Bottom);
        if (values.Length < 2)
        {
            return;
        }

        // Newest point is pinned to the right edge; the line grows leftwards as history fills up.
        var step = area.Width / (capacity - 1);
        var points = new PointF[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var ratio = (float)Math.Clamp(values[i] / max, 0, 1);
            points[i] = new PointF(area.Right - (values.Length - 1 - i) * step, area.Bottom - ratio * area.Height);
        }

        using var path = new GraphicsPath();
        path.AddLines(points);
        path.AddLine(points[^1], new PointF(points[^1].X, area.Bottom));
        path.AddLine(new PointF(points[^1].X, area.Bottom), new PointF(points[0].X, area.Bottom));
        path.CloseFigure();

        using var fill = new SolidBrush(Color.FromArgb(70, color));
        g.FillPath(fill, path);
        using var line = new Pen(color, Math.Max(1, scale));
        g.DrawLines(line, points);
    }

    private Font GetFont(FontConfig config, float scale)
    {
        var pixels = config.Size * scale * 96f / 72f;
        var key = (config.Family, pixels, config.Bold);
        if (_font is null || key != _fontKey)
        {
            _font?.Dispose();
            _font = new Font(config.Family, pixels, config.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            _fontKey = key;
        }

        return _font;
    }

    private static void Configure(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // ClearType needs an opaque background; grayscale AA is the right choice over transparency.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static StringFormat CreateFormat(StringAlignment alignment)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = alignment;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return format;
    }

    public void Dispose() => _font?.Dispose();

    private sealed record Cell(
        ItemConfig Item,
        MetricUnit? Unit,
        double? Value,
        string Label,
        string ValueText,
        string WidestText,
        IReadOnlyList<SecondaryText> Secondaries);

    private sealed record SecondaryText(string Text, string Widest);

    private sealed record PlacedSecondary(SecondaryText Text, ItemConfig Owner);

    /// <param name="Rows">Secondary values per row; longer than <paramref name="Cells"/> when a value is stacked under the last item.</param>
    /// <param name="SecondaryX">Offset of the first secondary slot from the column's left edge.</param>
    private sealed record Column(
        Cell[] Cells,
        List<PlacedSecondary>[] Rows,
        float LabelWidth,
        float ValueWidth,
        float GraphicWidth,
        float[] SecondaryWidths,
        float SecondaryX,
        float Width);
}
