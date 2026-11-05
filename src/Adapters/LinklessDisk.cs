using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Snail.MCP.FileSystem.Adapters;

/// <summary>Files and folders reached with no link followed anywhere on their path, and changed relative to a folder held open.</summary>
/// <remarks>
/// A path comes from <c>SessionRoots.Reach</c> with every link on it resolved, so a link on it now means one was swapped in
/// after the check. Opened by name, the write followed it: a folder turned into a link out of the copy between the check
/// and the write put the file outside the session within three to six writes. Here the folder is opened by one call that
/// refuses a link anywhere on its path — <c>O_NOFOLLOW_ANY</c> on macOS, <c>openat2</c> with <c>RESOLVE_NO_SYMLINKS</c> on
/// Linux — and every file is created, renamed and opened relative to that open folder, so what was checked is what is
/// changed, whatever the path says by then. A link swapped in is a refusal, never a write somewhere else.
/// </remarks>
[UnsupportedOSPlatform("windows")]
internal static class LinklessDisk
{
    private const int WriteOnly = 0x1;
    private const int AlreadyThere = 17;
    private const int Everyone = 0x1FF;
    private const int Readable = 0x1B6;
    private const long OpenAt2 = 437;
    private const int WorkingFolder = -100;
    private const ulong NoSymlinks = 0x04;

    /// <summary>The flags whose values differ between the systems and, on Linux, between the processors.</summary>
    private static readonly Flags Machine = Flags.OfThisMachine();

    /// <summary>The mode a new file takes, as <c>open</c> would give it under the process's umask.</summary>
    /// <remarks>Set explicitly after creating: on Apple silicon the mode of <c>openat</c>, a variadic argument, does not reach the call.</remarks>
    private static readonly int Fresh = Readable & ~Umask();

    /// <summary>Replaces a file whole in one step: written beside it in the same open folder and renamed over it.</summary>
    /// <param name="path">The file, with every link on its path resolved.</param>
    /// <param name="bytes">What the file holds afterwards.</param>
    /// <param name="mode">The mode of the file it replaces, or null for a new file.</param>
    public static void Replace(string path, byte[] bytes, UnixFileMode? mode)
    {
        using SafeFileHandle folder = Folder(Path.GetDirectoryName(path)!);
        string name = Path.GetFileName(path);
        string beside = $".{name}.{Guid.NewGuid():N}.tmp";

        Written(folder, beside, bytes, mode is { } kept ? (int)kept : Fresh, path);

        if (RenameAt(folder, beside, folder, name) != 0)
        {
            IOException failure = Failure(path);
            _ = UnlinkAt(folder, beside, 0);

            throw failure;
        }
    }

    /// <summary>Makes a folder and every missing folder above it, each inside the one made or opened before it.</summary>
    public static void MakeFolder(string path)
    {
        string existing = path;
        var missing = new Stack<string>();

        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
        {
            missing.Push(Path.GetFileName(existing));
            existing = parent;
        }

        SafeFileHandle folder = Folder(existing);

        try
        {
            while (missing.TryPop(out string? name))
            {
                if (MakeAt(folder, name, Everyone) != 0 && Marshal.GetLastPInvokeError() != AlreadyThere)
                {
                    throw Failure(path);
                }

                SafeFileHandle inner = Opened(OpenAt(folder, name, Machine.Folder | Machine.NoFollow | Machine.CloseOnExec, 0), path);
                folder.Dispose();
                folder = inner;
            }
        }
        finally
        {
            folder.Dispose();
        }
    }

    /// <summary>A file opened for reading by one call that refuses a link anywhere on its path.</summary>
    /// <remarks>Opening a named pipe waits for a writer; the callers do not open a file of no bytes.</remarks>
    public static FileStream Reading(string path)
    {
        int file = OperatingSystem.IsMacOS()
            ? Open(path, Machine.CloseOnExec | Machine.NoFollowAnywhere)
            : OpenWithoutLinks(path, Machine.CloseOnExec);

        return new FileStream(Opened(file, path), FileAccess.Read);
    }

    /// <summary>A folder opened by one call that refuses a link anywhere on its path.</summary>
    private static SafeFileHandle Folder(string path)
    {
        int folder = OperatingSystem.IsMacOS()
            ? Open(path, Machine.Folder | Machine.CloseOnExec | Machine.NoFollowAnywhere)
            : OpenWithoutLinks(path, Machine.Folder | Machine.CloseOnExec);

        return Opened(folder, path);
    }

    private static void Written(SafeFileHandle folder, string beside, byte[] bytes, int mode, string path)
    {
        using SafeFileHandle file = Opened(OpenAt(folder, beside, WriteOnly | Machine.Create | Machine.Exclusive | Machine.NoFollow | Machine.CloseOnExec, mode), path);

        try
        {
            if (Chmod(file, mode) != 0)
            {
                throw Failure(path);
            }

            using var stream = new FileStream(file, FileAccess.Write);

            stream.Write(bytes);
        }
        catch (IOException)
        {
            _ = UnlinkAt(folder, beside, 0);

            throw;
        }
    }

    private static SafeFileHandle Opened(int descriptor, string path) =>
        descriptor < 0 ? throw Failure(path) : new SafeFileHandle(descriptor, ownsHandle: true);

    private static IOException Failure(string path) => new($"'{path}' cannot be reached without following a link: {Marshal.GetLastPInvokeErrorMessage()}");

    /// <summary>The process's umask, read by setting it and setting it back once, at start.</summary>
    private static int Umask()
    {
        int mask = SetUmask(0x12);

        _ = SetUmask(mask);

        return mask;
    }

    /// <summary>Linux: <c>openat2</c> from the working folder with no link resolved on the way.</summary>
    private static int OpenWithoutLinks(string path, int flags)
    {
        var how = new OpenHow { Flags = (ulong)flags, Resolve = NoSymlinks };

        return (int)Syscall(OpenAt2, WorkingFolder, path, ref how, (nuint)Marshal.SizeOf<OpenHow>());
    }

    /// <summary>The <c>struct open_how</c> of <c>openat2</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenHow
    {
        public ulong Flags;
        public ulong Mode;
        public ulong Resolve;
    }

    /// <summary>The open flags of this machine.</summary>
    /// <param name="Folder"><c>O_DIRECTORY</c>.</param>
    /// <param name="NoFollow"><c>O_NOFOLLOW</c>: the last name of the path is not a link.</param>
    /// <param name="Create"><c>O_CREAT</c>.</param>
    /// <param name="Exclusive"><c>O_EXCL</c>.</param>
    /// <param name="CloseOnExec"><c>O_CLOEXEC</c>.</param>
    /// <param name="NoFollowAnywhere"><c>O_NOFOLLOW_ANY</c> of macOS: no name of the path is a link.</param>
    private readonly record struct Flags(int Folder, int NoFollow, int Create, int Exclusive, int CloseOnExec, int NoFollowAnywhere)
    {
        public static Flags OfThisMachine()
        {
            if (OperatingSystem.IsMacOS())
            {
                return new Flags(0x100000, 0x100, 0x200, 0x800, 0x1000000, 0x20000000);
            }

            bool isArm = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;

            return isArm
                ? new Flags(0x4000, 0x8000, 0x40, 0x80, 0x80000, 0)
                : new Flags(0x10000, 0x20000, 0x40, 0x80, 0x80000, 0);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(SafeFileHandle folder, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, int mode);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Syscall(long number, int folder, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, nuint size);

    [DllImport("libc", EntryPoint = "renameat", SetLastError = true)]
    private static extern int RenameAt(SafeFileHandle fromFolder, [MarshalAs(UnmanagedType.LPUTF8Str)] string from, SafeFileHandle toFolder, [MarshalAs(UnmanagedType.LPUTF8Str)] string to);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(SafeFileHandle folder, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MakeAt(SafeFileHandle folder, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int mode);

    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static extern int Chmod(SafeFileHandle file, int mode);

    [DllImport("libc", EntryPoint = "umask")]
    private static extern int SetUmask(int mask);
}
