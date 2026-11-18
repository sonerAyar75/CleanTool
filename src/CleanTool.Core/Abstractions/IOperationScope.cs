namespace CleanTool.Core.Abstractions;

/// <summary>
/// Atomic unit of work for one operation (an AutoCAD transaction in the AutoCAD layer).
/// If it is disposed without calling <see cref="Complete"/>, every change made inside it is rolled back.
/// </summary>
public interface IOperationScope : IDisposable
{
    void Complete();
}

/// <summary>Creates a new <see cref="IOperationScope"/> for each operation.</summary>
public interface IOperationScopeFactory
{
    IOperationScope Begin(string operationId);
}
