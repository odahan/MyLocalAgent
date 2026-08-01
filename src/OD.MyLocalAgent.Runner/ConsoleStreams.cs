using System.Runtime.InteropServices;
using System.Text;

namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Acquires runner console writers for the windowless (WinExe) subsystem (§19.1 S16-D2).
/// Redirected streams are used as provided; otherwise the runner attaches to the parent
/// shell console when one exists so manual invocations keep printing.
/// </summary>
internal static partial class ConsoleStreams
{
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Resolves the output and error writers for this process.
    /// </summary>
    /// <returns>The output and error writers.</returns>
    public static (TextWriter Output, TextWriter Error) Acquire()
    {
        if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
        {
            if (AttachConsole(AttachParentProcess))
            {
                return (
                    Console.IsOutputRedirected
                        ? Console.Out
                        : CreateAttachedWriter(Console.OpenStandardOutput()),
                    Console.IsErrorRedirected
                        ? Console.Error
                        : CreateAttachedWriter(Console.OpenStandardError()));
            }
        }

        return (Console.Out, Console.Error);
    }

    private static TextWriter CreateAttachedWriter(Stream stream) =>
        stream == Stream.Null
            ? TextWriter.Null
            : new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
