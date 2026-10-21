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
    public IReadOnlyList<(RectangleF Bounds, string MetricId)> LastLayout { get; private set; } = [];

    public Bitmap Render(RenderRequest request)
    {
        var hitAreas = new List<(RectangleF, string)>();
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
            .Select(column => MeasureColumn(column, measure, font, scale))
            .ToList();

        var spacing = layout.ItemSpacing * scale;
        var width = Padding * scale * 2 + columns.Sum(c => c.Width) + spacing * Math.Max(0, columns.Count - 1);
        var height = request.FixedHeight ?? (int)Math.Ceiling(rows * lineHeight + Padding * scale * 2);

        var bitmap = new Bitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Configure(g);
        g.Clear(request.Palette.Background);

        var usedRows = Math.Min(rows, Math.Max(1, cells.Count));
        var top = (height - usedRows * lineHeight) / 2;
        var x = Padding * scale;
        foreach (var column in columns)
        {
            var y = top;
            foreach (var cell in column.Cells)
            {
                var bounds = new RectangleF(x, y, column.Width, lineHeight);
                DrawCell(g, cell, column, bounds, font, scale, request);
                hitAreas.Add((bounds, cell.Item.Metric));
                y += lineHeight;
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
        return new Cell(item, descriptor?.Unit, value, item.Label, valueText, widest);
    }

    private static Column MeasureColumn(Cell[] cells, Graphics g, Font font, float scale)
    {
        var labelWidth = cells.Max(c => c.Label.Length == 0 ? 0 : g.MeasureString(c.Label, font, PointF.Empty, MeasureFormat).Width);
        var valueWidth = cells.Max(c => g.MeasureString(c.WidestText, font, PointF.Empty, MeasureFormat).Width);
        var graphicWidth = cells.Any(c => c.Item.Style != ItemStyle.Text) ? GraphicWidth * scale : 0;

        var width = labelWidth + valueWidth;
        if (labelWidth > 0)
        {
            width += Gap * scale;
        }

        if (graphicWidth > 0)
        {
            width += Gap * scale + graphicWidth;
        }

        return new Column(cells, labelWidth, valueWidth, graphicWidth, width);
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
        if (column.GraphicWidth <= 0 || cell.Item.Style == ItemStyle.Text)
        {
            return;
        }

        var graphicColor = valueColor == baseColor ? palette.Accent : valueColor;
        var history = request.History.Get(cell.Item.Metric);
        var max = cell.Unit == MetricUnit.Percent ? 100 : Math.Max(1, history.DefaultIfEmpty(0).Max());

        if (cell.Item.Style == ItemStyle.TextBar)
        {
            var barHeight = bounds.Height * 0.45f;
            var track = new RectangleF(x, bounds.Y + (bounds.Height - barHeight) / 2, column.GraphicWidth, barHeight);
            DrawBar(g, track, (cell.Value ?? 0) / max, palette.Foreground, graphicColor);
        }
        else
        {
            var chartHeight = bounds.Height * 0.8f;
            var area = new RectangleF(x, bounds.Y + (bounds.Height - chartHeight) / 2, column.GraphicWidth, chartHeight);
            DrawSparkline(g, area, history, max, request.History.Capacity, graphicColor, scale);
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

    private sealed record Cell(ItemConfig Item, MetricUnit? Unit, double? Value, string Label, string ValueText, string WidestText);

    private sealed record Column(Cell[] Cells, float LabelWidth, float ValueWidth, float GraphicWidth, float Width);
}
