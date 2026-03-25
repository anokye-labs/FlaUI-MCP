namespace FlaUI.Mcp.Tests;

public class ProgramTests
{
    [Fact]
    public void ParsePort_NoArgs_ReturnsNull()
    {
        var result = CliArgs.ParsePort([]);
        Assert.Null(result);
    }

    [Fact]
    public void ParsePort_WithValidPort_ReturnsPort()
    {
        var result = CliArgs.ParsePort(["--port", "8765"]);
        Assert.Equal(8765, result);
    }

    [Fact]
    public void ParsePort_WithInvalidPort_ReturnsNull()
    {
        var result = CliArgs.ParsePort(["--port", "notanumber"]);
        Assert.Null(result);
    }

    [Fact]
    public void ParsePort_PortFlagWithoutValue_ReturnsNull()
    {
        var result = CliArgs.ParsePort(["--port"]);
        Assert.Null(result);
    }

    [Fact]
    public void ParsePort_OtherArgsIgnored_ReturnsNull()
    {
        var result = CliArgs.ParsePort(["--verbose", "--config", "foo.json"]);
        Assert.Null(result);
    }

    [Fact]
    public void ParsePort_MixedArgs_ReturnsPort()
    {
        var result = CliArgs.ParsePort(["--verbose", "--port", "3000", "--config", "foo.json"]);
        Assert.Equal(3000, result);
    }
}
