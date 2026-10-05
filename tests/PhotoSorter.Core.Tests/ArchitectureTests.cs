using System.Reflection;

namespace PhotoSorter.Core.Tests;

/// <summary>Guards the rule from HANDOFF.md: all logic lives in Core, and Core never touches UI.</summary>
public class ArchitectureTests
{
    private static readonly string[] UiAssemblies =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Windows.Forms",
        "Mormorskopiadammsugare", // the WPF app itself (assembly name)
    ];

    [Fact]
    public void Core_does_not_reference_UI_assemblies()
    {
        var core = Assembly.Load("PhotoSorter.Core");

        var uiRefs = core.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => UiAssemblies.Contains(name))
            .ToList();

        Assert.Empty(uiRefs);
    }
}
