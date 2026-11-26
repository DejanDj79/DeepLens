using System.ComponentModel;
using VolSysAPI;
using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
using VolumetricaCore;
using static VolSysAPI.ExternalStructure;

namespace DeepLens;

public class DeepLensProbe : Indicator
{
    public static IndicatorDescriptionBase Register() => new()
    {
        Name = "DeepLens Probe",
        Description = "Read-only footprint and overlay diagnostic",
        Tags = new List<string> { "Footprint", "Utility" }
    };

    [Category("Diagnostics")]
    [DisplayName("Indicator index to inspect")]
    public int TargetIndex { get; set; } = 0;

    private IAnnotation panel;
    private readonly List<IAnnotation> labels = new();
    private long liveTicks;
    private long liveOpens;
    private bool logged;
    private DateTime lastPaint;

    public override void OnSet(bool setDefault, bool themeOverride)
    {
        NeedVbp = true;
        OnEndCall = CallHandler.HistRT;
        OnTickCall = CallHandler.RT;
        OnOpenCall = CallHandler.RT;
    }

    public override void OnLoad()
    {
        liveTicks = liveOpens = 0;
        logged = false;
        lastPaint = DateTime.MinValue;
        labels.Clear();
        panel = VAn.CreateAnnotation(AnnotationType.Rectangle);
        panel.CoordinateXType = panel.CoordinateYType = CoordinateTypeEnum.Relative;
        panel.X = .08; panel.X2 = .76;
        panel.Y = .08; panel.Y2 = .75;
        panel.BackColor = ColorRef.FromRgb(20, 25, 32);
        panel.LineColor = ColorRef.FromRgb(100, 115, 130);
        VAn.AddAnnotation(IndVars.FrontAnnList, panel);
        for (int i = 0; i < 15; i++)
        {
            var t = VAn.CreateAnnotation(AnnotationType.Text);
            t.CoordinateXType = t.CoordinateYType = CoordinateTypeEnum.Relative;
            t.X = .10; t.Y = .095 + i * .04;
            t.TextAlign = TextAlignment.VLeftHTop;
            t.FontSize = 11;
            t.ForeColor = ColorRef.FromRgb(230, 235, 240);
            VAn.AddAnnotation(IndVars.FrontAnnList, t);
            labels.Add(t);
        }
    }

    public override void OnTick(VolumetricaAPI.Connection.Structure.TickByTick priceInfo, bool isRt, VolSysAPI.Structure.AggrInfo aggrInfo)
    {
        if (isRt) liveTicks++;
    }

    public override void OnOpen(bool isRt)
    {
        if (isRt) liveOpens++;
    }

    public override void OnEnd(bool isRt)
    {
        if (labels.Count != 15) return;
        var now = DateTime.UtcNow;
        if (isRt && (now - lastPaint).TotalMilliseconds < 200) return;
        lastPaint = now;
        var lines = new List<string> {
            "DEEPLENS PROBE v0.1",
            $"Live ticks: {liveTicks} | New bars: {liveOpens}"
        };
        try
        {
            var bars = VAn.BarVars;
            int end = Math.Min(VAn.BarIndex, bars.Count - 1);
            lines.Add($"Bars: {bars.Count} | Current index: {end}");
            for (int i = Math.Max(0, end - 2); i <= end; i++)
            {
                var bar = bars[i];
                var levels = bar.VbpListAll?.SortedLHValues
                    .Where(a => a != null && a.Length > 0 && a[0] != null)
                    .Select(a => a[0]).ToList();
                lines.Add($"{bar.dt:HH:mm:ss} | Levels: {levels?.Count ?? 0}");
                if (levels == null || levels.Count == 0)
                {
                    lines.Add("No VBP data");
                    continue;
                }
                foreach (var level in levels.OrderByDescending(l => l.TotVol).Take(2))
                    lines.Add($"Tick {level.PinT} | Bid {level.BidVol} x Ask {level.AskVol}");
            }
        }
        catch (Exception ex) { lines.Add("VBP error: " + ex.GetType().Name); }
        try
        {
            var p = VAn.GetParamsById(TargetIndex);
            lines.Add($"Index {TargetIndex}: {p?.name ?? "not found"}");
            lines.Add($"Settings: {p?.XMLPropList?.Count ?? 0} | Series: {p?.v_series?.Count ?? 0}");
            if (!logged && p != null)
            {
                logged = true;
                VAn.AddToLog($"DeepLens index {TargetIndex}: {p.name}; parameter names: " +
                    string.Join(", ", p.XMLPropList?.Select(x => x.Name) ?? Enumerable.Empty<string>()));
            }
        }
        catch (Exception ex) { lines.Add("Settings error: " + ex.GetType().Name); }
        for (int i = 0; i < labels.Count; i++)
            labels[i].Text = i < lines.Count ? lines[i] : "";
    }
}
