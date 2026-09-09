using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.Web.Extensions;

using XafAIReportDesigner.Module.Services;

namespace XafAIReportDesigner.Web.Services;

/// <summary>
/// Report storage for the Web Report Designer backed by the XAF ReportDataV2 table —
/// the same storage the WinForms designer uses, so both hosts see the same reports.
/// </summary>
public sealed class ReportDataV2Storage(ReportDataV2Store store) : ReportStorageWebExtension
{
    public override bool CanSetData(string url) => true;

    public override bool IsValidUrl(string url) => ReportDataV2Store.IsValidName(url);

    public override byte[] GetData(string url)
    {
        var data = store.Load(url);
        if (data == null)
            throw new InvalidOperationException($"Report '{url}' was not found in ReportDataV2.");
        return data;
    }

    public override Dictionary<string, string> GetUrls() =>
        store.ListNames().ToDictionary(n => n, n => n);

    public override void SetData(XtraReport report, string url)
    {
        using var stream = new MemoryStream();
        report.SaveLayoutToXml(stream);
        store.Save(url, stream.ToArray());
    }

    /// <summary>Designer "Save As": create-only, so it can never overwrite another report (RPT-010).</summary>
    public override string SetNewData(XtraReport report, string defaultUrl)
    {
        using var stream = new MemoryStream();
        report.SaveLayoutToXml(stream);
        store.Insert(defaultUrl, stream.ToArray());
        return defaultUrl;
    }
}
