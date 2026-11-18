namespace CleanTool.Core.Pipeline;

/// <summary>
/// Turns a report into plain text lines. Platform independent; the AutoCAD layer writes these lines to the
/// command line and the dialog shows them in its results panel.
/// </summary>
public static class CleanReportFormatter
{
    /// <summary>Formats the report as lines: a header and one line per operation, followed by its messages.</summary>
    public static IReadOnlyList<string> Format(CleanReport report)
    {
        var lines = new List<string>
        {
            report.IsDryRun ? "CleanTool preview (no changes made):" : "CleanTool results:"
        };

        var verb = report.IsDryRun ? "would change" : "changed";

        foreach (var r in report.Results)
        {
            lines.Add(r.Status switch
            {
                OperationStatus.Failed => $"  [{r.DisplayName}] FAILED, rolled back: {r.Error}",
                OperationStatus.NothingToDo => $"  [{r.DisplayName}] nothing to do (examined {r.Examined})",
                _ => $"  [{r.DisplayName}] examined {r.Examined}, {verb} {r.Changed}, skipped {r.Skipped}, failed {r.Failed}"
            });

            lines.AddRange(r.Messages.Select(m => "    - " + m));
        }

        return lines;
    }
}
