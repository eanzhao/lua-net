using System.Text;

namespace Lua.Core.Extensions;

public static class StringBuilderExtensions
{
    public static void PrintAndCollect(this StringBuilder sb, string content)
    {
        sb.AppendLine(content);
        Console.WriteLine(content);
    }
}