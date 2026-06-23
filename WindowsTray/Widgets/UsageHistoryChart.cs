using System.Windows;
using System.Windows.Media;

namespace CodexBarTray;

/// <summary>
/// Draws a sampled usage-history line chart: faint 25/50/75 gridlines, an area
/// fill under the line, and the utilization line itself. Y is fixed to 0..100%;
/// X spans the sampled time domain (oldest → now). Points arrive pre-normalized
/// from <see cref="UsageHistoryWidgetViewModel"/>.
/// </summary>
public sealed class UsageHistoryChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<HistPoint>), typeof(UsageHistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HistPoint>? Points
    {
        get => (IReadOnlyList<HistPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static readonly Color Teal = Color.FromRgb(0x16, 0xD3, 0xB4);
    private static readonly Brush Grid = Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Line = Frozen(Teal);

    protected override void OnRender(DrawingContext dc)
    {
        if (Points is not { Count: >= 2 } points) return;

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double padT = 6, padB = 2, padL = 1, padR = 1;
        double X(double x) => padL + x * (w - padL - padR);
        double Y(double v) => padT + (1 - v / 100) * (h - padT - padB);

        // Gridlines at 25 / 50 / 75 %.
        var gridPen = new Pen(Grid, 1);
        foreach (var level in new[] { 25.0, 50.0, 75.0 })
            dc.DrawLine(gridPen, new Point(X(0), Y(level)), new Point(X(1), Y(level)));

        // Build the line path.
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(points[0].X), Y(points[0].Y)), isFilled: false, isClosed: false);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(new Point(X(points[i].X), Y(points[i].Y)), true, false);
        }
        line.Freeze();

        // Area fill: line path closed down to the baseline.
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(X(points[0].X), Y(0)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(X(points[0].X), Y(points[0].Y)), true, false);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(new Point(X(points[i].X), Y(points[i].Y)), true, false);
            ctx.LineTo(new Point(X(points[^1].X), Y(0)), true, false);
        }
        area.Freeze();

        var gradient = new LinearGradientBrush(
            Color.FromArgb(0x3A, Teal.R, Teal.G, Teal.B),
            Color.FromArgb(0x00, Teal.R, Teal.G, Teal.B),
            new Point(0, 0), new Point(0, 1));
        gradient.Freeze();
        dc.DrawGeometry(gradient, null, area);

        var linePen = new Pen(Line, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, linePen, line);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
