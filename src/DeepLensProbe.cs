using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
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
            Description = "Minimal registration and overlay test.",
            Tags = new List<string> { "Utility" }
        };
    }

    private IAnnotation label;
    private const int MaxRows = 24;
    private readonly IAnnotation[] bidLabels = new IAnnotation[MaxRows];
    private readonly IAnnotation[] askLabels = new IAnnotation[MaxRows];
    private readonly IAnnotation[] priceLabels = new IAnnotation[MaxRows];
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
        Description = "DeepLens v0.3.4 renderer test";
    }

    public override void OnLoad()
    {
        label = MakeText(.09, .06, 12);
        label.Text = "DeepLens v0.3.4 - waiting";
        var headers = MakeText(.10, .105, 10);
        headers.Text = "BID";
        headers = MakeText(.172, .105, 10);
        headers.Text = "ASK";
        headers = MakeText(.24, .105, 10);
        headers.Text = "PRICE";
        for (int i = 0; i < MaxRows; i++)
        {
            double y = .14 + i * .028;
            bidLabels[i] = MakeText(.10, y, 10);
            askLabels[i] = MakeText(.172, y, 10);
            priceLabels[i] = MakeText(.24, y, 10);
            bidLabels[i].LineWidth = askLabels[i].LineWidth = 1;
            bidLabels[i].FontBold = askLabels[i].FontBold = true;
            bidLabels[i].Visible = false;
            askLabels[i].Visible = false;
            priceLabels[i].Visible = false;
        }
        footer = MakeText(.09, .83, 11);
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            label.Text = "DeepLens v0.3.4 - waiting for bars";
            return;
        }
        int index = VAn.BarIndex;
        if (index < 0 || index >= bars.Count)
        {
            label.Text = "DeepLens v0.3.4 - invalid bar index";
            return;
        }
        var bar = bars[index];
        if (bar.VbpListAll == null)
        {
            label.Text = "DeepLens v0.3.4 - VBP NULL";
            return;
        }
        int count = 0;
        long bid = 0, ask = 0, total = 0;
        int pocTick = 0, pocVol = -1, pocBid = 0, pocAsk = 0;
        foreach (var entries in bar.VbpListAll.SortedLHValues)
        {
            if (entries == null || entries.Length == 0 || entries[0] == null)
                continue;
            var level = entries[0];
            count++;
            bid += level.BidVol;
            ask += level.AskVol;
            total += level.TotVol;
            if (level.TotVol > pocVol)
            {
                pocVol = level.TotVol;
                pocTick = level.PinT;
                pocBid = level.BidVol;
                pocAsk = level.AskVol;
            }
        }
        label.Text = $"DeepLens v0.3.4 | Bar {index} | {count} levels";
        footer.Text = $"Vol {total} | Delta {ask - bid} | POC {VAn.FromPinT(pocTick).Price:0.00}";
        int row = 0;
        int skip = Math.Max(0, count - MaxRows);
        foreach (var entries in bar.VbpListAll.SortedLHValues)
        {
            if (entries == null || entries.Length == 0 || entries[0] == null)
                continue;
            if (skip > 0) { skip--; continue; }
            if (row >= MaxRows) break;
            var level = entries[0];
            int slot = Math.Min(count, MaxRows) - 1 - row;
            bool poc = level.PinT == pocTick;
            int red = pocVol > 0 ? (int)(120.0 * level.BidVol / pocVol) : 0;
            int green = pocVol > 0 ? (int)(120.0 * level.AskVol / pocVol) : 0;
            bidLabels[slot].BackColor = ColorRef.FromRgb((byte)Math.Min(255, 55 + red), 30, 40);
            askLabels[slot].BackColor = ColorRef.FromRgb(24, (byte)Math.Min(255, 55 + green), 45);
            bidLabels[slot].LineColor = askLabels[slot].LineColor = poc ? ColorRef.FromRgb(240, 185, 65) : ColorRef.FromRgb(65, 75, 85);
            bidLabels[slot].Text = $"  {level.BidVol,5}  ";
            askLabels[slot].Text = $"  {level.AskVol,5}  ";
            priceLabels[slot].Text = $"{VAn.FromPinT(level.PinT).Price:0.00}";
            bidLabels[slot].Visible = askLabels[slot].Visible = priceLabels[slot].Visible = true;
            row++;
        }
        for (int k = row; k < MaxRows; k++)
        {
            bidLabels[k].Visible = askLabels[k].Visible = priceLabels[k].Visible = false;
        }
    }
}
