using Autodesk.AutoCAD.EditorInput;
using CleanTool.Core.Hosting;

namespace CleanTool.AcadTool.Hosting;

/// <summary>
/// Access point from commands to the loaded tool modules.
/// AutoCAD instantiates command classes itself (no constructor injection), so modules are fetched from here.
/// </summary>
internal static class ToolHost
{
    private static ModuleLoader _loader = new();

    /// <summary>Loads the modules (replacing any previously loaded set) and returns their statuses.</summary>
    public static IReadOnlyList<ModuleStatus> Load(IEnumerable<IToolModule> modules)
    {
        _loader = new ModuleLoader();
        return _loader.Load(modules);
    }

    /// <summary>Load results of all modules.</summary>
    public static IReadOnlyList<ModuleStatus> Statuses => _loader.Statuses;

    /// <summary>
    /// Used at the start of every command: if the module is not loaded, writes the reason to the command line and returns false.
    ///   if (!ToolHost.TryGet&lt;CleanModule&gt;(editor, out var clean)) return;
    /// </summary>
    public static bool TryGet<TModule>(Editor editor, out TModule module) where TModule : class, IToolModule
    {
        if (_loader.TryGet(out module, out var error)) return true;

        editor.WriteMessage($"\n[!] {error}");
        return false;
    }

    /// <summary>Terminates all modules (called on AutoCAD shutdown).</summary>
    public static void TerminateAll() => _loader.TerminateAll();
}
