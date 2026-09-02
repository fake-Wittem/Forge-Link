// 文件说明：绘制历史查询结果中的数值时间序列预览。
// 责任边界：仅负责轻量渲染，不查询数据或提供表单交互。

using System.Globalization;
using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.Controls;

public sealed class HistoryTrendChart : FrameworkElement
{
    private INotifyCollectionChanged? _observableValues;
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<PointValue>), typeof(HistoryTrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public IEnumerable<PointValue>? Values
    {
        get => (IEnumerable<PointValue>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        HistoryTrendChart chart = (HistoryTrendChart)dependencyObject;
        if (chart._observableValues is not null) chart._observableValues.CollectionChanged -= chart.OnCollectionChanged;
        chart._observableValues = eventArgs.NewValue as INotifyCollectionChanged;
        if (chart._observableValues is not null) chart._observableValues.CollectionChanged += chart.OnCollectionChanged;
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect area = new(0, 0, ActualWidth, ActualHeight);
        Brush surfaceBrush = ResolveBrush("ForgeSurfaceBrush", Color.FromRgb(255, 249, 238));
        Brush borderBrush = ResolveBrush("ForgeBorderBrush", Color.FromRgb(207, 194, 172));
        Brush mutedBrush = ResolveBrush("ForgeMutedBrush", Color.FromRgb(104, 115, 122));
        Brush primaryBrush = ResolveBrush("ForgePrimaryBrush", Color.FromRgb(18, 59, 82));
        drawingContext.DrawRoundedRectangle(surfaceBrush, new Pen(borderBrush, 1), area, 6, 6);
        List<(DateTimeOffset Time, double Value)> points = Values?
            .Where(static value => value.EngineeringValue is not null && value.Quality == DataQuality.Good)
            .Select(static value => TryDouble(value.EngineeringValue, out double number)
                ? (Time: value.CollectTimestampUtc, Value: number, Valid: true)
                : (Time: value.CollectTimestampUtc, Value: 0D, Valid: false))
            .Where(static item => item.Valid)
            .Select(static item => (item.Time, item.Value))
            .OrderBy(static item => item.Time).ToList() ?? [];
        if (points.Count < 2)
        {
            DrawText(drawingContext, "至少需要 2 个有效数值点才能绘制趋势", new Point(20, Math.Max(20, ActualHeight / 2 - 8)), mutedBrush);
            return;
        }

        const double left = 58;
        const double top = 18;
        double right = Math.Max(left + 1, ActualWidth - 18);
        double bottom = Math.Max(top + 1, ActualHeight - 34);
        double minimum = points.Min(static point => point.Value);
        double maximum = points.Max(static point => point.Value);
        if (maximum.Equals(minimum)) { maximum += 0.5; minimum -= 0.5; }
        long firstTicks = points[0].Time.UtcTicks;
        long lastTicks = points[^1].Time.UtcTicks;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int index = 0; index < points.Count; index++)
            {
                double x = left + (points[index].Time.UtcTicks - firstTicks) / (double)Math.Max(1, lastTicks - firstTicks) * (right - left);
                double y = bottom - (points[index].Value - minimum) / (maximum - minimum) * (bottom - top);
                if (index == 0) context.BeginFigure(new(x, y), false, false); else context.LineTo(new(x, y), true, false);
            }
        }
        geometry.Freeze();
        Pen axis = new(borderBrush, 1);
        drawingContext.DrawLine(axis, new(left, top), new(left, bottom));
        drawingContext.DrawLine(axis, new(left, bottom), new(right, bottom));
        drawingContext.DrawGeometry(null, new Pen(primaryBrush, 2), geometry);
        DrawText(drawingContext, maximum.ToString("0.###", CultureInfo.CurrentCulture), new Point(8, top - 8), mutedBrush);
        DrawText(drawingContext, minimum.ToString("0.###", CultureInfo.CurrentCulture), new Point(8, bottom - 8), mutedBrush);
        DrawText(drawingContext, points[0].Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), new Point(left, bottom + 8), mutedBrush);
        DrawText(drawingContext, points[^1].Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), new Point(Math.Max(left, right - 54), bottom + 8), mutedBrush);
    }

    /// <summary>从全局主题读取画刷，并在资源缺失时使用同色系安全回退值。</summary>
    private Brush ResolveBrush(string resourceKey, Color fallbackColor) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallbackColor);

    private void DrawText(DrawingContext context, string text, Point origin, Brush brush) => context.DrawText(
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), origin);

    private static bool TryDouble(object? value, out double number)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Number } element && element.TryGetDouble(out number))
            return double.IsFinite(number);
        try { number = Convert.ToDouble(value, CultureInfo.InvariantCulture); return double.IsFinite(number); }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        { number = 0; return false; }
    }
}
