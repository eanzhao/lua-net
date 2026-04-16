namespace Lua.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        var application = new LuaCliApplication();
        return application.Run(args);
    }
}
