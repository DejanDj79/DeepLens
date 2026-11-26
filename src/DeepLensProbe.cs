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
    [DisplayName("Detail offset (ticks)")]
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
    private IAnnotation settingsStatus;
    private IAnnotation projectionStatus;
    private int resizeCalls;
    private readonly IAnnotation[,] profileRects = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[] nativeCandles = new IAnnotation[Columns];
    private readonly IAnnotation[] parameterColumns = new IAnnotation[3];
    private const int Columns = 3;
    private const int MaxRows = 40;
    private IAnnotation label;
    private readonly IAnnotation[,] bidLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[,] askLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[] headers = new IAnnotation[Columns];
    private readonly IAnnotation[] footers = new IAnnotation[Columns];
    private IAnnotation footer;
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
        if (projectionStatus == null) return;
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
        Description = "DeepLens v0.6.13";
    }

    public override void OnLoad()
    {
        projectionStatus = MakeText(.48, .32, 10);
        projectionStatus.BackColor = ColorRef.FromRgb(25, 30, 38);
        resizeCalls = 0;
        label = MakeText(.09, .05, 12);
        label.Text = "DeepLens v0.6.13 - waiting";
        footer = MakeText(.09, .91, 10);
        settingsStatus = MakeText(.09, .045, 10);
        settingsStatus.X = .48;
        settingsStatus.BackColor = ColorRef.FromRgb(25, 30, 38);
        settingsStatus.LineColor = ColorRef.FromRgb(95, 105, 120);
        settingsStatus.LineWidth = 1;
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
            headers[c] = MakeText(.10, .09, 10);
            footers[c] = MakeText(.10, .85, 10);
            for (int k = 0; k < MaxRows; k++)
            {
                profileRects[c,k] = MakeShape(AnnotationType.Rectangle);
                bidLabels[c,k] = MakeText(.10, .14, 10);
                askLabels[c,k] = MakeText(.10, .14, 10);
                bidLabels[c,k].LineWidth = askLabels[c,k].LineWidth = 1;
                bidLabels[c,k].FontBold = true;
                askLabels[c,k].FontBold = false;
                askLabels[c,k].TextOnlyInside = false;
                askLabels[c,k].CheckStringWidthInside = false;
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
            }
        }
    }

    private void ClearRows()
    {
        for (int k = 0; k < MaxRows; k++)
        {
            for (int c = 0; c < Columns; c++)
            {
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
                profileRects[c,k].Visible = false;
            }
        }
        for (int c = 0; c < Columns; c++)
        {
            candleMarkers[c].Visible = false;
            nativeCandles[c].Visible = false;
            headers[c].Text = "";
            footers[c].Text = "";
        }
        footer.Text = "";
        projected.Clear();
        projectedLabels.Clear();
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
        projectionStatus.Visible = !ShowAllParameters && NativeShapes;
        if (ShowAllParameters)
        {
            ClearRows();
            label.Text = "DeepLens v0.6.13 - parameter inspector";
            label.FontSize = 12;
            var parameters = VAn.GetParamsById(InspectIndex);
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
        var style = VAn.GetParamsById(InspectIndex);
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
        settingsStatus.Visible = true;
        settingsStatus.Text = $"Index {InspectIndex}: {(style == null ? "not found" : style.name)} | Params {parameterCount}\n"
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
            label.Text = "DeepLens v0.6.13 - select Delta / Profile / Volume / Manual / Fixed";
            return;
        }
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            ClearRows();
            label.Text = "DeepLens v0.6.13 - waiting for bars";
            return;
        }
        int end = VAn.BarIndex;
        if (end < 0 || end >= bars.Count) return;

        // Compact baseline: keep the user zoom control and scale all profile geometry together.
        const double compactScale = .75;
        double zoom = compactScale * Math.Max(60, Math.Min(180, Zoom)) / 100.0;
        double step = .027 * zoom;
        int rows = Math.Min(MaxRows, Math.Max(1, (int)(.68 / step)));
        int font = Math.Max(6, Math.Min(24, (int)Math.Round(sourceFont * zoom)));
        double stride = .13 * zoom;
        double askOffset = .036 * zoom;
        double left = .10;
        int high = int.MinValue, low = int.MaxValue;
        int latestLow = int.MaxValue, latestHigh = int.MinValue;
        int[] poc = new int[Columns];
        int[] maxVol = new int[Columns];
        long[] vol = new long[Columns];
        long[] delta = new long[Columns];
        bool[] has = new bool[Columns];

        for (int c = 0; c < Columns; c++)
        {
            int index = end - Columns + 1 + c;
            if (index < 0) continue;
            var bar = bars[index];
            if (bar.VbpListAll == null) continue;
            foreach (var entries in bar.VbpListAll.SortedLHValues)
            {
                if (entries == null || entries.Length == 0 || entries[0] == null) continue;
                var level = entries[0];
                high = Math.Max(high, level.PinT);
                low = Math.Min(low, level.PinT);
                if (c == Columns - 1)
                {
                    latestLow = Math.Min(latestLow, level.PinT);
                    latestHigh = Math.Max(latestHigh, level.PinT);
                }
                vol[c] += level.TotVol;
                delta[c] += (long)level.AskVol - level.BidVol;
                if (!has[c] || level.TotVol > maxVol[c])
                {
                    maxVol[c] = level.TotVol;
                    poc[c] = level.PinT;
                }
                has[c] = true;
            }
        }

        if (high == int.MinValue)
        {
            ClearRows();
            label.Text = "DeepLens v0.6.13 - no VBP levels";
            return;
        }

        // Keep the current POC within the visible shared price range.
        int focus = latestHigh == int.MinValue ? high : Math.Max(latestLow, Math.Min(latestHigh, poc[Columns - 1]));
        int range = high - low + 1;
        int group = manualTicks;
        // Candidate fixed grid anchored at tick zero; compare against native prices.
        int gridLow = (int)Math.Floor((double)low / group) * group;
        int gridHigh = (int)Math.Floor((double)high / group) * group + group - 1;
        int focusHigh = (int)Math.Floor((double)(focus + DetailOffset) / group) * group + group - 1;
        int top = Math.Min(gridHigh, Math.Max(gridLow + rows * group - 1, focusHigh + (rows / 2) * group));
        int visibleRows = Math.Min(rows, (top - gridLow + 1) / group);
        ClearRows();
        label.Text = $"DeepLens v0.6.13 | DELTA PROFILE TEST | {group} ticks/row | Zoom {Zoom}%";
        label.FontSize = font + 2;
        settingsStatus.Visible = true;

        for (int c = 0; c < Columns; c++)
        {
            int index = end - Columns + 1 + c;
            double x = left + c * stride;
            headers[c].CoordinateXType = headers[c].CoordinateYType = CoordinateTypeEnum.Relative;
            footers[c].CoordinateXType = footers[c].CoordinateYType = CoordinateTypeEnum.Relative;
            headers[c].X = x;
            headers[c].Y = .09;
            footers[c].Y = .85;
            headers[c].FontSize = font;
            footers[c].X = x;
            footers[c].FontSize = font;
            if (index < 0) continue;
            var bar = bars[index];
            headers[c].Text = $"{bar.dt:HH:mm} " + (c == Columns - 1 ? "LIVE" : "");
            footers[c].Text = has[c] ? $"V {vol[c]}\nD {delta[c]}" : "No VBP";
            if (NativeShapes)
            {
                projectedLabels.Add((headers[c], x, .09));
                projectedLabels.Add((footers[c], x, .85));
            }
            if (!has[c]) continue;
            // Text-based candle preview: each row corresponds to an inherited price bucket.
            // Body and wick are quantized to that grid, not exact sub-row geometry.
            int candleHigh = bar.HighInT, candleLow = bar.LowInT;
            int candleOpen = bar.OpenInT, candleClose = bar.CloseInT;
            settingsStatus.Text += $"\nBar {index}: OHLC ticks {candleOpen}/{candleHigh}/{candleLow}/{candleClose}";
            if (NativeShapes && outsideStyle == 1 && markerAlignment == 0 && candleHigh >= candleLow)
            {
                // Official native candle layout, projected onto the independent relative Y grid.
                double yOpen = .14 + (top - (double)candleOpen) * step / group;
                double yClose = .14 + (top - (double)candleClose) * step / group;
                double yHigh = .14 + (top - (double)candleHigh) * step / group;
                double yLow = .14 + (top - (double)candleLow) * step / group;
                double yMin = .14 - step / 2, yMax = .14 + (visibleRows - .5) * step;
                if (yLow >= yMin && yHigh <= yMax)
                {
                    var candle = nativeCandles[c];
                    double halfWidth = Math.Max(.0015, Math.Min(.005, markerWidth * .0015)) * zoom;
                    projected.Add((candle, x - .009 * zoom - halfWidth, x - .009 * zoom + halfWidth,
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
                for (int k = 0; k < visibleRows; k++)
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
                    mark.X = x - .012 * zoom;
                    mark.Y = .14 + firstRow * step;
                    mark.FontSize = font;
                    mark.Text = marker;
                    mark.ForeColor = ColorRef.FromRgb(165, 170, 190);
                    mark.Visible = true;
                }
            }
            long[] bids = new long[MaxRows];
            long[] asks = new long[MaxRows];
            long[] totals = new long[MaxRows];
            bool[] present = new bool[MaxRows];
            var fullDelta = new Dictionary<int, long>();
            foreach (var entries in bar.VbpListAll.SortedLHValues)
            {
                if (entries == null || entries.Length == 0 || entries[0] == null) continue;
                var level = entries[0];
                int bucket = (int)Math.Floor((double)level.PinT / group);
                long oldDelta = 0;
                fullDelta.TryGetValue(bucket, out oldDelta);
                fullDelta[bucket] = oldDelta + (long)level.AskVol - level.BidVol;
                if (level.PinT > top || level.PinT < low) continue;
                int k = (top - level.PinT) / group;
                if (k >= visibleRows) continue;
                bids[k] += level.BidVol;
                asks[k] += level.AskVol;
                totals[k] += level.TotVol;
                present[k] = true;
            }
            long deltaScale = 1;
            foreach (var value in fullDelta.Values)
                deltaScale = Math.Max(deltaScale, Math.Abs(value));
            long scale = 1;
            for (int k = 0; k < visibleRows; k++)
                if (totals[k] > scale) scale = totals[k];
            for (int k = 0; k < visibleRows; k++)
            {
                if (!present[k]) continue;
                var b = bidLabels[c,k];
                var a = askLabels[c,k];
                b.X = x;
                a.X = x + askOffset;
                b.Y = a.Y = .14 + k * step;
                b.FontSize = font;
                a.FontSize = Math.Min(25, font + 1);
                int red = (int)(120.0 * bids[k] / scale);
                int green = (int)(120.0 * asks[k] / scale);
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
                long d = asks[k] - bids[k];
                int blocks = d == 0 ? 0 : Math.Max(1, (int)Math.Round(7.0 * Math.Abs(d) / deltaScale));
                ColorRef profileColor = (d < 0 ? bidColor : askColor).WithOpacity((byte)(255 * Math.Max(0, Math.Min(100, opacity)) / 100));
                b.Text = blocks == 0 ? "" : new string('█', blocks);
                b.ForeColor = profileColor;
                b.BackColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                b.LineColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                b.LineWidth = 0;
                b.Visible = !NativeShapes && blocks > 0;
                if (NativeShapes && d != 0)
                {
                    var rect = profileRects[c,k];
                    projected.Add((rect, x, x + .065 * zoom * Math.Abs(d) / deltaScale,
                        .14 + (k - .5) * step, .14 + (k + .5) * step, 0, 0, false));
                    rect.BackColor = profileColor;
                    rect.LineColor = isPoc ? pocColor : profileColor;
                    rect.LineWidth = isPoc ? pocWidth : 0;
                    rect.Visible = false;
                }
                a.CoordinateXType = a.CoordinateYType = CoordinateTypeEnum.Relative;
                a.X = x;
                if (NativeShapes)
                    projectedLabels.Add((a, x, .14 + (k - .35) * step));
                a.Text = !showZero && d == 0 ? "" : $"{d}";
                a.BackColor = ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                a.ForeColor = d < 0 ? bidText : askText;
                a.LineColor = isPoc && !NativeShapes ? pocColor : ColorRef.FromRgb(0, 0, 0).WithOpacity(0);
                a.LineWidth = isPoc && !NativeShapes ? pocWidth : 0;
                a.Visible = showZero || d != 0;
            }
        }
        if (NativeShapes) ProjectShapes();
        footer.Text = $"Fixed-grid comparison | {visibleRows} rows | offset {DetailOffset} ticks";
    }
}
