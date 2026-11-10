using System.Reflection;
using Avalonia.Logging;
using Picshare.Services;
using Picshare.Views;

namespace Picshare.Tests;

public sealed class SettingsViewLayoutTests
{
    private const double LabelWidth = 150;
    private const double CacheColumnWidth = (696 - LabelWidth - 16) / 3;
    private const double PeriodLabelWidth = 70;
    private const double EllipsisWidth = 14;

    [Fact]
    public void WideRow_AlignsPeriodLabelWithCacheInputsAndGivesExtraSpaceToNumeric()
    {
        var normal = Calculate(696);
        var wider = Calculate(760);
        Assert.Equal(LabelWidth, normal.ButtonSlot + normal.Spacing, 6);
        Assert.Equal(CacheColumnWidth, normal.Units, 6);
        Assert.True(normal.Numeric > normal.Units);
        Assert.True(normal.ButtonSlot < CacheColumnWidth); // Cap the button to preserve label alignment.
        Assert.Equal(normal.ButtonSlot, wider.ButtonSlot);
        Assert.Equal(normal.Label, wider.Label);
        Assert.Equal(normal.Units, wider.Units);
        Assert.Equal(normal.Numeric + 64, wider.Numeric, 6);
        Assert.Equal(696, TotalWidth(normal), 6);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(500)]
    [InlineData(600)]
    public void NarrowRows_ShrinkControlsByTheSameFactorAndKeepEllipsisAtRightEdge(double rowWidth)
    {
        var widths = Calculate(rowWidth);
        var scale = widths.Units / CacheColumnWidth;
        Assert.InRange(scale, 0, 1);
        Assert.Equal(widths.Units, widths.Numeric, 6);
        Assert.Equal(scale, widths.ButtonSlot / (LabelWidth - 8), 6);
        Assert.Equal(scale, widths.Label / PeriodLabelWidth, 6);
        Assert.Equal(EllipsisWidth, widths.Ellipsis);
        Assert.Equal(rowWidth, TotalWidth(widths), 6);
    }

    [Fact]
    public void ShrinkThreshold_IsContinuousWhenNumericReachesUnitsWidth()
    {
        var threshold = LabelWidth - 8 + PeriodLabelWidth + CacheColumnWidth * 2 + EllipsisWidth + 8 * 4;
        var above = Calculate(threshold + 0.01);
        var at = Calculate(threshold);
        var below = Calculate(threshold - 0.01);
        Assert.Equal(CacheColumnWidth + 0.01, above.Numeric, 6);
        Assert.Equal(CacheColumnWidth, at.Numeric, 6);
        Assert.Equal(at.Numeric, at.Units, 6);
        Assert.Equal(below.Numeric, below.Units, 6);
        Assert.True(below.Units < at.Units);
        Assert.InRange(at.ButtonSlot - below.ButtonSlot, 0, 0.01);
    }

    [Fact]
    public void TraceLogging_AcceptsAvaloniaVerboseEventsAndHigherLevels()
    {
        var type = typeof(DiagnosticLogging).GetNestedType("DiagnosticAvaloniaSink", BindingFlags.NonPublic)!;
        var sink = (ILogSink)Activator.CreateInstance(type, nonPublic: true)!;
        Assert.True(sink.IsEnabled(LogEventLevel.Verbose, "Layout"));
        Assert.True(sink.IsEnabled(LogEventLevel.Debug, "Binding"));
        Assert.True(sink.IsEnabled(LogEventLevel.Warning, "Platform"));
    }

    private static (double ButtonSlot, double Label, double Numeric, double Units, double Ellipsis, double Spacing) Calculate(double width)
    {
        var method = typeof(SettingsView).GetMethod("CalculateDiagnosticsColumnWidths", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((double, double, double, double, double, double))method.Invoke(null,
            [width, LabelWidth, CacheColumnWidth, PeriodLabelWidth, EllipsisWidth])!;
    }

    private static double TotalWidth((double ButtonSlot, double Label, double Numeric, double Units, double Ellipsis, double Spacing) widths) =>
        widths.ButtonSlot + widths.Label + widths.Numeric + widths.Units + widths.Ellipsis + widths.Spacing * 4;
}
