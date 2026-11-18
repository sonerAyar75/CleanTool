using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.Core.Abstractions;

namespace CleanTool.AcadTool.Infrastructure;

/// <summary>
/// Opens a separate AutoCAD transaction for every operation,
/// so that if one operation fails only its own changes are rolled back.
/// </summary>
internal sealed class AcadOperationScopeFactory : IOperationScopeFactory
{
    private readonly Document _document;

    /// <summary>Creates the factory for one document.</summary>
    public AcadOperationScopeFactory(Document document)
    {
        _document = document;
    }

    public IOperationScope Begin(string operationId) =>
        new AcadOperationScope(_document.TransactionManager.StartTransaction());

    private sealed class AcadOperationScope : IOperationScope
    {
        private readonly Transaction _transaction;
        private bool _completed;

        /// <summary>Wraps a started transaction.</summary>
        public AcadOperationScope(Transaction transaction)
        {
            _transaction = transaction;
        }

        /// <summary>Commits the transaction.</summary>
        public void Complete()
        {
            if (_completed) return;
            _transaction.Commit();
            _completed = true;
        }

        /// <summary>Aborts the transaction if it was not committed, then disposes it.</summary>
        public void Dispose()
        {
            if (!_completed) _transaction.Abort();
            _transaction.Dispose();
        }
    }
}
