namespace CleanTool.Core.Pipeline;

/// <summary>
/// Helper that lets operations accumulate counters and messages and finally produce an <see cref="OperationResult"/>.
/// The number of messages is capped so that reports stay readable on very large drawings.
/// </summary>
public sealed class OperationResultBuilder
{
    /// <summary>Default maximum number of messages kept per operation.</summary>
    public const int DefaultMaxMessages = 50;

    private readonly string _operationId;
    private readonly string _displayName;
    private readonly int _maxMessages;
    private readonly List<string> _messages = new();
    private int _suppressedMessages;

    /// <summary>Creates a builder.</summary>
    /// <param name="operationId">Id of the operation being reported.</param>
    /// <param name="displayName">Name shown in the report.</param>
    /// <param name="maxMessages">Messages beyond this number are counted but not kept.</param>
    public OperationResultBuilder(string operationId, string displayName, int maxMessages = DefaultMaxMessages)
    {
        _operationId = operationId;
        _displayName = displayName;
        _maxMessages = Math.Max(0, maxMessages);
    }

    /// <summary>Number of objects examined.</summary>
    public int Examined { get; private set; }
    /// <summary>Number of objects changed (or that would change in a preview).</summary>
    public int Changed { get; private set; }
    /// <summary>Number of objects deliberately left alone (e.g. on locked layers).</summary>
    public int Skipped { get; private set; }
    /// <summary>Number of objects that could not be changed.</summary>
    public int Failed { get; private set; }

    /// <summary>Counts examined objects.</summary>
    public void AddExamined(int count = 1) => Examined += count;

    /// <summary>Counts changed objects.</summary>
    public void AddChanged(int count = 1) => Changed += count;

    /// <summary>Counts a skipped object and optionally records why.</summary>
    public void AddSkipped(string? reason = null)
    {
        Skipped++;
        if (reason is not null) AddMessage(reason);
    }

    /// <summary>Counts a failed object and records why.</summary>
    public void AddFailed(string reason)
    {
        Failed++;
        AddMessage(reason);
    }

    /// <summary>Adds an informational message (subject to the message cap).</summary>
    public void AddMessage(string message)
    {
        if (_messages.Count < _maxMessages) _messages.Add(message);
        else _suppressedMessages++;
    }

    /// <summary>Creates the immutable result; the status is derived from the counters.</summary>
    public OperationResult Build()
    {
        var messages = new List<string>(_messages);
        if (_suppressedMessages > 0) messages.Add($"... and {_suppressedMessages} more message(s).");

        var status = Changed == 0 && Skipped == 0 && Failed == 0
            ? OperationStatus.NothingToDo
            : OperationStatus.Completed;

        return new OperationResult(_operationId, _displayName, status, Examined, Changed, Skipped, Failed, messages);
    }
}
