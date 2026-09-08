namespace NektoTranslate.System.Contracts;


// Deliberately just these two fields. A shell polling this in a loop while the window is still
// hidden needs "is it up" and "which build is this," not a health report.
public sealed record HealthResponse(string status, string version);
