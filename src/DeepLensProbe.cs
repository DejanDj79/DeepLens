using System.ComponentModel;
using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
using VolumetricaControls;
using VolumetricaCore;
using static VolSysAPI.ExternalStructure;

namespace DeepLens;

public class DeepLensProbe : Indicator
{
    public static IndicatorDescriptionBase Register()
    {
        return new IndicatorDescriptionBase
        {
            Name = "DeepLens Probe",
            Description = "Three-bar live footprint test.",
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
    private readonly List<(IAnnotation Label, double X, double Y)> projectedLabels = new();
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

    private IAnnotation panelBackground;
    private IAnnotation settingsStatus;
    private IAnnotation projectionStatus;
    private int resizeCalls;
    private int resolvedSourceIndex = -1;
    private int lastRequestedIndex = -1;
    private readonly IAnnotation[,] profileRects = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[] nativeCandles = new IAnnotation[Columns];
    private readonly IAnnotation[] parameterColumns = new IAnnotation[3];
    private const int Columns = 3;
    private const int MaxRows = 512;
    private const int InitialRows = 40;
    [Category("Deep Print diagnostic")]
    [DisplayName("Show diagnostics")]
    public bool ShowDiagnostics { get; set; } = false;

    private IAnnotation label;
    private readonly IAnnotation[,] bidLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[,] askLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[] candleMarkers = new IAnnotation[Columns];

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
        foreach (var p in projectedLabels)
        {
            p.Label.CoordinateXType = CoordinateTypeEnum.Absolute;
            p.Label.CoordinateYType = CoordinateTypeEnum.Absolute;
            p.Label.UseAxisWithSpace = false;
            p.Label.X = X(p.X);
            p.Label.Y = Y(p.Y);
        }
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
        Description = "DeepLens v0.6.20";
    }

    public override void OnLoad()
    {
        projected.Clear();
        projectedLabels.Clear();
        resolvedSourceIndex = -1;
        lastRequestedIndex = -1;
        // Register first in the same foreground annotation group as the native shapes.
        // This puts the panel behind the candles and profiles in insertion order.
        panelBackground = MakeShape(AnnotationType.Rectangle);
        panelBackground.BackColor = ColorRef.FromRgb(18, 21, 27);
        panelBackground.LineColor = ColorRef.FromRgb(82, 88, 99);
        panelBackground.LineWidth = 1;
        projectionStatus = MakeText(.48, .32, 10);
        projectionStatus.BackColor = ColorRef.FromRgb(25, 30, 38);
        resizeCalls = 0;
        label = MakeText(.09, .05, 12);
        label.Text = "DeepLens v0.6.20 - waiting";
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
        for (int c = 0; c < Columns; c++)
        {
            nativeCandles[c] = MakeShape(AnnotationType.Candlestick);
            candleMarkers[c] = MakeText(.10, .14, 10);
            candleMarkers[c].Visible = false;
            for (int k = 0; k < InitialRows; k++)
                EnsureRow(c, k);
        }
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

    private void ClearRows()
    {
        panelBackground.Visible = false;
        for (int k = 0; k < MaxRows; k++)
        {
            for (int c = 0; c < Columns; c++)
            {
                if (profileRects[c,k] == null) continue;
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
                profileRects[c,k].Visible = false;
            }
        }
        for (int c = 0; c < Columns; c++)
        {
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
        return type == 2 && mode == 1 && input == 0 && grouping == 1 &&
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
        label.Visible = ShowDiagnostics || ShowAllParameters;
        projectionStatus.Visible = ShowDiagnostics && !ShowAllParameters && NativeShapes;
        if (ShowAllParameters)
        {
            ClearRows();
            label.Text = "DeepLens v0.6.20 - parameter inspector";
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
            + "DELTA / PROFILE source | native shapes experiment";
        // Comparison only: user-selected source is Delta / Volume / Manual / Fixed.
        // Do not render filtered or non-volume data as if it were verified.
        if (style == null || style.name != "Deep Print" || manualTicks < 1 || manualTicks > 10000 || footType != 2 || showStyle != 1 || proportion != 0 || inputData != 0 || autoGrouping != 1 || groupMode != 0 || filterMin != 0 || filterMax != 0)
        {
            ClearRows();
            label.Visible = true;
            label.Text = style == null
                ? "DeepLens - add Deep Print to this chart"
                : "DeepLens - unsupported Deep Print settings (see actual / expected)";
            settingsStatus.Visible = true;
            settingsStatus.Text += "\nExpected: FootType=2 ShowStyle=1 InputData=0"
                + "\nAutoGrouping=1 ManualTicks=1..10000 GroupMode=0"
                + "\nFilterMin=0 FilterMax=0 Proportion=0 (Bar relative)";
            return;
        }
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens v0.6.20 - waiting for bars";
            return;
        }
        int end = VAn.BarIndex;
        if (end < 0 || end >= bars.Count)
        {
            ClearRows();
            label.Visible = true;
            label.Text = "DeepLens - waiting for a valid bar index";
            return;
        }

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
        // Fit three columns and their label allowance inside the chosen width.
        double horizontalZoom = Math.Min(zoom, panelWidth / (.028 + (Columns - 1) * .13 + .05 + .035));
        double stride = .13 * horizontalZoom;
        double profileWidth = .05 * horizontalZoom;
        double askOffset = .036 * horizontalZoom;
        double left = panelLeft + .028 * horizontalZoom;
        int high = int.MinValue, low = int.MaxValue;
        bool anyVbp = false;
        int[] poc = new int[Columns];
        int[] maxVol = new int[Columns];
        bool[] has = new bool[Columns];

        for (int c = 0; c < Columns; c++)
        {
            int index = end - Columns + 1 + c;
            if (index < 0 || index >= bars.Count) continue;
            var bar = bars[index];
            if (bar == null) continue;
            // Include both OHLC extremes and all reported footprint prices.
            if (bar.HighInT >= bar.LowInT)
            {
                high = Math.Max(high, bar.HighInT);
                low = Math.Min(low, bar.LowInT);
            }
            if (bar.VbpListAll == null) continue;
            foreach (var entries in bar.VbpListAll.SortedLHValues)
            {
                if (entries == null || entries.Length == 0 || entries[0] == null) continue;
                var level = entries[0];
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
            label.Text = "DeepLens - no VBP data for the last three bars";
            return;
        }

        // Auto fit all selected bars. Keep the original fixed price grouping and
        // shrink the display scale, never the price range or the underlying data.
        int group = manualTicks;
        int gridLow = (int)Math.Floor((double)low / group) * group;
        int top = (int)Math.Floor((double)high / group) * group + group - 1;
        int visibleRows = (int)(((long)top - gridLow + 1) / group);
        // Keep the established top-tick alignment; reserve room for the bottom tick
        // which can lie up to one group below the final row's center.
        double availableHeight = panelHeight - 2 * paddingY;
        double step = Math.Min(preferredStep, availableHeight / (visibleRows + .5));
        double rowOrigin = panelTop + paddingY + step * .5;
        double TickToPanelY(double tick) => rowOrigin + (top - tick) * step / group;
        int font = Math.Max(6, (int)Math.Round(preferredFont * step / preferredStep));
        int labelStride = Math.Max(1, (int)Math.Ceiling((font + 1.0) / (preferredFont + 1.0) * preferredStep / step));
        bool profileLimited = false;
        ClearRows();
        label.Text = $"DeepLens v0.6.20 | DELTA PROFILE TEST | {group} ticks/row | Zoom {Zoom}%";
        label.FontSize = font + 2;
        settingsStatus.Visible = ShowDiagnostics;

        if (NativeShapes && ShowPanelBackground)
        {
            projected.Add((panelBackground, panelLeft, panelLeft + panelWidth,
                panelTop, panelTop + panelHeight, 0, 0, false));
        }

        for (int c = 0; c < Columns; c++)
        {
            int index = end - Columns + 1 + c;
            double x = left + c * stride;
            if (index < 0 || index >= bars.Count) continue;
            var bar = bars[index];
            if (bar == null) continue;
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
            if (bar.VbpListAll == null || !has[c]) continue;
            // Sparse occupied rows: a large gap between bars does not allocate
            // thousands of empty annotations or truncate the visible price range.
            var levels = new SortedDictionary<int, (long Bid, long Ask, long Total)>();
            foreach (var entries in bar.VbpListAll.SortedLHValues)
            {
                if (entries == null || entries.Length == 0 || entries[0] == null) continue;
                var level = entries[0];
                int k = (int)(((long)top - level.PinT) / group);
                if (k < 0 || k >= visibleRows) continue;
                levels.TryGetValue(k, out var old);
                levels[k] = (old.Bid + level.BidVol, old.Ask + level.AskVol, old.Total + level.TotVol);
            }
            long deltaScale = 1, scale = 1;
            foreach (var value in levels.Values)
            {
                deltaScale = Math.Max(deltaScale, Math.Abs(value.Ask - value.Bid));
                scale = Math.Max(scale, value.Total);
            }
            int sampleStride = Math.Max(1, (int)Math.Ceiling(levels.Count / (double)MaxRows));
            profileLimited |= sampleStride > 1;
            int occupied = 0, slot = 0;
            foreach (var entry in levels)
            {
                if (occupied++ % sampleStride != 0 || slot >= MaxRows) continue;
                int k = entry.Key;
                int rowSlot = slot++;
                EnsureRow(c, rowSlot);
                var b = bidLabels[c,rowSlot];
                var a = askLabels[c,rowSlot];
                b.X = x;
                a.X = x + askOffset;
                b.Y = a.Y = rowOrigin + k * step;
                b.FontSize = font;
                a.FontSize = Math.Min(25, font + 1);
                int red = (int)(120.0 * entry.Value.Bid / scale);
                int green = (int)(120.0 * entry.Value.Ask / scale);
                if (inherited)
                {
                    byte alpha = (byte)(255 * Math.Max(0, Math.Min(100, opacity)) / 100);
                    b.BackColor = bidColor.WithOpacity(alpha);
                    a.BackColor = askColor.WithOpacity(alpha);
                }
                else
                {
                    b.BackColor = ColorRef.FromRgb((byte)Math.Min(255, 55 + red), 30, 40);
                    a.BackColor = ColorRef.FromRgb(24, (byte)Math.Min(255, 55 + green), 45);
                }
                b.ForeColor = bidText;
                a.ForeColor = askText;
                bool isPoc = enablePoc && poc[c] <= top && (top - poc[c]) / group == k;
                b.LineColor = a.LineColor = isPoc ? pocColor : ColorRef.FromRgb(65,75,85);
                b.LineWidth = a.LineWidth = isPoc ? pocWidth : borderWidth;
                long d = entry.Value.Ask - entry.Value.Bid;
                int blocks = d == 0 ? 0 : Math.Max(1, (int)Math.Round(5.0 * Math.Abs(d) / deltaScale));
                ColorRef profileColor = (d < 0 ? bidColor : askColor).WithOpacity((byte)(255 * Math.Max(0, Math.Min(100, opacity)) / 100));
                b.Text = blocks == 0 ? "" : new string('█', blocks);
                b.ForeColor = profileColor;
                b.BackColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                b.LineColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                b.LineWidth = 0;
                b.Visible = !NativeShapes && blocks > 0;
                if (NativeShapes && d != 0)
                {
                    var rect = profileRects[c,rowSlot];
                    projected.Add((rect, x, x + profileWidth * Math.Abs(d) / deltaScale,
                        rowOrigin + (k - .5) * step, rowOrigin + (k + .5) * step, 0, 0, false));
                    rect.BackColor = profileColor;
                    rect.LineColor = isPoc ? pocColor : profileColor;
                    rect.LineWidth = isPoc ? pocWidth : 0;
                    rect.Visible = false;
                }
                a.CoordinateXType = a.CoordinateYType = CoordinateTypeEnum.Relative;
                a.X = x;
                if (NativeShapes)
                    projectedLabels.Add((a, x, rowOrigin + (k - .35) * step));
                a.Text = !showZero && d == 0 ? "" : $"{d}";
                a.BackColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                a.ForeColor = d < 0 ? bidText : askText;
                a.LineColor = isPoc && !NativeShapes ? pocColor : ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                a.LineWidth = isPoc && !NativeShapes ? pocWidth : 0;
                a.Visible = (showZero || d != 0) && (labelStride == 1 || k % labelStride == 0);
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
