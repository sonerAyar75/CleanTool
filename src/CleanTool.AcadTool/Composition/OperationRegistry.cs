namespace CleanTool.AcadTool.Composition;

/// <summary>
/// Registered operation factories. Registration order = display order in the dialog. Execution order is decided by CleanStage.
/// </summary>
internal sealed class OperationRegistry
{
    private readonly List<IOperationFactory> _factories = new();

    /// <summary>Registered factories in registration (display) order.</summary>
    public IReadOnlyList<IOperationFactory> All => _factories;

    /// <summary>Registers a factory.</summary>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="InvalidOperationException">An operation with the same id is already registered.</exception>
    public OperationRegistry Register(IOperationFactory factory)
    {
        if (_factories.Any(f => string.Equals(f.Id, factory.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Operation '{factory.Id}' is already registered.");

        _factories.Add(factory);
        return this;
    }

    public IOperationFactory Get(string id) =>
        _factories.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Operation '{id}' is not registered.");
}
