using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
/// <summary>Admitted original report bytes; integrity is not verification of supplied source claims.</summary>
public sealed class OperationalContextFile
{
    private readonly byte[] _original;
    public OperationalContextReport Report { get; }
    private OperationalContextFile(byte[] original, OperationalContextReport report) { _original = original; Report = report; }
    public byte[] CopyOriginal() => _original.ToArray();
    public static OperationalContextFile Read(byte[] original, string tenant)
    {
        if (original.Length > OperationalReportRegistry.MaximumBytes) throw new ConfigurationException("Operational evidence exceeds16MiB.");
        var copy = original.ToArray();
        string json;
        try { json = new UTF8Encoding(false, true).GetString(copy).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new ConfigurationException("Operational evidence must be strict UTF-8."); }
        return new(copy, OperationalContextSchema.Read(json, tenant));
    }
    public static OperationalContextFile ReadFile(string file, string tenant)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > OperationalReportRegistry.MaximumBytes) throw new ConfigurationException("Operational evidence exceeds16MiB.");
        using var reader = new BinaryReader(stream);
        return Read(reader.ReadBytes(OperationalReportRegistry.MaximumBytes + 1), tenant);
    }
    public static void VerifyOriginalExport(string file, byte[] original)
    {
        if (original.Length > OperationalReportRegistry.MaximumBytes) throw new ConfigurationException("Original operational evidence exceeds16MiB.");
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != original.Length || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), SHA256.HashData(original)))
            throw new IntegrityException("Original operational export readback differs. Retain the original without another service read.");
    }
}
