using System.Reflection;

namespace OD.MyLocalAgent.Runner;

internal static class Program
{
    private const string ProductName = "OD.MyLocalAgent Runner";

    private static void Main()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        Console.WriteLine($"{ProductName} {version.ToString(3)}");
    }
}
