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
    private readonly IAnnotation[] bidCells = new IAnnotation[MaxRows];
    private readonly IAnnotation[] askCells = new IAnnotation[MaxRows];
    private readonly IAnnotation[] bidLabels = new IAnnotation[MaxRows];
    private readonly IAnnotation[] askLabels = new IAnnotation[MaxRows];
    private readonly IAnnotation[] priceLabels = new IAnnotation[MaxRows];
    private IAnnotation background;
    private IAnnotation footer;
    private IAnnotation testRect;
    private IAnnotation testText;

    private IAnnotation MakeRect(double x, double y, double x2, double y2, ColorRef color)
    {
        var a = VAn.CreateAnnotation(AnnotationType.Rectangle);
        a.CoordinateXType = CoordinateTypeEnum.Relative;
        a.CoordinateYType = CoordinateTypeEnum.Relative;
        a.X = x; a.Y = y; a.X2 = x2; a.Y2 = y2;
        a.BackColor = color.WithOpacity(88);
        a.PlotForeground = true;
        a.Visible = true;
        a.LineColor = ColorRef.FromRgb(65, 75, 85);
        a.LineWidth = 2;
        VAn.AddAnnotation(IndVars.FrontAnnList, a);
        return a;
    }

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
        Description = "DeepLens v0.3.3 renderer test";
    }

    public override void OnLoad()
    {
        background = MakeRect(.08, .05, .34, .90, ColorRef.FromRgb(18, 25, 33));
        label = MakeText(.09, .06, 12);
        label.Text = "DeepLens v0.3.3 - waiting";
        for (int i = 0; i < MaxRows; i++)
        {
            double y = .14 + i * .028;
            bidCells[i] = MakeRect(.09, y, .16, y + .027, ColorRef.FromRgb(65, 30, 40));
            askCells[i] = MakeRect(.162, y, .232, y + .027, ColorRef.FromRgb(24, 63, 51));
            bidLabels[i] = MakeText(.10, y, 10);
            askLabels[i] = MakeText(.172, y, 10);
            priceLabels[i] = MakeText(.24, y, 10);
            bidCells[i].Visible = false;
            askCells[i].Visible = false;
            bidLabels[i].Visible = false;
            askLabels[i].Visible = false;
            priceLabels[i].Visible = false;
        }
        footer = MakeText(.09, .83, 11);
        testRect = MakeRect(.48, .15, .68, .30, ColorRef.FromRgb(180, 45, 45));
        testRect.Width = 160;
        testRect.Height = 70;
        testText = MakeText(.48, .35, 14);
        testText.Text = "TEXT BACKGROUND TEST";
        testText.BackColor = ColorRef.FromRgb(180, 45, 45);
        testText.LineColor = ColorRef.FromRgb(255, 255, 0);
        testText.LineWidth = 2;
        testText.Width = 180;
        testText.Height = 40;
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        testRect.X = .48; testRect.X2 = .68;
        testRect.Y = .15; testRect.Y2 = .30;
        testRect.LineColor = ColorRef.FromRgb(255, 255, 0);
        testRect.Visible = true;
        background.X = .08; background.X2 = .34;
        background.Y = .05; background.Y2 = .90;
        background.Visible = true;
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            label.Text = "DeepLens v0.3.3 - waiting for bars";
            return;
        }
        int index = VAn.BarIndex;
        if (index < 0 || index >= bars.Count)
        {
            label.Text = "DeepLens v0.3.3 - invalid bar index";
            return;
        }
        var bar = bars[index];
        if (bar.VbpListAll == null)
        {
            label.Text = "DeepLens v0.3.3 - VBP NULL";
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
        label.Text = $"DeepLens v0.3.3 | Bar {index} | {count} levels";
        footer.Text = $"Vol {total} | Delta {ask - bid} | POC tick {pocTick}";
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
            bidCells[slot].BackColor = ColorRef.FromRgb((byte)Math.Min(255, 55 + red), 30, 40);
            askCells[slot].BackColor = ColorRef.FromRgb(24, (byte)Math.Min(255, 55 + green), 45);
            bidCells[slot].LineColor = askCells[slot].LineColor = poc ? ColorRef.FromRgb(240, 185, 65) : ColorRef.FromRgb(65, 75, 85);
            bidLabels[slot].Text = $"{level.BidVol}";
            askLabels[slot].Text = $"{level.AskVol}";
            priceLabels[slot].Text = $"{VAn.FromPinT(level.PinT).Price:0.00}";
            double y = .14 + slot * .028;
            bidCells[slot].X = .09; bidCells[slot].X2 = .16;
            askCells[slot].X = .162; askCells[slot].X2 = .232;
            bidCells[slot].Y = askCells[slot].Y = y;
            bidCells[slot].Y2 = askCells[slot].Y2 = y + .027;
            bidCells[slot].Visible = askCells[slot].Visible = true;
            bidLabels[slot].Visible = askLabels[slot].Visible = priceLabels[slot].Visible = true;
            row++;
        }
        for (int k = row; k < MaxRows; k++)
        {
            bidCells[k].Visible = askCells[k].Visible = false;
            bidLabels[k].Visible = askLabels[k].Visible = priceLabels[k].Visible = false;
        }
    }
}
