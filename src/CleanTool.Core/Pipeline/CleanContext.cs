using CleanTool.Core.Abstractions;

namespace CleanTool.Core.Pipeline;

/// <summary>Context shared by all operations during one run.</summary>
public sealed class CleanContext
{
    /// <summary>Creates a run context.</summary>
    /// <param name="dryRun">true for a preview that changes nothing.</param>
    /// <param name="logger">Where messages go; a null logger when omitted.</param>
    public CleanContext(bool dryRun = false, ICleanLogger? logger = null)
    {
        DryRun = dryRun;
        Logger = logger ?? NullCleanLogger.Instance;
    }

    /// <summary>When true this is a preview: nothing in the drawing is changed.</summary>
    public bool DryRun { get; }

    /// <summary>Where operations write user-facing messages.</summary>
    public ICleanLogger Logger { get; }
}
