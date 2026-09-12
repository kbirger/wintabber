namespace WinTabber.UI.Common.Tests;

/// <summary>
/// This project is a Phase 2+ scaffold with no real code yet, so it has no real tests either.
/// Microsoft.Testing.Platform treats a project with zero tests as a failure, which breaks
/// `dotnet test --solution WinTabber.slnx` for the whole repo. This placeholder exists only to
/// keep the project passing CI until Phase 2 adds real tests here.
/// </summary>
public class PlaceholderTests
{
    [Test]
    public async Task Placeholder_KeepsProjectInTestRun()
    {
        var assemblyName = typeof(PlaceholderTests).Assembly.GetName().Name;

        await Assert.That(assemblyName).IsEqualTo("WinTabber.UI.Common.Tests");
    }
}
