using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Snail.MCP.FileSystem.Adapters;

/// <summary>Files and folders reached with no link followed anywhere on their path, and changed relative to a folder held open.</summary>
/// <remarks>
/// A path comes from <c>SessionRoots.Reach</c> with every link on it resolved, so a link on it now means one was swapped in
/// after the check. Opened by name, the write followed it: a folder turned into a link out of the copy between the check
/// and the write put the file outside the session within three to six writes. Here the folder is opened by one call that
/// refuses a link anywhere on its path — <c>O_NOFOLLOW_ANY</c> on macOS, <c>openat2</c> with <c>RESOLVE_NO_SYMLINKS</c> on
/// Linux — and every file is created, renamed and opened relative to that open folder, so what was checked is what is
/// changed, whatever the path says by then. A link swapped in is a refusal, never a write somewhere else.
/// <para>
/// Where Linux has no <c>openat2</c> — a kernel before 5.6, gVisor, a seccomp profile that does not know it, an amd64
/// image run under Rosetta or QEMU — the path is opened one folder at a time from <c>/</c>, each inside the one before
/// it and none of them a link, which refuses the same paths. Without it every read and write of such a system failed.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("windows")]
internal static partial class LinklessDisk
{
    private const int WriteOnly = 0x1;
    private const int AlreadyThere = 17;
    private const int NotPermitted = 1;
    private const int NoEntry = 2;
    private const int Denied = 13;
    private const int NoSuchCall = 38;
    private const int NotAFolder = 20;
    private const byte UnknownType = 0;
    private const byte FolderType = 4;
    private const byte FileType = 8;
    private const byte LinkType = 10;
    private const int TypeBits = 0xF000;
    private const int FolderMode = 0x4000;
    private const int LinkMode = 0xA000;
    private const int Everyone = 0x1FF;
    private const int Readable = 0x1B6;
    private const long OpenAt2 = 437;
    private const int WorkingFolder = -100;
    private const ulong NoSymlinks = 0x04;

    /// <summary>The flags whose values differ between the systems and, on Linux, between the processors.</summary>
    private static readonly Flags Machine = Flags.OfThisMachine();

    /// <summary>Where this machine's C library keeps what a folder entry and a file's status hold.</summary>
    private static readonly Layout Shape = Layout.OfThisMachine();

    /// <summary>The mode a new file takes, as <c>open</c> would give it under the process's umask.</summary>
    /// <remarks>Set explicitly after creating: on Apple silicon the mode of <c>openat</c>, a variadic argument, does not reach the call.</remarks>
    private static readonly int Fresh = Readable & ~Umask();

    /// <summary>Whether paths are opened one folder at a time, as on a Linux that does not answer <c>openat2</c>.</summary>
    /// <remarks>Set the first time <c>openat2</c> is refused as unknown, and by a test to walk that way on any system.</remarks>
    internal static bool WalksFolderByFolder
    {
        get => Volatile.Read(ref _walksFolderByFolder);
        set => Volatile.Write(ref _walksFolderByFolder, value);
    }

    private static bool _walksFolderByFolder;

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
            Exception failure = Failure(path);
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
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handle is owned by the stream returned, and disposed here when the stream cannot be made.")]
    public static FileStream Reading(string path)
    {
        SafeFileHandle handle = Opened(Linkless(path, Machine.CloseOnExec), path);

        try
        {
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();

            throw;
        }
    }

    /// <summary>What a folder holds, read from the folder opened with no link on its path, links left out.</summary>
    /// <remarks>
    /// Enumerated by name, a folder swapped for a link out of the copy between the check and the enumeration named the
    /// files outside the session: <c>list_directory</c> within 13 listings, <c>find_files</c> within two thousand. Read from
    /// the open folder, the names are those of the folder that was checked, or the folder is refused.
    /// </remarks>
    public static unsafe Contents Contents(string folder)
    {
        int descriptor = Linkless(folder, Machine.Folder | Machine.CloseOnExec);

        if (descriptor < 0)
        {
            throw Failure(folder);
        }

        nint listing = Shape.IsInode64 ? OpenListing64(descriptor) : OpenListing(descriptor);

        if (listing == 0)
        {
            Exception failure = Failure(folder);
            _ = Close(descriptor);

            throw failure;
        }

        var files = new List<string>();
        var folders = new List<string>();

        try
        {
            while (true)
            {
                nint entry = Shape.IsInode64 ? NextEntry64(listing) : NextEntry(listing);

                if (entry == 0)
                {
                    break;
                }

                ReadOnlySpan<byte> name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)(entry + Shape.NameAt));

                if (name is [(byte)'.'] or [(byte)'.', (byte)'.'])
                {
                    continue;
                }

                string named = Encoding.UTF8.GetString(name);
                byte type = *(byte*)(entry + Shape.TypeAt);

                if (type == UnknownType)
                {
                    type = TypeOf(descriptor, named);
                }

                if (type == LinkType || type == UnknownType)
                {
                    continue;
                }

                (type == FolderType ? folders : files).Add(Path.Join(folder, named));
            }

            if (Marshal.GetLastPInvokeError() != 0)
            {
                throw Failure(folder);
            }
        }
        finally
        {
            _ = CloseListing(listing);
        }

        return new Contents([.. files], [.. folders]);
    }

    /// <summary>The length of a file, read from the folder above it opened with no link on its path.</summary>
    /// <remarks>Read by name, the size of a file outside the session reached the answer through a folder swapped for a link.</remarks>
    public static long Length(string path)
    {
        using SafeFileHandle folder = Folder(Path.GetDirectoryName(path)!);
        bool isAdded = false;

        try
        {
            folder.DangerousAddRef(ref isAdded);

            Status status = Stated((int)folder.DangerousGetHandle(), Path.GetFileName(path)) ?? throw Failure(path);

            return (status.Mode & TypeBits) == FolderMode
                ? throw new FileNotFoundException($"'{path}' is a folder, not a file", path)
                : status.Size;
        }
        finally
        {
            if (isAdded)
            {
                folder.DangerousRelease();
            }
        }
    }

    /// <summary>The type of an entry the folder did not name, from its status; unknown when it is gone.</summary>
    private static byte TypeOf(int folder, string name) =>
        Stated(folder, name) switch
        {
            null => UnknownType,
            { Mode: var mode } when (mode & TypeBits) == FolderMode => FolderType,
            { Mode: var mode } when (mode & TypeBits) == LinkMode => LinkType,
            _ => FileType
        };

    /// <summary>The status of a name inside an open folder, the name itself when it is a link, or null with the error left set.</summary>
    private static unsafe Status? Stated(int folder, string name)
    {
        byte* status = stackalloc byte[256];
        int result = Shape.StatCall != 0
            ? (int)StatSyscall(Shape.StatCall, folder, name, status, Shape.NoFollow)
            : Shape.IsInode64 ? StatAt64(folder, name, status, Shape.NoFollow) : StatAt(folder, name, status, Shape.NoFollow);

        if (result != 0)
        {
            return null;
        }

        int mode = Shape.IsModeShort ? *(ushort*)(status + Shape.ModeAt) : *(int*)(status + Shape.ModeAt);

        return new Status(mode, *(long*)(status + Shape.SizeAt));
    }

    /// <summary>A folder opened by one call that refuses a link anywhere on its path.</summary>
    private static SafeFileHandle Folder(string path) => Opened(Linkless(path, Machine.Folder | Machine.CloseOnExec), path);

    /// <summary>A descriptor opened by the one call this system has that refuses a link anywhere on the path, or a negative number.</summary>
    private static int Linkless(string path, int flags)
    {
        if (WalksFolderByFolder)
        {
            return FolderByFolder(path, flags);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Open(path, flags | Machine.NoFollowAnywhere);
        }

        int file = OpenWithoutLinks(path, flags);

        if (file >= 0 || Marshal.GetLastPInvokeError() is not (NoSuchCall or NotPermitted))
        {
            return file;
        }

        WalksFolderByFolder = true;

        return FolderByFolder(path, flags);
    }

    /// <summary>The path opened one name at a time from <c>/</c>, each inside the folder opened before it and none of them followed as a link.</summary>
    private static int FolderByFolder(string path, int flags)
    {
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (!path.StartsWith('/') || Array.Exists(names, name => name is "." or ".."))
        {
            throw new IOException($"'{path}' is not a full path with every link resolved");
        }

        int folder = Open("/", Machine.Folder | Machine.CloseOnExec);

        for (int at = 0; at < names.Length && folder >= 0; at++)
        {
            int wanted = at == names.Length - 1 ? flags : Machine.Folder | Machine.CloseOnExec;
            int next = OpenAt(folder, names[at], wanted | Machine.NoFollow, 0);
            int error = Marshal.GetLastPInvokeError();

            _ = Close(folder);
            Marshal.SetLastPInvokeError(error);
            folder = next;
        }

        return folder;
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = UnlinkAt(folder, beside, 0);

            throw;
        }
    }

    private static SafeFileHandle Opened(int descriptor, string path) =>
        descriptor < 0 ? throw Failure(path) : new SafeFileHandle(descriptor, ownsHandle: true);

    /// <summary>The error of the last call, as the exception .NET throws for it: a file that is not there, a permission, or any other.</summary>
    private static Exception Failure(string path)
    {
        int error = Marshal.GetLastPInvokeError();
        string message = $"'{path}' cannot be reached without following a link: {Marshal.GetPInvokeErrorMessage(error)}";

        return error switch
        {
            NoEntry => new FileNotFoundException(message, path),
            NotAFolder => new DirectoryNotFoundException(message),
            Denied or NotPermitted => new UnauthorizedAccessException(message),
            _ => new IOException(message)
        };
    }

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

    /// <summary>The mode and length of a file, as <c>stat</c> gives them.</summary>
    private readonly record struct Status(int Mode, long Size);

    /// <summary>The places in this machine's <c>struct dirent</c> and <c>struct stat</c> the server reads, and how it asks for a status.</summary>
    /// <param name="NameAt">Where the name of an entry starts.</param>
    /// <param name="TypeAt">Where the type of an entry is: <c>d_type</c>.</param>
    /// <param name="ModeAt">Where the mode of a status is: <c>st_mode</c>.</param>
    /// <param name="IsModeShort">Whether the mode is two bytes, as on macOS, rather than four.</param>
    /// <param name="SizeAt">Where the length of a status is: <c>st_size</c>.</param>
    /// <param name="NoFollow"><c>AT_SYMLINK_NOFOLLOW</c>.</param>
    /// <param name="StatCall">The number of <c>newfstatat</c> on Linux, called directly so no glibc version is needed; zero on macOS.</param>
    /// <param name="IsInode64">Whether the C library names its calls with <c>$INODE64</c>, as macOS does on Intel.</param>
    private readonly record struct Layout(int NameAt, int TypeAt, int ModeAt, bool IsModeShort, int SizeAt, int NoFollow, long StatCall, bool IsInode64)
    {
        public static Layout OfThisMachine()
        {
            bool isArm = RuntimeInformation.ProcessArchitecture is Architecture.Arm64;

            if (OperatingSystem.IsMacOS())
            {
                return new Layout(21, 20, 4, true, 96, 0x20, 0, !isArm);
            }

            return isArm
                ? new Layout(19, 18, 16, false, 48, 0x100, 79, false)
                : new Layout(19, 18, 24, false, 48, 0x100, 262, false);
        }
    }

    [LibraryImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
    private static partial nint OpenListing(int folder);

    [LibraryImport("libc", EntryPoint = "fdopendir$INODE64", SetLastError = true)]
    private static partial nint OpenListing64(int folder);

    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static partial nint NextEntry(nint listing);

    [LibraryImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)]
    private static partial nint NextEntry64(nint listing);

    [LibraryImport("libc", EntryPoint = "closedir", SetLastError = true)]
    private static partial int CloseListing(nint listing);

    [LibraryImport("libc", EntryPoint = "fstatat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int StatAt(int folder, string name, byte* status, int flags);

    [LibraryImport("libc", EntryPoint = "fstatat$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int StatAt64(int folder, string name, byte* status, int flags);

    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial long StatSyscall(long number, int folder, string name, byte* status, int flags);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle folder, string name, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(int folder, string name, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial long Syscall(long number, int folder, string path, ref OpenHow how, nuint size);

    [LibraryImport("libc", EntryPoint = "renameat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameAt(SafeFileHandle fromFolder, string from, SafeFileHandle toFolder, string to);

    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnlinkAt(SafeFileHandle folder, string name, int flags);

    [LibraryImport("libc", EntryPoint = "mkdirat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MakeAt(SafeFileHandle folder, string name, int mode);

    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int Chmod(SafeFileHandle file, int mode);

    [LibraryImport("libc", EntryPoint = "umask")]
    private static partial int SetUmask(int mask);
}
