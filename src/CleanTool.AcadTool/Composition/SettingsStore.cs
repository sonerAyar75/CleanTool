namespace CleanTool.AcadTool.Composition;

/// <summary>
/// Settings valid for the AutoCAD session and the operations checked in the dialog.
/// Both the CT_* commands and the dialog read from here, so a setting changed in the dialog applies to the commands too.
/// (Saving to a JSON file will be added here.)
/// </summary>
internal sealed class SettingsStore
{
    private HashSet<string> _enabled;

    /// <summary>Creates the store.</summary>
    /// <param name="initial">Initial settings.</param>
    /// <param name="enabledOperationIds">Operations checked in the dialog at start.</param>
    public SettingsStore(ToolSettings initial, IEnumerable<string> enabledOperationIds)
    {
        Current = initial;
        _enabled = new HashSet<string>(enabledOperationIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The current settings.</summary>
    public ToolSettings Current { get; private set; }

    /// <summary>Is the operation checked in the dialog?</summary>
    public bool IsEnabled(string operationId) => _enabled.Contains(operationId);

    /// <summary>Replaces the settings and the set of checked operations.</summary>
    public void Update(ToolSettings settings, IEnumerable<string> enabledOperationIds)
    {
        Current = settings ?? throw new ArgumentNullException(nameof(settings));
        _enabled = new HashSet<string>(enabledOperationIds, StringComparer.OrdinalIgnoreCase);
    }
}
