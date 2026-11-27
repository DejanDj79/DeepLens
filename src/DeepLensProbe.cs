using System.ComponentModel;
using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
using VolumetricaControls;
using VolumetricaCore;
using static VolSysAPI.ExternalStructure;
using static VolSysAPI.Structure;

namespace DeepLens;

public class DeepLensProbe : Indicator
{
    public static IndicatorDescriptionBase Register()
    {
        return new IndicatorDescriptionBase
        {
            Name = "DeepLens Probe",
            Description = "Independent Renko 30|1 footprint with live and historical range selection.",
            Tags = new List<string> { "Utility" }
        };
    }

    [Category("Layout")]
    [DisplayName("Zoom (%)")]
    [VolCustom(MinValue = 60, MaxValue = 180, IncrementValue = 10)]
    public int Zoom { get; set; } = 100;

    [Category("Layout")]
    [DisplayName("Detail offset (ticks) - inactive with auto fit")]
    [Browsable(false)]
    [VolCustom(MinValue = -10000, MaxValue = 10000, IncrementValue = 1)]
    public int DetailOffset { get; set; } = 0;

    [Category("Deep Print diagnostic")]
    [DisplayName("Inspect indicator index")]
    [VolCustom(MinValue = 0, MaxValue = 50, IncrementValue = 1)]
    public int InspectIndex { get; set; } = 2;

    [Category("Deep Print diagnostic")]
    [DisplayName("Show all parameters")]
    public bool ShowAllParameters { get; set; } = false;

    [Category("Deep Print diagnostic")]
    [DisplayName("Expanded parameter index (-1 = overview)")]
    [VolCustom(MinValue = -1, MaxValue = 200, IncrementValue = 1)]
    public int ExpandedParameter { get; set; } = -1;

    [Category("Deep Print diagnostic")]
    [DisplayName("Expanded page")]
    [VolCustom(MinValue = 0, MaxValue = 100, IncrementValue = 1)]
    public int ExpandedPage { get; set; } = 0;

    [Category("Layout")]
    [DisplayName("Native shapes test")]
    public bool NativeShapes { get; set; } = true;

    private readonly List<(IAnnotation Shape, double X1, double X2, double Y1, double Y2, double Y3, double Close, bool Candle)> projected = new();
    private readonly List<(IAnnotation Label, double X, double Y, int Column, int Row)> projectedLabels = new();
    private double nativeLabelStep, nativeLabelWidth;
    private int nativeLabelPreferredFont, nativeLabelFallbackFont, nativeLabelFallbackStride;
    [Category("Layout")]
    [DisplayName("Show panel background")]
    public bool ShowPanelBackground { get; set; } = true;

    [Category("Layout")]
    [DisplayName("Panel X (%)")]
    [VolCustom(MinValue = 0, MaxValue = 99, IncrementValue = 1)]
    public int PanelX { get; set; } = 8;

    [Category("Layout")]
    [DisplayName("Panel Y (%)")]
    [VolCustom(MinValue = 0, MaxValue = 99, IncrementValue = 1)]
    public int PanelY { get; set; } = 12;

    [Category("Layout")]
    [DisplayName("Panel width (%)")]
    [VolCustom(MinValue = 18, MaxValue = 95, IncrementValue = 1)]
    public int PanelWidth { get; set; } = 28;

    [Category("Layout")]
    [DisplayName("Panel height (%)")]
    [VolCustom(MinValue = 20, MaxValue = 95, IncrementValue = 1)]
    public int PanelHeight { get; set; } = 70;

    [Category("Selection")]
    [DisplayName("Selected zone (off = Live Follow)")]
    public bool UseSelectedZone { get; set; } = false;

    [Category("Selection")]
    [DisplayName("Zone start (main bars ago)")]
    [VolCustom(MinValue = 0, MaxValue = 100000, IncrementValue = 1)]
    public int ZoneStartBarsAgo { get; set; } = 5;

    [Category("Selection")]
    [DisplayName("Zone end (main bars ago)")]
    [VolCustom(MinValue = 0, MaxValue = 100000, IncrementValue = 1)]
    public int ZoneEndBarsAgo { get; set; } = 1;

    [Category("Selection")]
    [DisplayName("Live candles")]
    [VolCustom(MinValue = 1, MaxValue = 12, IncrementValue = 1)]
    public int LiveCandles { get; set; } = 3;

    [Category("Selection")]
    [DisplayName("Zone candles per page")]
    [VolCustom(MinValue = 1, MaxValue = 12, IncrementValue = 1)]
    public int ZoneCandlesPerPage { get; set; } = 6;

    [Category("Selection")]
    [DisplayName("Zone page (0 = first)")]
    [VolCustom(MinValue = 0, MaxValue = 100000, IncrementValue = 1)]
    public int ZonePage { get; set; } = 0;

    private bool zoneCaptured;
    private int capturedStartAgo = -1, capturedEndAgo = -1;
    private DateTime zoneStartTime, zoneEndExclusive;
    private DateTime? menuStartTime;
    private IAnnotation zoneMarker;
    private IAnnotation modeLabel;
    private bool panelHidden;
    private int zonePageCount, activeZonePage;
    // Copy raw OHLC and footprint numbers, not mutable SDK bar references.
    private sealed class LensBar
    {
        public int Index, OpenInT, HighInT, LowInT, CloseInT;
        public readonly List<(int PinT, long BidVol, long AskVol, long TotVol)> Levels = new();
    }
    private List<LensBar> lastView;
    private List<LensBar> frozenView;

    private ResolutionData renko;
    private int renkoFilterIndex = -1;
    private IAnnotation panelBackground;
    private IAnnotation settingsStatus;
    private IAnnotation projectionStatus;
    private int resizeCalls;
    private int resolvedSourceIndex = -1;
    private int lastRequestedIndex = -1;
    private readonly IAnnotation[,] profileRects = new IAnnotation[MaxColumns, MaxRows];
    private readonly IAnnotation[,] secondaryRects = new IAnnotation[MaxColumns, MaxRows];
    private readonly IAnnotation[] nativeCandles = new IAnnotation[MaxColumns];
    private readonly IAnnotation[] parameterColumns = new IAnnotation[3];
    private const int MaxColumns = 12;
    private const int InitialColumns = 3;
    private const int MaxRows = 512;
    private const int InitialRows = 40;
    [Category("Deep Print diagnostic")]
    [DisplayName("Show diagnostics")]
    public bool ShowDiagnostics { get; set; } = false;

    private IAnnotation label;
    private readonly IAnnotation[,] bidLabels = new IAnnotation[MaxColumns, MaxRows];
    private readonly IAnnotation[,] askLabels = new IAnnotation[MaxColumns, MaxRows];
    private readonly IAnnotation[] candleMarkers = new IAnnotation[MaxColumns];

    private IAnnotation MakeText(double x, double y, int size)
    {
        var a = VAn.CreateAnnotation(AnnotationType.Text);
        a.CoordinateXType = CoordinateTypeEnum.Relative;
        a.CoordinateYType = CoordinateTypeEnum.Relative;
        a.X = x; a.Y = y;
        a.TextAlign = TextAlignment.VLeftHTop;
        a.FontSize = size;
        a.PlotForeground = true;
        a.ForeColor = ColorRef.FromRgb(235, 240, 245);
        VAn.AddAnnotation(IndVars.FrontAnnList, a);
        return a;
    }

    private IAnnotation MakeShape(AnnotationType type)
    {
        var a = VAn.CreateAnnotation(type);
        a.Area = ChartAreaRef.Main;
        a.CoordinateXType = CoordinateTypeEnum.Absolute;
        a.CoordinateYType = CoordinateTypeEnum.Absolute;
        a.UseAxisWithSpace = false;
        a.PlotForeground = true;
        a.Margin = MarginEnum.None;
        a.PixelMargin = 0;
        a.MinPxForMargin = 0;
        a.Visible = false;
        VAn.AddAnnotation(IndVars.Ann_List, a);
        return a;
    }

    // Map overlay positions into the visible chart axes for native shapes.
    private void ProjectShapes()
    {
        if (projectionStatus == null || projected.Count == 0) return;
        if (AxisX == null || AxisY == null)
        {
            projectionStatus.Text = $"AxisX null: {AxisX == null} | AxisY null: {AxisY == null} | shapes {projected.Count}";
            return;
        }
        double xmin = AxisX.ViewMinimum, xmax = AxisX.ViewMaximum;
        double ymin = AxisY.ViewMinimum, ymax = AxisY.ViewMaximum;
        projectionStatus.Text = $"X: {xmin:0.##} .. {xmax:0.##} | Y: {ymin:0.##} .. {ymax:0.##}\nShapes: {projected.Count} | Resize calls: {resizeCalls}";
        if (!double.IsFinite(xmin) || !double.IsFinite(xmax) ||
            !double.IsFinite(ymin) || !double.IsFinite(ymax) ||
            xmax <= xmin || ymax <= ymin) return;
        double X(double v) => xmin + v * (xmax - xmin);
        double Y(double v) => ymax - v * (ymax - ymin);
        foreach (var p in projected)
        {
            p.Shape.X = X(p.X1);
            p.Shape.X2 = X(p.X2);
            p.Shape.Y = Y(p.Y1);
            p.Shape.Y2 = Y(p.Y2);
            if (p.Candle)
            {
                p.Shape.Y3 = Y(p.Y3);
                p.Shape.Height = Y(p.Close) - p.Shape.Y;
            }
            p.Shape.Visible = true;
        }
        // Actual area dimensions are available on desktop, including after a chart resize.
        // Use a conservative text-size estimate, not the previous chart-relative font formula.
        double pixelWidth = 0, pixelHeight = 0;
        try
        {
            var measures = VAn.GetChartAreaMeasures(ChartAreaRef.Main);
            pixelWidth = measures.PixelWidth;
            pixelHeight = measures.PixelHeight;
        }
        catch (System.NotSupportedException) { }
        catch (System.NotImplementedException) { }
        bool pixelFit = double.IsFinite(pixelWidth) && double.IsFinite(pixelHeight) &&
            pixelWidth > 0 && pixelHeight > 0 && nativeLabelStep > 0 && nativeLabelWidth > 0;
        double rowPixels = nativeLabelStep * pixelHeight;
        double columnPixels = nativeLabelWidth * pixelWidth;
        int fontSize = nativeLabelFallbackFont;
        int strideRows = nativeLabelFallbackStride;
        if (pixelFit)
        {
            fontSize = Math.Clamp((int)Math.Floor(rowPixels / 1.5), 6, nativeLabelPreferredFont);
            // Preserve one common font for every candle; reserve horizontal room for the longest number.
            int maxChars = 1;
            foreach (var p in projectedLabels)
                maxChars = Math.Max(maxChars, p.Label.Text.Length);
            fontSize = Math.Clamp((int)Math.Floor((columnPixels - 2) / maxChars), 6, fontSize);
            strideRows = (int)Math.Min(int.MaxValue, Math.Max(1, Math.Ceiling(fontSize * 1.5 / rowPixels)));
        }
        var lastRows = new int[MaxColumns * 2];
        for (int i = 0; i < lastRows.Length; i++) lastRows[i] = int.MinValue;
        foreach (var p in projectedLabels)
        {
            int lastRow = lastRows[p.Column];
            p.Label.CoordinateXType = CoordinateTypeEnum.Absolute;
            p.Label.CoordinateYType = CoordinateTypeEnum.Absolute;
            p.Label.UseAxisWithSpace = false;
            p.Label.X = X(p.X);
            // Center labels vertically on their footprint row when pixel dimensions are available.
            p.Label.Y = Y(pixelFit ? p.Y - fontSize * .75 / pixelHeight : p.Y - nativeLabelStep * .35);
            p.Label.FontSize = fontSize;
            bool widthFits = !pixelFit || p.Label.Text.Length * fontSize + 2 <= columnPixels;
            bool show = widthFits && (lastRow == int.MinValue || (long)p.Row - lastRow >= strideRows);
            p.Label.Visible = show;
            if (show) lastRows[p.Column] = p.Row;
        }
        if (pixelFit)
            projectionStatus.Text += $"\nArea: {pixelWidth:0} x {pixelHeight:0} px | row {rowPixels:0.0}px | font {fontSize} | stride {strideRows}";
        if (projected.Count > 0)
        {
            var p = projected[0];
            projectionStatus.Text += $"\nFirst { (p.Candle ? "candle" : "rectangle") }: X {p.Shape.X:0.##} .. {p.Shape.X2:0.##} Y {p.Shape.Y:0.##} .. {p.Shape.Y2:0.##}";
        }
    }

    public override void OnResizeAxis(bool isFinal)
    {
        resizeCalls++;
        ProjectShapes();
    }

    public override void OnSet(bool setDefault, bool themeOverride)
    {
        OnEndCall = CallHandler.HistRT;
        OnResizeCall = CallHandler.HistRT;
        NeedVbp = true;
        Description = "DeepLens v0.6.28";
    }

    public override void OnInit()
    {
        // Independent tick-built bars for the same instrument, regardless of the main chart resolution.
        // Request on every initialization, before the platform prepares its history.
        renko = VAn.RequestResolution(ChartResolutionInfo.ParamTypeEnum.Renko, 30, 1,
            null, false);
        renkoFilterIndex = renko == null ? -1 :
            renko.AddNewFilter(0, 0, DataInputTypeEnum.Volume, VbpType.Bar, 0, true);
    }

    public override void OnLoad()
    {
        projected.Clear();
        projectedLabels.Clear();
        // Deepchart rebuilds the annotation groups when settings change.
        // Do not reuse annotations belonging to the previous load.
        for (int c = 0; c < MaxColumns; c++)
        {
            for (int k = 0; k < MaxRows; k++)
            {
                profileRects[c,k] = null;
                secondaryRects[c,k] = null;
                bidLabels[c,k] = null;
                askLabels[c,k] = null;
            }
        }
        resolvedSourceIndex = -1;
        lastRequestedIndex = -1;
        // Register first in the same foreground annotation group as the native shapes.
        // This puts the panel behind the candles and profiles in insertion order.
        zoneMarker = MakeShape(AnnotationType.Rectangle);
        zoneMarker.BackColor = ColorRef.FromRgb(80, 165, 245).WithOpacity(28);
        zoneMarker.LineColor = ColorRef.FromRgb(80, 165, 245);
        zoneMarker.LineWidth = 1;
        panelBackground = MakeShape(AnnotationType.Rectangle);
        panelBackground.BackColor = ColorRef.FromRgb(18, 21, 27);
        panelBackground.LineColor = ColorRef.FromRgb(82, 88, 99);
        panelBackground.LineWidth = 1;
        modeLabel = MakeText(.08, .08, 10);
        modeLabel.Visible = false;
        projectionStatus = MakeText(.48, .32, 10);
        projectionStatus.BackColor = ColorRef.FromRgb(25, 30, 38);
        resizeCalls = 0;
        label = MakeText(.09, .05, 12);
        label.Text = "DeepLens v0.6.28 - waiting";
        label.Visible = ShowDiagnostics || ShowAllParameters;
        projectionStatus.Visible = ShowDiagnostics && NativeShapes && !ShowAllParameters;
        settingsStatus = MakeText(.09, .045, 10);
        settingsStatus.X = .48;
        settingsStatus.BackColor = ColorRef.FromRgb(25, 30, 38);
        settingsStatus.LineColor = ColorRef.FromRgb(95, 105, 120);
        settingsStatus.LineWidth = 1;
        settingsStatus.Visible = ShowDiagnostics || ShowAllParameters;
        for (int i = 0; i < 3; i++)
        {
            parameterColumns[i] = MakeText(.09 + i * .29, .11, 11);
            parameterColumns[i].BackColor = ColorRef.FromRgb(25, 30, 38);
            parameterColumns[i].LineColor = ColorRef.FromRgb(95, 105, 120);
            parameterColumns[i].LineWidth = 1;
            parameterColumns[i].Visible = false;
        }
        for (int c = 0; c < MaxColumns; c++)
        {
            nativeCandles[c] = null;
            candleMarkers[c] = null;
            if (c < InitialColumns)
                EnsureColumn(c);
        }
    }

    private void EnsureColumn(int c)
    {
        if (nativeCandles[c] != null) return;
        nativeCandles[c] = MakeShape(AnnotationType.Candlestick);
        candleMarkers[c] = MakeText(.10, .14, 10);
        candleMarkers[c].Visible = false;
        for (int k = 0; k < InitialRows; k++) EnsureRow(c, k);
    }

    private void EnsureRow(int c, int k)
    {
        if (profileRects[c,k] != null) return;
        profileRects[c,k] = MakeShape(AnnotationType.Rectangle);
        bidLabels[c,k] = MakeText(.10, .14, 10);
        askLabels[c,k] = MakeText(.10, .14, 10);
        bidLabels[c,k].LineWidth = askLabels[c,k].LineWidth = 1;
        bidLabels[c,k].FontBold = true;
        askLabels[c,k].FontBold = false;
        bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
    }

    private void ConfigureValueLabel(IAnnotation label, long value, ColorRef foreground, ColorRef transparent, int font)
    {
        label.FontSize = Math.Min(25, font + 1);
        label.FontBold = false;
        label.Text = value.ToString();
        label.ForeColor = foreground;
        label.BackColor = label.LineColor = transparent;
        label.LineWidth = 0;
        label.Visible = false;
    }

    private void ClearRows()
    {
        panelBackground.Visible = false;
        if (modeLabel != null) modeLabel.Visible = false;
        for (int k = 0; k < MaxRows; k++)
        {
            for (int c = 0; c < MaxColumns; c++)
            {
                if (profileRects[c,k] == null) continue;
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
                profileRects[c,k].Visible = false;
                if (secondaryRects[c,k] != null) secondaryRects[c,k].Visible = false;
            }
        }
        for (int c = 0; c < MaxColumns; c++)
        {
            if (nativeCandles[c] == null) continue;
            candleMarkers[c].Visible = false;
            nativeCandles[c].Visible = false;
        }
        projected.Clear();
        projectedLabels.Clear();
    }

    // GetParamsById can throw rather than return null when a chart has fewer indicators.
    private BaseIndicatorPrms TryGetParams(int index)
    {
        if (index < 0) return null;
        try
        {
            return VAn.GetParamsById(index);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (System.IndexOutOfRangeException)
        {
            return null;
        }
    }

    // Match the supported renderer configuration rather than just the indicator name.
    private bool IsSupportedSource(BaseIndicatorPrms source)
    {
        if (source == null || source.name != "Deep Print" || source.XMLPropList == null) return false;
        int type = -1, mode = -1, input = -1, grouping = -1, groupMode = -1;
        int ticks = 0, min = -1, max = -1, proportion = -1;
        foreach (var prop in source.XMLPropList)
        {
            if (prop.Value == null || !int.TryParse(prop.Value.ToString(), out int value)) continue;
            if (prop.Name == "FootType") type = value;
            if (prop.Name == "ShowStyle") mode = value;
            if (prop.Name == "InputData") input = value;
            if (prop.Name == "AutoGrouping") grouping = value;
            if (prop.Name == "GroupMode") groupMode = value;
            if (prop.Name == "ManualTicks") ticks = value;
            if (prop.Name == "FilterMin") min = value;
            if (prop.Name == "FilterMax") max = value;
            if (prop.Name == "FootProportion") proportion = value;
        }
        return type >= 0 && type <= 3 && mode == 1 && input == 0 && grouping == 1 &&
            groupMode == 0 && ticks >= 1 && ticks <= 10000 && min == 0 && max == 0 && proportion == 0;
    }

    private BaseIndicatorPrms ResolveDeepPrint()
    {
        if (lastRequestedIndex != InspectIndex)
        {
            lastRequestedIndex = InspectIndex;
            resolvedSourceIndex = -1;
        }
        var requested = TryGetParams(InspectIndex);
        if (IsSupportedSource(requested))
        {
            resolvedSourceIndex = InspectIndex;
            return requested;
        }
        if (resolvedSourceIndex >= 0 && resolvedSourceIndex != InspectIndex)
        {
            var cached = TryGetParams(resolvedSourceIndex);
            if (IsSupportedSource(cached)) return cached;
        }

        // Keep the preferred source for diagnostics if none of the instances matches.
        BaseIndicatorPrms fallback = requested != null && requested.name == "Deep Print" ? requested : null;
        int fallbackIndex = fallback == null ? -1 : InspectIndex;
        for (int i = 0; i <= 50; i++)
        {
            if (i == InspectIndex) continue;
            var candidate = TryGetParams(i);
            if (candidate == null || candidate.name != "Deep Print") continue;
            if (IsSupportedSource(candidate))
            {
                resolvedSourceIndex = i;
                return candidate;
            }
            if (fallback == null)
            {
                fallback = candidate;
                fallbackIndex = i;
            }
        }
        resolvedSourceIndex = fallbackIndex;
        return fallback;
    }

    // Convert main-chart bar selections into a fixed exchange-time interval.
    // Offsets are captured once, so new bars do not move an active selection.
    private bool CaptureZone()
    {
        if (zoneCaptured && capturedStartAgo == ZoneStartBarsAgo &&
            capturedEndAgo == ZoneEndBarsAgo) return true;
        var main = VAn.BarVars;
        if (main == null || main.Count == 0 || renko == null || !renko.HasBar) return false;
        int last = Math.Min(VAn.BarIndex, main.Count - 1);
        int start = last - Math.Max(ZoneStartBarsAgo, ZoneEndBarsAgo);
        int end = last - Math.Min(ZoneStartBarsAgo, ZoneEndBarsAgo);
        if (start < 0 || end < start || end > last || main[start] == null || main[end] == null) return false;
        DateTime finish;
        if (end < last && main[end + 1] != null)
            finish = main[end + 1].exchDt;
        else
        {
            var current = renko.CurrentBar;
            if (current == null) return false;
            finish = current.exchDt.AddTicks(1);
        }
        if (finish <= main[start].exchDt) return false;
        zoneStartTime = main[start].exchDt;
        zoneEndExclusive = finish;
        capturedStartAgo = ZoneStartBarsAgo;
        capturedEndAgo = ZoneEndBarsAgo;
        zoneCaptured = true;
        return true;
    }

    private static int FirstAtOrAfter(List<BarClass> bars, int last, DateTime time)
    {
        int lo = 0, hi = last + 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (bars[mid].exchDt < time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // The installed desktop SDK supports these context menu APIs.
    public override void OnContextMenu(ChartAreaRef? area, int? barIndex)
    {
        if (area != ChartArea && area != ChartAreaRef.Main) return;
        var main = VAn.BarVars;
        bool valid = main != null && barIndex.HasValue && barIndex.Value >= 0 &&
            barIndex.Value < main.Count && main[barIndex.Value] != null;
        VAn.AddMenuItem(this, "DeepLens: start zone", MarkZoneStart, valid, barIndex);
        VAn.AddMenuItem(this, "DeepLens: finish zone", MarkZoneEnd, valid && menuStartTime.HasValue, barIndex);
        VAn.AddMenuItem(this, "DeepLens: Live Follow", ResumeLive, true);
        VAn.AddMenuItem(this, "DeepLens: previous page", (a,b,p) =>
        {
            ZonePage = Math.Max(0, activeZonePage - 1);
            frozenView = null;
            NeedRecalculate = true;
        }, !panelHidden && UseSelectedZone && frozenView == null && activeZonePage > 0);
        VAn.AddMenuItem(this, "DeepLens: next page", (a,b,p) =>
        {
            ZonePage = Math.Min(zonePageCount - 1, activeZonePage + 1);
            frozenView = null;
            NeedRecalculate = true;
        }, !panelHidden && UseSelectedZone && frozenView == null && activeZonePage + 1 < zonePageCount);
        VAn.AddMenuItem(this, frozenView == null ? "DeepLens: freeze" : "DeepLens: resume",
            (a,b,p) =>
            {
                if (frozenView != null) frozenView = null;
                else if (lastView != null && lastView.Count > 0) frozenView = lastView;
                NeedRecalculate = true;
            }, !panelHidden && lastView != null && lastView.Count > 0);
        VAn.AddMenuItem(this, "DeepLens: clear zone", (a,b,p) =>
        {
            UseSelectedZone = false;
            zoneCaptured = false;
            menuStartTime = null;
            frozenView = null;
            NeedRecalculate = true;
        }, UseSelectedZone || menuStartTime.HasValue);
        VAn.AddMenuItem(this, panelHidden ? "DeepLens: show panel" : "DeepLens: hide panel",
            (a,b,p) =>
            {
                panelHidden = !panelHidden;
                NeedRecalculate = true;
            }, true);

        bool layoutEnabled = !panelHidden;
        VAn.AddMenuItem(this, "DeepLens: center panel horizontally here", MovePanelHere,
            layoutEnabled && valid, barIndex);
        VAn.AddMenuItem(this, "DeepLens: move panel up", (a,b,p) => AdjustLayout(0,-10,0,0,0),
            layoutEnabled && PanelY > 0);
        VAn.AddMenuItem(this, "DeepLens: move panel down", (a,b,p) => AdjustLayout(0,10,0,0,0),
            layoutEnabled && PanelY < 100 - Math.Clamp(PanelHeight,20,95));
        VAn.AddMenuItem(this, "DeepLens: wider panel", (a,b,p) => AdjustLayout(0,0,10,0,0),
            layoutEnabled && PanelWidth < 95);
        VAn.AddMenuItem(this, "DeepLens: narrower panel", (a,b,p) => AdjustLayout(0,0,-10,0,0),
            layoutEnabled && PanelWidth > 18);
        VAn.AddMenuItem(this, "DeepLens: taller panel", (a,b,p) => AdjustLayout(0,0,0,10,0),
            layoutEnabled && PanelHeight < 95);
        VAn.AddMenuItem(this, "DeepLens: shorter panel", (a,b,p) => AdjustLayout(0,0,0,-10,0),
            layoutEnabled && PanelHeight > 20);
        VAn.AddMenuItem(this, $"DeepLens: zoom in ({Zoom}% -> {Math.Min(180, Zoom + 10)}%)", (a,b,p) => AdjustLayout(0,0,0,0,10),
            layoutEnabled && Zoom < 180);
        VAn.AddMenuItem(this, $"DeepLens: zoom out ({Zoom}% -> {Math.Max(60, Zoom - 10)}%)", (a,b,p) => AdjustLayout(0,0,0,0,-10),
            layoutEnabled && Zoom > 60);
        VAn.AddMenuItem(this, "DeepLens: reset layout", (a,b,p) =>
        {
            PanelX = 8; PanelY = 12; PanelWidth = 28; PanelHeight = 70; Zoom = 100;
            NeedRecalculate = true;
        }, layoutEnabled);
    }

    // Keep every candle and price row visible while reserving zoom headroom at 100%.
    // At 180% reach the largest allowed size (subject to the panel's available space).
    private static double FitZoom(double preferredAt100, double fitLimit, int zoomPercent)
    {
        double baseline = Math.Min(preferredAt100, fitLimit * .75);
        double maximum = Math.Min(preferredAt100 * 1.8, fitLimit);
        int z = Math.Clamp(zoomPercent, 60, 180);
        if (z <= 100) return baseline * z / 100.0;
        return baseline + (maximum - baseline) * (z - 100) / 80.0;
    }

    private void AdjustLayout(int dx, int dy, int dw, int dh, int dz)
    {
        PanelWidth = Math.Clamp(PanelWidth + dw, 18, 95);
        PanelHeight = Math.Clamp(PanelHeight + dh, 20, 95);
        PanelX = Math.Clamp(PanelX + dx, 0, 100 - PanelWidth);
        PanelY = Math.Clamp(PanelY + dy, 0, 100 - PanelHeight);
        Zoom = Math.Clamp(Zoom + dz, 60, 180);
        NeedRecalculate = true;
    }

    private void MovePanelHere(ChartAreaRef? area, int? barIndex, object param)
    {
        if (!(param is int i) || AxisX == null) return;
        double min = AxisX.ViewMinimum, max = AxisX.ViewMaximum;
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min) return;
        int width = Math.Clamp(PanelWidth, 18, 95);
        double fraction = Math.Clamp((i + 1.0 - min) / (max - min), 0, 1);
        PanelX = Math.Clamp((int)Math.Round(fraction * 100 - width * .5), 0, 100 - width);
        AdjustLayout(0,0,0,0,0);
    }

    private void ResumeLive(ChartAreaRef? area, int? barIndex, object param)
    {
        UseSelectedZone = false;
        zoneCaptured = false;
        menuStartTime = null;
        frozenView = null;
        panelHidden = false;
        NeedRecalculate = true;
    }

    // Absolute chart coordinates: the marker stays with the selected bars when panning.
    private void UpdateZoneMarker()
    {
        if (zoneMarker == null) return;
        zoneMarker.Visible = false;
        var main = VAn.BarVars;
        if (main == null || main.Count == 0) return;
        int last = Math.Min(VAn.BarIndex, main.Count - 1);
        if (last < 0) return;
        int begin, finish;
        if (menuStartTime.HasValue)
        {
            begin = FirstAtOrAfter(main, last, menuStartTime.Value);
            finish = FirstAtOrAfter(main, last, menuStartTime.Value.AddTicks(1)) - 1;
        }
        else if (UseSelectedZone && zoneCaptured)
        {
            begin = FirstAtOrAfter(main, last, zoneStartTime);
            finish = FirstAtOrAfter(main, last, zoneEndExclusive) - 1;
        }
        else return;
        if (begin > last || finish < begin) return;
        double hi = double.NegativeInfinity, lo = double.PositiveInfinity;
        for (int i = begin; i <= finish; i++)
        {
            var bar = main[i];
            if (bar == null) continue;
            hi = Math.Max(hi, bar.High);
            lo = Math.Min(lo, bar.Low);
        }
        if (!double.IsFinite(hi) || !double.IsFinite(lo) || hi < lo) return;
        if (hi == lo)
        {
            double tick = VAn.FromPrice(hi).SubDouble;
            if (tick <= 0 || !double.IsFinite(tick)) return;
            hi += tick * .5;
            lo -= tick * .5;
        }
        zoneMarker.X = begin + .5;
        zoneMarker.X2 = finish + 1.5;
        zoneMarker.Y = hi;
        zoneMarker.Y2 = lo;
        zoneMarker.Visible = true;
    }

    private LensBar CopyBar(BarClass source, int index)
    {
        if (source == null) return null;
        var copy = new LensBar
        {
            Index = index, OpenInT = source.OpenInT, HighInT = source.HighInT,
            LowInT = source.LowInT, CloseInT = source.CloseInT
        };
        if (source.VbpListAll != null)
        {
            foreach (var entries in source.VbpListAll.SortedLHValues)
            {
                if (entries == null || renkoFilterIndex < 0 || renkoFilterIndex >= entries.Length ||
                    entries[renkoFilterIndex] == null) continue;
                var level = entries[renkoFilterIndex];
                copy.Levels.Add((level.PinT, level.BidVol, level.AskVol, level.TotVol));
            }
        }
        return copy;
    }

    private void MarkZoneStart(ChartAreaRef? area, int? barIndex, object param)
    {
        var main = VAn.BarVars;
        if (main == null || !(param is int i) || i < 0 || i >= main.Count || main[i] == null) return;
        menuStartTime = main[i].exchDt;
        NeedRecalculate = true;
    }

    private void MarkZoneEnd(ChartAreaRef? area, int? barIndex, object param)
    {
        var main = VAn.BarVars;
        if (!menuStartTime.HasValue || main == null || !(param is int i) ||
            i < 0 || i >= main.Count || main[i] == null || renko == null || !renko.HasBar) return;
        DateTime start = menuStartTime.Value;
        DateTime finish = main[i].exchDt;
        // Both clicks can be made in either chronological direction.
        DateTime earlier = start <= finish ? start : finish;
        DateTime later = start <= finish ? finish : start;
        int last = Math.Min(VAn.BarIndex, main.Count - 1);
        int next = FirstAtOrAfter(main, last, later.AddTicks(1));
        var current = renko.CurrentBar;
        if (current == null) return;
        DateTime exclusive = next <= last ? main[next].exchDt : current.exchDt.AddTicks(1);
        if (exclusive <= earlier) return;
        zoneStartTime = earlier;
        zoneEndExclusive = exclusive;
        capturedStartAgo = ZoneStartBarsAgo;
        capturedEndAgo = ZoneEndBarsAgo;
        zoneCaptured = true;
        UseSelectedZone = true;
        frozenView = null;
        panelHidden = false;
        ZonePage = 0;
        menuStartTime = null;
        NeedRecalculate = true;
    }

    private static string FindColor(string raw, string name)
    {
        int at = raw.IndexOf("\"" + name + "\"");
        if (at < 0) return "";
        int value = raw.IndexOf("\"Value\"", at);
        if (value < 0) return "";
        int hash = raw.IndexOf('#', value);
        if (hash < 0 || hash + 9 > raw.Length) return "";
        return raw.Substring(hash, 9);
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        UpdateZoneMarker();
        if (panelHidden)
        {
            ClearRows();
            label.Visible = settingsStatus.Visible = projectionStatus.Visible = false;
            foreach (var col in parameterColumns) col.Visible = false;
            return;
        }
        if (frozenView == null) lastView = null;
        label.Visible = ShowDiagnostics || ShowAllParameters;
        projectionStatus.Visible = ShowDiagnostics && !ShowAllParameters && NativeShapes;
        if (ShowAllParameters)
        {
            ClearRows();
            label.Text = "DeepLens v0.6.28 - parameter inspector";
            label.FontSize = 12;
            var parameters = TryGetParams(InspectIndex);
            var props = parameters == null ? null : parameters.XMLPropList;
            int count = props == null ? 0 : props.Count;
            settingsStatus.Visible = true;
            settingsStatus.Text = parameters == null
                ? $"Index {InspectIndex}: not found"
                : $"Index {InspectIndex}: {parameters.name} | XML: {count}";
            if (props != null && ExpandedParameter >= 0 && ExpandedParameter < count)
            {
                var prop = props[ExpandedParameter];
                string raw = prop.Value == null ? "null" : prop.Value.ToString();
                if (raw == null) raw = "null";
                raw = raw.Replace("\r", "");
                string[] parts = raw.Split('\n');
                var compactLines = new List<string>();
                foreach (string part in parts)
                {
                    string line = part.Trim();
                    if (line.Length == 0) continue;
                    while (line.Length > 42)
                    {
                        compactLines.Add(line.Substring(0, 42));
                        line = line.Substring(42);
                    }
                    if (line.Length > 0) compactLines.Add(line);
                }
                int page = Math.Max(0, ExpandedPage);
                int pageCount = Math.Max(1, (compactLines.Count + 71) / 72);
                page = Math.Min(page, pageCount - 1);
                settingsStatus.Text = $"{prop.Name} [{ExpandedParameter}] | page {page + 1}/{pageCount}";
                for (int col = 0; col < 3; col++)
                {
                    string lines = "";
                    int first = page * 72 + col * 24;
                    int last = Math.Min(compactLines.Count, first + 24);
                    for (int j = first; j < last; j++)
                        lines += compactLines[j] + "\n";
                    parameterColumns[col].Text = lines;
                    parameterColumns[col].Visible = first < last;
                }
                return;
            }
            int perColumn = (count + 2) / 3;
            for (int col = 0; col < 3; col++)
            {
                string lines = "";
                int first = col * perColumn;
                int last = Math.Min(count, first + perColumn);
                for (int s = first; s < last; s++)
                {
                    var prop = props[s];
                    string value = prop.Value == null ? "null" : prop.Value.ToString();
                    if (value == null) value = "null";
                    value = value.Replace("\r", "").Replace("\n", " ");
                    if (value.Length > 28)
                        value = value.Substring(0, 25) + "...";
                    lines += $"{s}: {prop.Name} = {value}\n";
                }
                parameterColumns[col].Text = lines;
                parameterColumns[col].Visible = first < last;
            }
            return;
        }
        settingsStatus.Visible = false;
        for (int col = 0; col < 3; col++)
            parameterColumns[col].Visible = false;
        var style = ResolveDeepPrint();
        bool inherited = false;
        string rawPoc = "missing", rawBid = "missing", rawAsk = "missing";
        int revision = 17;
        int parameterCount = 0;
        bool showZero = true, enablePoc = true;
        float sourceFont = 10;
        int opacity = 70;
        int markerWidth = 2, outsideStyle = -1, markerAlignment = -1;
        int footType = -1, showStyle = -1, inputData = -1, autoGrouping = -1, manualTicks = 0, groupMode = -1, filterMin = -1, filterMax = -1, proportion = -1;
        float borderWidth = 1, pocWidth = 1;
        ColorRef bidColor = ColorRef.FromRgb(150, 40, 55);
        ColorRef askColor = ColorRef.FromRgb(35, 150, 65);
        ColorRef bidText = ColorRef.FromRgb(235, 240, 245);
        ColorRef askText = bidText;
        ColorRef volumeColor = ColorRef.FromRgb(85, 115, 205);
        ColorRef volumeText = bidText;
        ColorRef pocColor = ColorRef.FromRgb(240, 185, 65);
        if (style != null && style.name == "Deep Print" && style.XMLPropList != null)
        {
            foreach (var prop in style.XMLPropList)
            {
                string raw = prop.Value == null ? "" : prop.Value.ToString();
                if (raw == null) continue;
                parameterCount++;
                if (prop.Name != null)
                    foreach (char ch in prop.Name) revision = unchecked(revision * 31 + ch);
                foreach (char ch in raw) revision = unchecked(revision * 31 + ch);
                if (prop.Name == "EnablePoc")
                {
                    rawPoc = raw;
                    if (bool.TryParse(raw, out bool ep)) enablePoc = ep;
                }
                if (prop.Name == "ShowZeroOnText") bool.TryParse(raw, out showZero);
                if (prop.Name == "FontSize" && float.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) sourceFont = f;
                if (int.TryParse(raw, out int iv))
                {
                    if (prop.Name == "FootType") footType = iv;
                    if (prop.Name == "BarMarkerWidth") markerWidth = iv;
                    if (prop.Name == "OutsideBarStyle") outsideStyle = iv;
                    if (prop.Name == "MarkerAlignment") markerAlignment = iv;
                    if (prop.Name == "ShowStyle") showStyle = iv;
                    if (prop.Name == "InputData") inputData = iv;
                    if (prop.Name == "AutoGrouping") autoGrouping = iv;
                    if (prop.Name == "ManualTicks") manualTicks = iv;
                    if (prop.Name == "GroupMode") groupMode = iv;
                    if (prop.Name == "FilterMin") filterMin = iv;
                    if (prop.Name == "FilterMax") filterMax = iv;
                    if (prop.Name == "FootProportion") proportion = iv;
                }
                if (prop.Name == "BackOpacity" && int.TryParse(raw, out int o)) opacity = o;
                if (prop.Name == "BorderWidth" && float.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w)) borderWidth = w;
                if (prop.Name == "PocBorderWidth" && float.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pw)) pocWidth = pw;
                if (prop.Name == "PocBorderColor" && ColorRef.TryParse(raw, out var pc)) pocColor = pc;
                if (prop.Name == "Background")
                {
                    if (ColorRef.TryParse(FindColor(raw, "FixedVolColor"), out var vc)) volumeColor = vc;
                    string bv = FindColor(raw, "FixedBidColor");
                    string av = FindColor(raw, "FixedAskColor");
                    rawBid = bv;
                    rawAsk = av;
                    if (ColorRef.TryParse(bv, out var bc) && ColorRef.TryParse(av, out var ac))
                    {
                        bidColor = bc;
                        askColor = ac;
                        inherited = true;
                    }
                }
                if (prop.Name == "Text")
                {
                    if (ColorRef.TryParse(FindColor(raw, "FixedVolColor"), out var vt)) volumeText = vt;
                    if (ColorRef.TryParse(FindColor(raw, "FixedBidColor"), out var bt)) bidText = bt;
                    if (ColorRef.TryParse(FindColor(raw, "FixedAskColor"), out var at)) askText = at;
                }
            }
        }
        settingsStatus.Visible = ShowDiagnostics;
        settingsStatus.Text = $"Index {resolvedSourceIndex} (requested {InspectIndex}): {(style == null ? "not found" : style.name)} | Params {parameterCount}\n"
            + $"FootType={footType} ShowStyle={showStyle} InputData={inputData}\n"
            + $"AutoGrouping={autoGrouping} ManualTicks={manualTicks} GroupMode={groupMode}\n"
            + $"FilterMin={filterMin} FilterMax={filterMax} Proportion={proportion}\n"
            + $"Candle style={outsideStyle} Alignment={markerAlignment} Width={markerWidth}\n"
            + "PROFILE source | Volume / Bid-Ask / Delta / Delta+Volume";
        // Profile renderer: FootType follows the Deep Print dropdown order (0..3).
        // Do not render filtered or non-volume input data as if it were verified.
        if (style == null || style.name != "Deep Print" || manualTicks < 1 || manualTicks > 10000 || footType < 0 || footType > 3 || showStyle != 1 || proportion != 0 || inputData != 0 || autoGrouping != 1 || groupMode != 0 || filterMin != 0 || filterMax != 0)
        {
            ClearRows();
            label.Visible = true;
            label.Text = style == null
                ? "DeepLens - add Deep Print to this chart"
                : "DeepLens - unsupported Deep Print settings (see actual / expected)";
            settingsStatus.Visible = true;
            settingsStatus.Text += "\nExpected: FootType=0..3 ShowStyle=1 InputData=0"
                + "\nAutoGrouping=1 ManualTicks=1..10000 GroupMode=0"
                + "\nFilterMin=0 FilterMax=0 Proportion=0 (Bar relative)";
            return;
        }
        if (renko == null || !renko.HasBar || renkoFilterIndex < 0)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens - waiting for Renko 30|1 / VBP resolution";
            return;
        }
        var bars = renko.BarVars;
        if (bars == null || bars.Count == 0)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens - waiting for Renko 30|1 bars";
            return;
        }
        int end = renko.BarIndex;
        if (end < 0 || end >= bars.Count)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens - waiting for a valid Renko bar index";
            return;
        }

        int firstRenkoIndex;
        int columns;
        int zoneTotal = 0, zonePageIndex = 0;
        zonePageCount = 0;
        if (UseSelectedZone)
        {
            if (!CaptureZone())
            {
                ClearRows();
                label.Visible = true;
                label.Text = "DeepLens - selected main-chart range is not available";
                return;
            }
            UpdateZoneMarker();
            int from = FirstAtOrAfter(bars, end, zoneStartTime);
            int until = FirstAtOrAfter(bars, end, zoneEndExclusive);
            zoneTotal = until - from;
            if (zoneTotal <= 0)
            {
                ClearRows();
                label.Visible = true;
                label.Text = "DeepLens - no Renko bars in selected time range";
                return;
            }
            int pageSize = Math.Clamp(ZoneCandlesPerPage, 1, MaxColumns);
            zonePageCount = (zoneTotal - 1) / pageSize + 1;
            zonePageIndex = Math.Clamp(ZonePage, 0, zonePageCount - 1);
            activeZonePage = zonePageIndex;
            firstRenkoIndex = from + zonePageIndex * pageSize;
            columns = Math.Min(pageSize, until - firstRenkoIndex);
        }
        else
        {
            zoneCaptured = false;
            columns = Math.Min(end + 1, Math.Clamp(LiveCandles, 1, MaxColumns));
            firstRenkoIndex = end - columns + 1;
        }

        List<LensBar> view = frozenView;
        if (view == null)
        {
            view = new List<LensBar>(columns);
            for (int i = 0; i < columns; i++)
                view.Add(CopyBar(bars[firstRenkoIndex + i], firstRenkoIndex + i));
        }
        columns = view.Count;

        // Compact baseline: keep the user zoom control and scale all profile geometry together.
        const double compactScale = .75;
        double zoom = compactScale * Math.Max(60, Math.Min(180, Zoom)) / 100.0;
        double panelWidth = Math.Clamp(PanelWidth, 18, 95) / 100.0;
        double panelHeight = Math.Clamp(PanelHeight, 20, 95) / 100.0;
        double panelLeft = Math.Clamp(PanelX / 100.0, 0, 1 - panelWidth);
        double panelTop = Math.Clamp(PanelY / 100.0, 0, 1 - panelHeight);
        const double paddingY = .012;
        double preferredStep = .027 * zoom;
        int preferredFont = Math.Max(6, Math.Min(24, (int)Math.Round(sourceFont * zoom)));
        // Fit the active page and its label allowance inside the chosen width.
        bool dualProfile = footType == 1 || footType == 3;
        double baseStride = dualProfile ? .23 : .13;
        double baseTail = dualProfile ? .185 : .085;
        double horizontalZoom = FitZoom(compactScale,
            panelWidth / (.028 + (columns - 1) * baseStride + baseTail), Zoom);
        double stride = baseStride * horizontalZoom;
        double profileWidth = (dualProfile ? .073 : .05) * horizontalZoom;
        double secondOffset = .093 * horizontalZoom;
        double left = panelLeft + .028 * horizontalZoom;
        int high = int.MinValue, low = int.MaxValue;
        bool anyVbp = false;
        int[] poc = new int[columns];
        long[] maxVol = new long[columns];
        bool[] has = new bool[columns];

        for (int c = 0; c < columns; c++)
        {
            var bar = view[c];
            if (bar == null) continue;
            // Include both OHLC extremes and all reported footprint prices.
            if (bar.HighInT >= bar.LowInT)
            {
                high = Math.Max(high, bar.HighInT);
                low = Math.Min(low, bar.LowInT);
            }
            foreach (var level in bar.Levels)
            {
                high = Math.Max(high, level.PinT);
                low = Math.Min(low, level.PinT);
                anyVbp = true;
                if (!has[c] || level.TotVol > maxVol[c])
                {
                    maxVol[c] = level.TotVol;
                    poc[c] = level.PinT;
                }
                has[c] = true;
            }
        }

        if (!anyVbp || high == int.MinValue || low > high)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens - no VBP data for the selected Renko 30|1 bars";
            return;
        }

        lastView = view;

        // Auto fit all selected bars. Keep the original fixed price grouping and
        // shrink the display scale, never the price range or the underlying data.
        int group = manualTicks;
        int gridLow = (int)Math.Floor((double)low / group) * group;
        int top = (int)Math.Floor((double)high / group) * group + group - 1;
        int visibleRows = (int)(((long)top - gridLow + 1) / group);
        // Keep the established top-tick alignment; reserve room for the bottom tick
        // which can lie up to one group below the final row's center.
        double availableHeight = panelHeight - 2 * paddingY;
        double step = FitZoom(.027 * compactScale, availableHeight / (visibleRows + .5), Zoom);
        double rowOrigin = panelTop + paddingY + step * .5;
        double TickToPanelY(double tick) => rowOrigin + (top - tick) * step / group;
        int font = Math.Max(6, (int)Math.Round(preferredFont * step / preferredStep));
        int labelStride = Math.Max(1, (int)Math.Ceiling((font + 1.0) / (preferredFont + 1.0) * preferredStep / step));
        nativeLabelStep = step;
        nativeLabelWidth = dualProfile
            ? Math.Min(secondOffset, panelLeft + panelWidth - (left + (columns - 1) * stride + secondOffset))
            : Math.Min(stride, panelLeft + panelWidth - (left + (columns - 1) * stride));
        nativeLabelPreferredFont = Math.Clamp(preferredFont + 1, 6, 25);
        nativeLabelFallbackFont = Math.Min(25, font + 1);
        nativeLabelFallbackStride = labelStride;
        bool profileLimited = false;
        ClearRows();
        string footName = footType == 0 ? "VOLUME" : footType == 1 ? "BID | ASK" : footType == 2 ? "DELTA" : "DELTA | VOLUME";
        label.Text = $"DeepLens v0.6.28 | {footName} | RENKO 30|1 | {(frozenView != null ? "FROZEN" : UseSelectedZone ? $"ZONE page {zonePageIndex + 1}/{zonePageCount}, {zoneTotal} bars" : "LIVE")} | {group} ticks/row | Zoom {Zoom}%";
        label.FontSize = font + 2;
        settingsStatus.Visible = ShowDiagnostics;
        modeLabel.X = panelLeft;
        modeLabel.Y = Math.Max(0, panelTop - .028);
        modeLabel.Text = frozenView != null ? "DeepLens | FROZEN" : $"DeepLens | {zonePageIndex + 1}/{zonePageCount}";
        modeLabel.Visible = frozenView != null || UseSelectedZone;

        if (NativeShapes && ShowPanelBackground)
        {
            projected.Add((panelBackground, panelLeft, panelLeft + panelWidth,
                panelTop, panelTop + panelHeight, 0, 0, false));
        }

        for (int c = 0; c < columns; c++)
        {
            EnsureColumn(c);
            double x = left + c * stride;
            var bar = view[c];
            if (bar == null) continue;
            int index = bar.Index;
            // Text-based candle preview: each row corresponds to an inherited price bucket.
            // Body and wick are quantized to that grid, not exact sub-row geometry.
            int candleHigh = bar.HighInT, candleLow = bar.LowInT;
            int candleOpen = bar.OpenInT, candleClose = bar.CloseInT;
            settingsStatus.Text += $"\nBar {index}: OHLC ticks {candleOpen}/{candleHigh}/{candleLow}/{candleClose}";
            if (NativeShapes && outsideStyle == 1 && markerAlignment == 0 && candleHigh >= candleLow)
            {
                // Official native candle layout, projected onto the independent relative Y grid.
                double yOpen = TickToPanelY(candleOpen);
                double yClose = TickToPanelY(candleClose);
                double yHigh = TickToPanelY(candleHigh);
                double yLow = TickToPanelY(candleLow);
                double yMin = panelTop + paddingY, yMax = panelTop + panelHeight - paddingY;
                if (candleOpen >= candleLow && candleOpen <= candleHigh &&
                    candleClose >= candleLow && candleClose <= candleHigh)
                {
                    var candle = nativeCandles[c];
                    double halfWidth = Math.Max(.0015, Math.Min(.005, markerWidth * .0015)) * horizontalZoom * .75;
                    projected.Add((candle, x - .007 * horizontalZoom - halfWidth, x - .007 * horizontalZoom + halfWidth,
                        Math.Clamp(yOpen, yMin, yMax), Math.Clamp(yLow, yMin, yMax),
                        Math.Clamp(yHigh, yMin, yMax), Math.Clamp(yClose, yMin, yMax), true));
                    candle.BackColor = ColorRef.FromRgb(120, 125, 145);
                    candle.LineColor = ColorRef.FromRgb(180, 185, 200);
                    candle.LineWidth = 1;
                    candle.Visible = false;
                }
            }
            else if (!NativeShapes && outsideStyle == 1 && markerAlignment == 0 && candleHigh >= candleLow && candleOpen >= candleLow && candleOpen <= candleHigh && candleClose >= candleLow && candleClose <= candleHigh)
            {
                int firstRow = -1;
                string marker = "";
                for (int k = Math.Max(0, (int)(((long)top - candleHigh) / group)); k <= Math.Min(visibleRows - 1, (int)(((long)top - candleLow) / group)) && marker.Length < 1024; k++)
                {
                    int upper = top - k * group;
                    int lower = upper - group + 1;
                    if (upper < candleLow || lower > candleHigh) continue;
                    bool body = upper >= Math.Min(candleOpen, candleClose) && lower <= Math.Max(candleOpen, candleClose);
                    if (firstRow < 0) firstRow = k;
                    else marker += "\n";
                    marker += body ? "█" : "│";
                }
                if (firstRow >= 0)
                {
                    var mark = candleMarkers[c];
                    mark.X = x - .012 * horizontalZoom;
                    mark.Y = rowOrigin + firstRow * step;
                    mark.FontSize = font;
                    mark.Text = marker;
                    mark.ForeColor = ColorRef.FromRgb(165, 170, 190);
                    mark.Visible = true;
                }
            }
            if (!has[c]) continue;
            // Sparse occupied rows: a large gap between bars does not allocate
            // thousands of empty annotations or truncate the visible price range.
            var levels = new SortedDictionary<int, (long Bid, long Ask, long Total)>();
            foreach (var level in bar.Levels)
            {
                int k = (int)(((long)top - level.PinT) / group);
                if (k < 0 || k >= visibleRows) continue;
                levels.TryGetValue(k, out var old);
                levels[k] = (old.Bid + level.BidVol, old.Ask + level.AskVol, old.Total + level.TotVol);
            }
            long deltaScale = 1, scale = 1, sideScale = 1;
            int pocRow = -1;
            long groupedPocVolume = -1;
            foreach (var pair in levels)
            {
                var value = pair.Value;
                deltaScale = Math.Max(deltaScale, Math.Abs(value.Ask - value.Bid));
                scale = Math.Max(scale, value.Total);
                sideScale = Math.Max(sideScale, Math.Max(value.Bid, value.Ask));
                if (value.Total > groupedPocVolume)
                {
                    groupedPocVolume = value.Total;
                    pocRow = pair.Key;
                }
            }
            int sampleStride = Math.Max(1, (int)Math.Ceiling(levels.Count / (double)MaxRows));
            profileLimited |= sampleStride > 1;
            int occupied = 0, slot = 0;
            int lastLabelRow = int.MinValue;
            foreach (var entry in levels)
            {
                if (occupied++ % sampleStride != 0 || slot >= MaxRows) continue;
                int k = entry.Key;
                int rowSlot = slot++;
                EnsureRow(c, rowSlot);
                var b = bidLabels[c,rowSlot];
                var a = askLabels[c,rowSlot];
                long d = entry.Value.Ask - entry.Value.Bid;
                bool isPoc = enablePoc && k == pocRow;
                byte alpha = (byte)(255 * Math.Clamp(opacity, 0, 100) / 100);
                ColorRef transparent = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                ColorRef deltaColor = d < 0 ? bidColor : askColor;
                ColorRef deltaText = d < 0 ? bidText : askText;
                long firstValue = footType == 0 ? entry.Value.Total : footType == 1 ? entry.Value.Bid : d;
                long firstScale = footType == 0 ? scale : footType == 1 ? sideScale : deltaScale;
                ColorRef firstColor = (footType == 0 ? volumeColor : footType == 1 ? bidColor : deltaColor).WithOpacity(alpha);
                ColorRef firstText = footType == 0 ? volumeText : footType == 1 ? bidText : deltaText;
                long secondValue = footType == 1 ? entry.Value.Ask : entry.Value.Total;
                long secondScale = footType == 1 ? sideScale : scale;
                ColorRef secondColor = (footType == 1 ? askColor : volumeColor).WithOpacity(alpha);
                ColorRef secondText = footType == 1 ? askText : volumeText;
                double rowY = rowOrigin + k * step;
                double topY = rowY - step * .5, bottomY = rowY + step * .5;

                if (NativeShapes)
                {
                    if (firstValue != 0 || isPoc)
                    {
                        var rect = profileRects[c,rowSlot];
                        double width = profileWidth * Math.Abs(firstValue) / firstScale;
                        if (isPoc) width = Math.Max(width, profileWidth * .03);
                        projected.Add((rect, x, x + width, topY, bottomY, 0, 0, false));
                        rect.BackColor = firstColor;
                        rect.LineColor = isPoc ? pocColor : firstColor;
                        rect.LineWidth = isPoc ? pocWidth : 0;
                        rect.Visible = false;
                    }
                    if (dualProfile && (secondValue != 0 || isPoc))
                    {
                        var rect = secondaryRects[c,rowSlot];
                        if (rect == null)
                        {
                            rect = MakeShape(AnnotationType.Rectangle);
                            secondaryRects[c,rowSlot] = rect;
                        }
                        double width = profileWidth * Math.Abs(secondValue) / secondScale;
                        if (isPoc) width = Math.Max(width, profileWidth * .03);
                        projected.Add((rect, x + secondOffset, x + secondOffset + width,
                            topY, bottomY, 0, 0, false));
                        rect.BackColor = secondColor;
                        rect.LineColor = isPoc ? pocColor : secondColor;
                        rect.LineWidth = isPoc ? pocWidth : 0;
                        rect.Visible = false;
                    }
                    // For paired views each sub-column gets an independent label stride.
                    // ProjectShapes visits labels in row order; the label key tracks each side.
                    ConfigureValueLabel(a, firstValue, firstText, transparent, font);
                    if (showZero || firstValue != 0)
                        projectedLabels.Add((a, x, rowY, c * 2, k));
                    if (dualProfile)
                    {
                        ConfigureValueLabel(b, secondValue, secondText, transparent, font);
                        if (showZero || secondValue != 0)
                            projectedLabels.Add((b, x + secondOffset, rowY, c * 2 + 1, k));
                    }
                }
                else
                {
                    // Legacy text preview is retained for the native-shapes diagnostic toggle.
                    b.CoordinateXType = b.CoordinateYType = CoordinateTypeEnum.Relative;
                    b.X = x;
                    b.Y = rowY;
                    b.FontSize = font;
                    b.FontBold = false;
                    int blocks = firstValue == 0 ? 0 : Math.Max(1, (int)Math.Round(5.0 * Math.Abs(firstValue) / firstScale));
                    b.Text = blocks == 0 ? "" : new string('█', blocks);
                    b.ForeColor = firstColor;
                    b.BackColor = b.LineColor = transparent;
                    b.LineWidth = 0;
                    b.Visible = blocks > 0;
                    ConfigureValueLabel(a, firstValue, firstText, transparent, font);
                    a.CoordinateXType = a.CoordinateYType = CoordinateTypeEnum.Relative;
                    a.X = x;
                    a.Y = rowY;
                    if (dualProfile) a.Text = $"{firstValue} | {secondValue}";
                    bool eligible = showZero || firstValue != 0 || (dualProfile && secondValue != 0);
                    a.Visible = eligible && (lastLabelRow == int.MinValue || (long)k - lastLabelRow >= labelStride);
                    if (a.Visible) lastLabelRow = k;
                    a.LineColor = isPoc ? pocColor : transparent;
                    a.LineWidth = isPoc ? pocWidth : 0;
                }
            }
        }
        if (profileLimited)
        {
            label.Visible = true;
            label.Text = "DeepLens - very dense profile: some levels omitted (512 per candle)";
        }
        if (NativeShapes) ProjectShapes();
    }
}
