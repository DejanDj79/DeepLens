# DeepLens

Desktop-only DeepCharts footprint magnifier research. V1 scope: one selection, one live overlay, inherit Deep Print settings where supported.

## Probe

Build on Windows with .NET 10 and local DeepCharts SDK DLLs:

```powershell
dotnet build src/DeepLens.csproj -p:DeepchartDevId=YOUR-DEVELOPER-ID
```

Restart DeepCharts and add Personal → DeepLens Probe to a normal candlestick chart. Use replay or SIM. The probe is read-only and never places orders. It displays the latest three bars' price-level data and checks a configurable indicator index. Compare values with native Deep Print using identical filters and tick grouping. Change the index to locate Deep Print, if it is exposed. Logs contain only parameter names for the selected index. Mouse selection and native renderer reuse are not implemented or confirmed. No SDK binaries or decompiled vendor sources are included.
