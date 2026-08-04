using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OD.MyLocalAgent.Core;

internal sealed class WindowsRecycleBinService : IRecycleBinService
{
    private const uint DeleteOperation = 3;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoErrorUi = 0x0400;

    public Task MoveToBinAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => MoveCore(absolutePath, cancellationToken),
            cancellationToken);
    }

    private static void MoveCore(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = new ShellFileOperation
        {
            Operation = DeleteOperation,
            From = absolutePath + '\0' + '\0',
            Flags = Silent | NoConfirmation | AllowUndo | NoErrorUi,
        };
        var result = SHFileOperation(ref operation);
        if (result != 0)
        {
            throw new Win32Exception(
                result,
                "Windows could not move the path to the Recycle Bin.");
        }

        if (operation.AnyOperationsAborted)
        {
            throw new OperationCanceledException(
                "Windows canceled the Recycle Bin operation.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public nint WindowHandle;
        public uint Operation;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public nint NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShellFileOperation operation);
#pragma warning restore SYSLIB1054
}
