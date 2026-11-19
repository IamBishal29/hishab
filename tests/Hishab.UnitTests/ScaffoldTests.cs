namespace Hishab.UnitTests;

public class ScaffoldTests
{
    [Fact]
    public void Domain_assembly_is_loadable()
    {
        typeof(Domain.AssemblyMarker).Assembly.GetName().Name.ShouldBe("Hishab.Domain");
    }
}
