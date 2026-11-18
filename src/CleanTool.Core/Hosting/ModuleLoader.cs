namespace CleanTool.Core.Hosting;

/// <summary>
/// An independently developed tool (e.g. Clean, WallTrim). It owns its commands, operations and settings and
/// never references other tools. Everything shared lives in Core and in the shared AutoCAD infrastructure.
/// </summary>
public interface IToolModule
{
    /// <summary>Unique short name (shown in reports and error messages).</summary>
    string Name { get; }

    /// <summary>Called once while the plug-in loads. If it throws, only this module is disabled.</summary>
    void Initialize();

    /// <summary>Called when AutoCAD shuts down.</summary>
    void Terminate() { }
}

/// <summary>The outcome of loading one module.</summary>
public sealed record ModuleStatus(string Name, bool IsLoaded, string? Error)
{
    public override string ToString() => IsLoaded ? $"{Name}: loaded" : $"{Name}: FAILED - {Error}";
}

/// <summary>
/// Loads modules in isolation: if one throws in Initialize the others still load, and running a command of the
/// failed module reports why it is unavailable. Independent of AutoCAD, so it can be unit tested.
/// </summary>
public sealed class ModuleLoader
{
    private readonly Dictionary<string, IToolModule> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModuleStatus> _statuses = new();

    /// <summary>Load results of all modules, in load order.</summary>
    public IReadOnlyList<ModuleStatus> Statuses => _statuses;

    /// <summary>Initializes the given modules one by one; a failing module is recorded and skipped.</summary>
    /// <param name="modules">Modules to load.</param>
    /// <returns>The status of every module loaded so far.</returns>
    /// <exception cref="InvalidOperationException">Two modules have the same name.</exception>
    public IReadOnlyList<ModuleStatus> Load(IEnumerable<IToolModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            if (_loaded.ContainsKey(module.Name) || _failed.ContainsKey(module.Name))
                throw new InvalidOperationException($"A module named '{module.Name}' is already registered.");

            try
            {
                module.Initialize();
                _loaded[module.Name] = module;
                _statuses.Add(new ModuleStatus(module.Name, true, null));
            }
            catch (Exception ex)
            {
                // Boundary: one tool failing must not affect the other tools or AutoCAD.
                _failed[module.Name] = ex.Message;
                _statuses.Add(new ModuleStatus(module.Name, false, ex.Message));
            }
        }

        return _statuses;
    }

    /// <summary>
    /// Returns a loaded module. If it is not registered or failed to load, returns false and a message to show the user.
    /// </summary>
    public bool TryGet<TModule>(out TModule module, out string error) where TModule : class, IToolModule
    {
        var found = _loaded.Values.OfType<TModule>().FirstOrDefault();
        if (found is not null)
        {
            module = found;
            error = string.Empty;
            return true;
        }

        module = null!;
        var failure = _failed.FirstOrDefault(f => string.Equals(f.Key, ModuleNameHint<TModule>(), StringComparison.OrdinalIgnoreCase));
        error = failure.Key is not null
            ? $"{failure.Key} is not available: it failed to load ({failure.Value})."
            : $"{typeof(TModule).Name} is not loaded.";
        return false;
    }

    /// <summary>Terminates all modules; one module throwing does not stop the others.</summary>
    public void TerminateAll()
    {
        foreach (var module in _loaded.Values)
        {
            try { module.Terminate(); }
            catch { /* Nothing to do during shutdown; never block AutoCAD from closing. */ }
        }
    }

    /// <summary>"CleanModule" → "Clean": naming convention used to find the module name for error messages.</summary>
    private static string ModuleNameHint<TModule>()
    {
        var name = typeof(TModule).Name;
        return name.EndsWith("Module", StringComparison.Ordinal) ? name[..^"Module".Length] : name;
    }
}
