namespace IAW.Core.Diagnostics;

public interface ISelfDiagnosable
{
    Task<DiagnosticReport> DiagnoseAsync(CancellationToken ct = default);
}
