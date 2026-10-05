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

    /// <summary>
    /// Catches files saved through a tool that read UTF-8 as Windows-1252 ("Skåne" → "SkÃ¥ne", "…" → "â€¦").
    /// </summary>
    [Fact]
    public void Source_files_have_no_double_encoded_text()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PhotoSorter.sln"))) root = root.Parent;
        Assert.NotNull(root);

        string[] patterns = ["*.cs", "*.xaml", "*.csproj", "*.md", "*.yml", "*.props"];
        var broken = patterns
            .SelectMany(p => Directory.EnumerateFiles(root.FullName, p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.EndsWith(nameof(ArchitectureTests) + ".cs"))
            .Where(f => File.ReadAllText(f).Contains('Ã') || File.ReadAllText(f).Contains("â€"))
            .Select(f => Path.GetRelativePath(root.FullName, f))
            .ToList();

        Assert.Empty(broken);
    }

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
