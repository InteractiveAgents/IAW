namespace Core.V3.Diagnostics;

public interface ISelfDiagnosable
{
    Task<DiagnosticReport> DiagnoseAsync(CancellationToken ct = default);
}
