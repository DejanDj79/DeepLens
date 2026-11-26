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
        label.Text = "DeepLens v0.2.1 - waiting for VBP data";
        VAn.AddAnnotation(IndVars.FrontAnnList, label);
    }

    public override void OnEnd(bool isRt)
    {
        if (label == null) return;
        try
        {
            var bars = VAn.BarVars;
            if (bars == null || bars.Count == 0)
            {
                label.Text = "DeepLens v0.2.1 - waiting for bars";
                return;
            }
            int end = Math.Min(VAn.BarIndex, bars.Count - 1);
            var lines = new List<string> { "DEEPLENS v0.2.1 - VBP TEST", $"Bars: {bars.Count} | Index: {end}" };
            for (int i = Math.Max(0, end - 2); i <= end; i++)
            {
                var bar = bars[i];
                var levels = bar.VbpListAll?.SortedLHValues
                    .Where(a => a != null && a.Length > 0 && a[0] != null)
                    .Select(a => a[0]).ToList();
                lines.Add($"{bar.dt:HH:mm:ss} | Levels: {levels?.Count ?? 0}");
                if (levels != null && levels.Count > 0)
                {
                    var level = levels.OrderByDescending(l => l.TotVol).First();
                    lines.Add($"Tick {level.PinT} | Bid {level.BidVol} x Ask {level.AskVol}");
                }
            }
            label.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            label.Text = "DeepLens v0.2.1 VBP error: " + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
