using CleanTool.Core.Hosting;

namespace CleanTool.Core.Tests;

public class ModuleLoaderTests
{
    private sealed class CleanModule : IToolModule
    {
        public string Name => "Clean";
        public bool Initialized { get; private set; }
        public bool Terminated { get; private set; }
        public void Initialize() => Initialized = true;
        public void Terminate() => Terminated = true;
    }

    private sealed class WallTrimModule : IToolModule
    {
        public string Name => "WallTrim";
        public void Initialize() => throw new InvalidOperationException("settings file is corrupt");
    }

    private sealed class OtherModule : IToolModule
    {
        public string Name => "Other";
        public void Initialize() { }
        public void Terminate() => throw new Exception("boom");
    }

    [Fact]
    public void Failing_module_does_not_stop_the_others()
    {
        var loader = new ModuleLoader();
        var clean = new CleanModule();

        var statuses = loader.Load(new IToolModule[] { new WallTrimModule(), clean });

        Assert.True(clean.Initialized);
        Assert.False(statuses[0].IsLoaded);
        Assert.True(statuses[1].IsLoaded);
    }

    [Fact]
    public void Loaded_module_can_be_retrieved()
    {
        var loader = new ModuleLoader();
        var clean = new CleanModule();
        loader.Load(new IToolModule[] { clean });

        Assert.True(loader.TryGet<CleanModule>(out var found, out _));
        Assert.Same(clean, found);
    }

    [Fact]
    public void Failed_module_reports_why_it_is_unavailable()
    {
        var loader = new ModuleLoader();
        loader.Load(new IToolModule[] { new WallTrimModule() });

        Assert.False(loader.TryGet<WallTrimModule>(out _, out var error));
        Assert.Contains("WallTrim is not available", error);
        Assert.Contains("settings file is corrupt", error);
    }

    [Fact]
    public void Unregistered_module_is_reported_as_not_loaded()
    {
        var loader = new ModuleLoader();

        Assert.False(loader.TryGet<CleanModule>(out _, out var error));
        Assert.Contains("not loaded", error);
    }

    [Fact]
    public void Duplicate_module_names_are_rejected()
    {
        var loader = new ModuleLoader();

        Assert.Throws<InvalidOperationException>(() => loader.Load(new IToolModule[] { new CleanModule(), new CleanModule() }));
    }

    [Fact]
    public void Terminate_runs_for_all_modules_even_if_one_throws()
    {
        var loader = new ModuleLoader();
        var clean = new CleanModule();
        loader.Load(new IToolModule[] { new OtherModule(), clean });

        loader.TerminateAll();

        Assert.True(clean.Terminated);
    }

    [Fact]
    public void Status_text_is_readable()
    {
        Assert.Equal("Clean: loaded", new ModuleStatus("Clean", true, null).ToString());
        Assert.Equal("WallTrim: FAILED - x", new ModuleStatus("WallTrim", false, "x").ToString());
    }
}
