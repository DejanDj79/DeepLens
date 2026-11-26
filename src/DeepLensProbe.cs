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
    [DisplayName("Auto Fit (group ticks)")]
    public bool AutoFit { get; set; } = true;

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
    public bool ShowAllParameters { get; set; } = true;

    private IAnnotation settingsStatus;
    private readonly IAnnotation[] parameterColumns = new IAnnotation[3];
    private const int Columns = 3;
    private const int MaxRows = 40;
    private IAnnotation label;
    private readonly IAnnotation[,] bidLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[,] askLabels = new IAnnotation[Columns, MaxRows];
    private readonly IAnnotation[] priceLabels = new IAnnotation[MaxRows];
    private readonly IAnnotation[] headers = new IAnnotation[Columns];
    private readonly IAnnotation[] footers = new IAnnotation[Columns];
    private IAnnotation footer;

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

    public override void OnSet(bool setDefault, bool themeOverride)
    {
        OnEndCall = CallHandler.HistRT;
        NeedVbp = true;
        Description = "DeepLens v0.4.5";
    }

    public override void OnLoad()
    {
        label = MakeText(.09, .05, 12);
        label.Text = "DeepLens v0.4.5 - waiting";
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
            headers[c] = MakeText(.10, .09, 10);
            footers[c] = MakeText(.10, .85, 10);
            for (int k = 0; k < MaxRows; k++)
            {
                bidLabels[c,k] = MakeText(.10, .14, 10);
                askLabels[c,k] = MakeText(.10, .14, 10);
                bidLabels[c,k].LineWidth = askLabels[c,k].LineWidth = 1;
                bidLabels[c,k].FontBold = askLabels[c,k].FontBold = true;
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
            }
        }
        for (int k = 0; k < MaxRows; k++)
        {
            priceLabels[k] = MakeText(.70, .14, 10);
            priceLabels[k].Visible = false;
        }
    }

    private void ClearRows()
    {
        for (int k = 0; k < MaxRows; k++)
        {
            priceLabels[k].Visible = false;
            for (int c = 0; c < Columns; c++)
                bidLabels[c,k].Visible = askLabels[c,k].Visible = false;
        }
        for (int c = 0; c < Columns; c++)
        {
            headers[c].Text = "";
            footers[c].Text = "";
        }
        footer.Text = "";
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        if (ShowAllParameters)
        {
            ClearRows();
            label.Text = "DeepLens v0.4.5 - parameter inspector";
            label.FontSize = 12;
            var parameters = VAn.GetParamsById(InspectIndex);
            var props = parameters == null ? null : parameters.XMLPropList;
            int count = props == null ? 0 : props.Count;
            settingsStatus.Visible = true;
            settingsStatus.Text = parameters == null
                ? $"Index {InspectIndex}: not found"
                : $"Index {InspectIndex}: {parameters.name} | XML: {count}";
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
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            ClearRows();
            label.Text = "DeepLens v0.4.5 - waiting for bars";
            return;
        }
        int end = VAn.BarIndex;
        if (end < 0 || end >= bars.Count) return;

        double zoom = Math.Max(60, Math.Min(180, Zoom)) / 100.0;
        double step = .027 * zoom;
        int rows = Math.Min(MaxRows, Math.Max(1, (int)(.68 / step)));
        int font = Math.Max(8, (int)(10 * zoom));
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
            label.Text = "DeepLens v0.4.5 - no VBP levels";
            return;
        }

        // Keep the current POC within the visible shared price range.
        int focus = latestHigh == int.MinValue ? high : Math.Max(latestLow, Math.Min(latestHigh, poc[Columns - 1]));
        int range = high - low + 1;
        int group = AutoFit ? Math.Max(1, (range + rows - 1) / rows) : 1;
        int top = AutoFit ? high : Math.Min(high, Math.Max(low + rows - 1, focus + DetailOffset + rows / 2));
        int visibleRows = Math.Min(rows, (top - low + group) / group);
        ClearRows();
        label.Text = $"DeepLens v0.4.5 | {(AutoFit ? "AUTO FIT" : "DETAIL")} | {group} tick/row | Zoom {Zoom}%";
        label.FontSize = font + 2;
        for (int k = 0; k < visibleRows; k++)
        {
            int tick = top - k * group;
            var p = priceLabels[k];
            p.X = left + Columns * stride;
            p.Y = .14 + k * step;
            p.FontSize = font;
            p.Text = group == 1 ? $"{VAn.FromPinT(tick).Price:0.00}" : $"{VAn.FromPinT(tick).Price:0.00} - {VAn.FromPinT(Math.Max(low, tick - group + 1)).Price:0.00}";
            p.Visible = true;
        }

        for (int c = 0; c < Columns; c++)
        {
            int index = end - Columns + 1 + c;
            double x = left + c * stride;
            headers[c].X = x;
            headers[c].FontSize = font;
            footers[c].X = x;
            footers[c].FontSize = font;
            if (index < 0) continue;
            var bar = bars[index];
            headers[c].Text = $"{bar.dt:HH:mm} " + (c == Columns - 1 ? "LIVE" : "");
            footers[c].Text = has[c] ? $"V {vol[c]}\nD {delta[c]}" : "No VBP";
            if (!has[c]) continue;
            long[] bids = new long[MaxRows];
            long[] asks = new long[MaxRows];
            long[] totals = new long[MaxRows];
            bool[] present = new bool[MaxRows];
            foreach (var entries in bar.VbpListAll.SortedLHValues)
            {
                if (entries == null || entries.Length == 0 || entries[0] == null) continue;
                var level = entries[0];
                if (level.PinT > top || level.PinT < low) continue;
                int k = (top - level.PinT) / group;
                if (k >= visibleRows) continue;
                bids[k] += level.BidVol;
                asks[k] += level.AskVol;
                totals[k] += level.TotVol;
                present[k] = true;
            }
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
                b.FontSize = a.FontSize = font;
                int red = (int)(120.0 * bids[k] / scale);
                int green = (int)(120.0 * asks[k] / scale);
                b.BackColor = ColorRef.FromRgb((byte)Math.Min(255, 55 + red), 30, 40);
                a.BackColor = ColorRef.FromRgb(24, (byte)Math.Min(255, 55 + green), 45);
                b.LineColor = a.LineColor = poc[c] <= top && (top - poc[c]) / group == k ? ColorRef.FromRgb(240,185,65) : ColorRef.FromRgb(65,75,85);
                b.Text = $"  {bids[k],5}  ";
                a.Text = $"  {asks[k],5}  ";
                b.Visible = a.Visible = true;
            }
        }
        footer.Text = AutoFit ? $"Full range: {range} ticks / {visibleRows} rows | grouped volumes" : $"Showing {visibleRows} of {range} ticks | detail offset {DetailOffset}";
    }
}
