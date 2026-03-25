namespace FlaUI.Mcp;

public static class CliArgs
{
    public static int? ParsePort(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out var port))
                return port;
        }
        return null;
    }
}
