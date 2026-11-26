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

    public override void OnSet(bool setDefault, bool themeOverride)
    {
        OnEndCall = CallHandler.HistRT;
        NeedVbp = true;
        Description = "VBP data test";
    }

    public override void OnLoad()
    {
        label = VAn.CreateAnnotation(AnnotationType.Text);
        label.CoordinateXType = CoordinateTypeEnum.Relative;
        label.CoordinateYType = CoordinateTypeEnum.Relative;
        label.X = 0.15;
        label.Y = 0.10;
        label.TextAlign = TextAlignment.VLeftHTop;
        label.FontSize = 16;
        label.FontBold = true;
        label.ForeColor = ColorRef.FromRgb(230, 235, 240);
        label.Text = "DeepLens v0.2.4 - waiting for VBP data";
        VAn.AddAnnotation(IndVars.FrontAnnList, label);
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        var bars = VAn.BarVars;
        if (bars == null || bars.Count == 0)
        {
            label.Text = "DeepLens v0.2.4 - waiting for bars";
            return;
        }
        int index = VAn.BarIndex;
        if (index < 0 || index >= bars.Count)
        {
            label.Text = "DeepLens v0.2.4 - invalid bar index";
            return;
        }
        var bar = bars[index];
        if (bar.VbpListAll == null)
        {
            label.Text = "DeepLens v0.2.4 - VBP NULL";
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
        label.Text = $"DeepLens v0.2.4 | Index: {index} | Levels: {count}\n"
            + $"Bid: {bid} | Ask: {ask} | Total: {total} | Delta: {ask - bid}\n"
            + (count == 0 ? "POC: N/A" : $"POC tick: {pocTick} | Vol: {pocVol} | {pocBid} x {pocAsk}");
    }
}
