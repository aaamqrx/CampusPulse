namespace CampusPulse.Core;

public static class DiagnosticText
{
    public static string Format(IEnumerable<StatusEntry> entries) => string.Join(Environment.NewLine,
        entries.OrderByDescending(entry => entry.Time)
            .Select(entry => $"{entry.Time.ToLocalTime():MM-dd HH:mm:ss}  {entry.Message}"));
}
