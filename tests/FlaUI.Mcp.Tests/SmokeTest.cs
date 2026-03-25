namespace FlaUI.Mcp.Tests;

public class SmokeTest
{
    [Fact]
    public void ProjectReference_Compiles()
    {
        var registry = new Core.ElementRegistry();
        Assert.NotNull(registry);
    }
}
