using Fsp;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static BookFUSE.CalibreLibrary;
using FileInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

namespace BookFUSE
{
    /// <summary>
    /// Represents a FUSE file system which translates a calibre library to a Kavita library.
    /// </summary>
    [SupportedOSPlatform("windows")]
    class BookFUSE : FileSystemBase
    {
        protected static void ThrowIoExceptionWithHResult(int HResult)
            => throw new IOException(null, HResult);
        protected static void ThrowIoExceptionWithWin32(int error)
            => ThrowIoExceptionWithHResult(unchecked((int)(0x80070000 | error)));
        protected static void ThrowIoExceptionWithNtStatus(int status)
            => ThrowIoExceptionWithWin32((int)Win32FromNtStatus(status));

        /// <summary>
        /// Create a new BookFUSE file system instance.
        /// </summary>
        /// <param name="path">The calibre library file path.</param>
        public BookFUSE(string path)
        {
            var _path = Path.GetFullPath(path);
            if (_path.EndsWith('\\'))
            {
                _path = _path[..^1];
            }
            _Library = new(_path);
        }

        /// <summary>
        /// Handles changes to the library by responding to file system events.
        /// </summary>
        /// <remarks>This method is triggered when a file system change is detected. It ensures that the
        /// library is re-initialized after a short delay to handle potential bursts of file system events.</remarks>
        /// <param name="sender">The source of the event, typically the file system watcher.</param>
        /// <param name="e">The event data containing information about the file system change.</param>
        public void LibraryChanged(object sender, FileSystemEventArgs e)
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = new Timer(_ =>
                {
                    lock (_lock)
                    {
                        _timer?.Dispose();
                        _timer = null;
                    }
                    _Library.Init();
                }, null, 500, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Add the base library path to a file name.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>The file's absolute path.</returns>
        public string ConcatPath(string fileName)
            => _Library.Root + fileName;

        /// <inheritdoc />
        public override int ExceptionHandler(Exception ex)
        {
            int HResult = ex.HResult;
            if ((HResult & 0xFFFF0000) == 0x80070000)
            {
                return NtStatusFromWin32((uint)HResult & 0xFFFF);
            }
            return STATUS_UNEXPECTED_IO_ERROR;
        }

        /// <inheritdoc />
        public override int Init(object HostObject)
        {
            _Library.Init();
            FileSystemHost host = (FileSystemHost)HostObject;
            host.FileSystemName = PROGNAME;
            host.SectorSize = ALLOCATION_UNIT;
            host.SectorsPerAllocationUnit = 1;
            host.MaxComponentLength = 255;
            host.FileInfoTimeout = 1000;
            host.CaseSensitiveSearch = false;
            host.CasePreservedNames = true;
            host.UnicodeOnDisk = true;
            host.PersistentAcls = true;
            host.PostCleanupWhenModifiedOnly = true;
            host.PassQueryDirectoryPattern = true;
            host.FlushAndPurgeOnCleanup = true;
            host.VolumeCreationTime = (ulong)File.GetCreationTimeUtc(_Library.Root).ToFileTimeUtc();
            host.VolumeSerialNumber = 0;
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
        {
            VolumeInfo = new()
            {
                TotalSize = (ulong)_Library.VolumeSize,
                FreeSize = 0
            };
            VolumeInfo.SetVolumeLabel(PROGNAME);
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override int GetSecurityByName(string FileName, out uint FileAttributes, ref byte[] SecurityDescriptor)
        {
            FileAttributes attributes = System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Directory;
            if (FileName == "\\")
            {
                System.IO.FileInfo info = new(ConcatPath(FileName));
                attributes |= info.Attributes;
            }
            else if (_Library.GetLibrary(FileName, out Library? library))
            {
                if (library.GetSeries(FileName, out Series? series))
                {
                    if (series.GetBook(FileName, out Book? _))
                    {
                        // Remove directory attribute for books
                        attributes &= ~System.IO.FileAttributes.Directory;
                    }
                }
            }
            FileAttributes = (uint)attributes;
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override int Open(string FileName, uint CreateOptions, uint GrantedAccess,
            out object? FileNode, out object? FileDesc, out FileInfo FileInfo, out string? NormalizedName)
        {
            // Normalized name is not used
            NormalizedName = default;

            // Ignore desktop.ini files
            if (FileName.EndsWith("desktop.ini"))
            {
                FileNode = default;
                FileDesc = default;
                FileInfo = default;
                return STATUS_SUCCESS;
            }

            if (_Library.GetLibrary(FileName, out Library? library))
            {
                if (library.GetSeries(FileName, out Series? series))
                {
                    if (series.GetBook(FileName, out Book? book))
                    {
                        LibraryFileDesc libraryFile = new(_Library.Root, library, series, book);
                        libraryFile.GetFileInfo(out FileInfo);
                        FileNode = book;
                        FileDesc = libraryFile;
                    }
                    else // FileName is a series
                    {
                        LibraryFileDesc libraryFile = new(_Library.Root, library, series);
                        libraryFile.GetFileInfo(out FileInfo);
                        FileNode = series;
                        FileDesc = libraryFile;
                    }
                }
                else // FileName is a library
                {
                    LibraryFileDesc libraryFile = new(_Library.Root, library);
                    libraryFile.GetFileInfo(out FileInfo);
                    FileNode = library;
                    FileDesc = libraryFile;
                }
            }
            else // FileName is the directory root
            {
                LibraryFileDesc libraryFile = new(ConcatPath(FileName));
                libraryFile.GetFileInfo(out FileInfo);
                FileNode = default;
                FileDesc = libraryFile;
            }
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override void Close(object FileNode, object FileDesc)
        {
            if (FileDesc is null) { return; }

            ((LibraryFileDesc)FileDesc).Stream?.Dispose();
        }

        /// <inheritdoc />
        public override int Read(object FileNode, object FileDesc, nint Buffer, ulong Offset, uint Length, out uint BytesTransferred)
        {
            if (FileDesc is null)
            {
                BytesTransferred = 0;
                return STATUS_NOT_FOUND;
            }

            LibraryFileDesc libraryFile = (LibraryFileDesc)FileDesc;
            if (libraryFile.Book is null || libraryFile.Library is null)
            {
                BytesTransferred = 0;
                return STATUS_NOT_FOUND;
            }

            libraryFile.Stream ??= new(Path.Join(_Library.Root, libraryFile.Library.Name, libraryFile.Book.Path, libraryFile.Book.PhysicalName),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
            if ((ulong)libraryFile.Stream.Length <= Offset)
            {
                ThrowIoExceptionWithNtStatus(STATUS_END_OF_FILE);
            }

            byte[] bytes = new byte[Length];
            libraryFile.Stream.Seek((long)Offset, SeekOrigin.Begin);
            BytesTransferred = (uint)libraryFile.Stream.Read(bytes, 0, bytes.Length);
            Marshal.Copy(bytes, 0, Buffer, bytes.Length);
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override int GetFileInfo(object FileNode, object FileDesc, out FileInfo FileInfo)
        {
            ((LibraryFileDesc)FileDesc).GetFileInfo(out FileInfo);
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override int GetSecurity(object FileNode, object FileDesc, ref byte[] SecurityDescriptor)
        {
            SecurityDescriptor = ((LibraryFileDesc)FileDesc).GetSecurityDescriptor();
            return STATUS_SUCCESS;
        }

        /// <inheritdoc />
        public override bool ReadDirectoryEntry(object FileNode, object FileDesc, string Pattern, string Marker, ref object Context, out string? FileName, out FileInfo FileInfo)
        {
            LibraryFileDesc libraryFile = (LibraryFileDesc)FileDesc;
            Pattern = Pattern?.Replace('<', '*').Replace('>', '?').Replace('"', '.') ?? string.Empty;
            int index = 0;
            // Parse the root directory
            if (libraryFile.Root != null)
            {
                // Parse a library folder
                if (libraryFile.Library != null)
                {
                    // Parse a series folder
                    if (libraryFile.Series != null)
                    {
                        var books = libraryFile.Series.Books.Where(book => book.VirtualName.Contains(Pattern, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (Context is null)
                        {
                            if (Marker != null)
                            {
                                index = Array.BinarySearch([.. books.Select(b => b.VirtualName)], Marker);
                                if (index >= 0) { index++; }
                                else { index = ~index; }
                            }
                        }
                        else
                        {
                            index = (int)Context;
                        }
                        if (index < books.Length)
                        {
                            Context = index + 1;
                            FileName = books[index].VirtualName;
                            new LibraryFileDesc(_Library.Root, libraryFile.Library, libraryFile.Series, books[index]).GetFileInfo(out FileInfo);
                            return true;
                        }
                        else
                        {
                            FileName = default;
                            FileInfo = default;
                            return false;
                        }
                    }
                    var series = libraryFile.Library.SeriesList.Where(series => series.Name.Contains(Pattern, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (Context is null)
                    {
                        if (Marker != null)
                        {
                            index = Array.BinarySearch([.. series.Select(s => s.Name)], Marker);
                            if (index >= 0) { index++; }
                            else { index = ~index; }
                        }
                    }
                    else
                    {
                        index = (int)Context;
                    }
                    if (index < series.Length)
                    {
                        Context = index + 1;
                        FileName = series[index].Name;
                        new LibraryFileDesc(_Library.Root, libraryFile.Library, series[index]).GetFileInfo(out FileInfo);
                        return true;
                    }
                    else
                    {
                        FileName = default;
                        FileInfo = default;
                        return false;
                    }
                }
                var libraries = _Library.Libraries.Where(lib => lib.Name.Contains(Pattern, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (Context is null)
                {
                    if (Marker != null)
                    {
                        index = Array.BinarySearch([.. libraries.Select(l => l.Name)], Marker);
                        if (index >= 0) { index++; }
                        else { index = ~index; }
                    }
                }
                else
                {
                    index = (int)Context;
                }
                if (index < libraries.Length)
                {
                    Context = index + 1;
                    FileName = libraries[index].Name;
                    new LibraryFileDesc(_Library.Root, libraries[index]).GetFileInfo(out FileInfo);
                    return true;
                }
                else
                {
                    FileName = default;
                    FileInfo = default;
                    return false;
                }
            }
            throw new DirectoryNotFoundException(libraryFile.ToString());
        }

        /// <summary>
        /// The name of the file system.
        /// </summary>
        private readonly string PROGNAME = "BookFUSE";

        /// <summary>
        /// The calibre library instance.
        /// </summary>
        private readonly CalibreLibrary _Library;

        /// <summary>
        /// The size of the allocation unit for the file system.
        /// </summary>
        protected const int ALLOCATION_UNIT = 4096;

        /// <summary>
        /// A timer used to delay the re-initialization of the library after file system changes.
        /// </summary>
        private Timer? _timer;

        /// <summary>
        /// A lock object to synchronize access to the timer and library re-initialization.
        /// </summary>
        private readonly object _lock = new();
    }

    /// <summary>
    /// This class implements a Windows service that mounts the BookFUSE file system.
    /// </summary>
    class BookFUSEService : Service
    {
        /// <summary>
        /// Represents an exception that occurs when there is an error in the command-line usage.
        /// </summary>
        /// <remarks>This exception is typically thrown to indicate that the command-line arguments
        /// provided by the user are invalid or do not conform to the expected format.</remarks>
        /// <param name="message">The message to include in the exception</param>
        private class CommandLineUsageException(string? message = null) : Exception(message)
        {
            public bool HasMessage = message != null;
        }

        /// <summary>
        /// The program name for logging purposes.
        /// </summary>
        private const string PROGNAME = "BookFUSE";

        /// <summary>
        /// Construct a new BookFUSE service instance.
        /// </summary>
        public BookFUSEService() : base(nameof(BookFUSEService)) { }

        /// <summary>
        /// Handles the startup logic for the application, including parsing command-line arguments and mounting the
        /// file system.
        /// </summary>
        /// <remarks>This method is invoked during the application's startup sequence. It processes the
        /// provided command-line arguments to configure the file system host and mount the file system. The method
        /// expects specific options to be passed via the <paramref name="Args"/> parameter, and throws exceptions if
        /// the arguments are invalid or required options are missing. Supported options include: <list type="bullet">
        /// <item><description><c>-d</c>: Specifies debug flags.</description></item> <item><description><c>-D</c>:
        /// Specifies the path to the debug log file.</description></item> <item><description><c>-u</c>: Specifies the
        /// UNC prefix for the volume.</description></item> <item><description><c>-p</c>: Specifies the path to the
        /// directory to expose.</description></item> <item><description><c>-m</c>: Specifies the mount point for the
        /// file system.</description></item> </list> If the required options are not provided or invalid values are
        /// supplied, a <see cref="CommandLineUsageException"/> is thrown.</remarks>
        /// <param name="Args">An array of command-line arguments passed to the application.</param>
        [SupportedOSPlatform("windows")]
        protected override void OnStart(string[] Args)
        {
            try
            {
                /* Parse command-line args */
                string? debugLogFile = null;
                uint debugFlags = 0;
                string? volumePrefix = null;
                string? path = null;
                string? mountPoint = null;
                FileSystemHost? host = null;
                BookFUSE? bookFUSE = null;
                int i;
                for (i = 1; i < Args.Length; i++)
                {
                    string arg = Args[i];
                    if (arg[0] != '-') { continue; }
                    switch (arg[1])
                    {
                        case '?':
                            throw new CommandLineUsageException();
                        case 'd':
                            ArgToInt(Args, ref i, ref debugFlags);
                            break;
                        case 'D':
                            ArgToString(Args, ref i, ref debugLogFile);
                            break;
                        case 'm':
                            ArgToString(Args, ref i, ref mountPoint);
                            break;
                        case 'p':
                            ArgToString(Args, ref i, ref path);
                            break;
                        case 'u':
                            ArgToString(Args, ref i, ref volumePrefix);
                            break;
                        default:
                            throw new CommandLineUsageException("Unknown option: " + arg);
                    }
                }
                if (Args.Length > i)
                {
                    throw new CommandLineUsageException("Invalid option provided");
                }
                if (path is null && volumePrefix != null)
                {
                    i = volumePrefix.IndexOf('\\');
                    if (i != -1 && i < volumePrefix.Length && volumePrefix[i + 1] != '\\')
                    {
                        i = volumePrefix.IndexOf('\\', i + 1);
                        if (i != -1 && i + 1 < volumePrefix.Length && (
                            ('A' <= volumePrefix[i + 1] && volumePrefix[i + 1] <= 'Z') ||
                            ('a' <= volumePrefix[i + 1] && volumePrefix[i + 1] <= 'z')) &&
                            volumePrefix[i + 2] == '$')
                        {
                            path = string.Format("{0}:{1}", volumePrefix[i + 1], volumePrefix[(i + 3)..]);
                        }
                    }
                }
                if (path is null || mountPoint is null)
                {
                    throw new CommandLineUsageException(path is null ?
                        "Either [p]ath or [u]nc prefix must be specified" : "[m]ount point must be specified");
                }
                if (debugLogFile != null)
                {
                    if (FileSystemHost.SetDebugLogFile(debugLogFile) > 0)
                    {
                        throw new CommandLineUsageException("Cannot open debug log file");
                    }
                }

                /* Mount the file system */
                bookFUSE = new(path);
                host = new(bookFUSE) { Prefix = volumePrefix };
                if (host.Mount(mountPoint, null, true, debugFlags) > 0)
                {
                    throw new CommandLineUsageException("Cannot mount file system");
                }
                mountPoint = host.MountPoint();
                _Host = host;
                Log(EVENTLOG_INFORMATION_TYPE, string.Format("{0}{1}{2} -p {3} -m {4}",
                    PROGNAME,
                    (volumePrefix?.Length ?? 0) > 0 ? " -u " : "",
                    volumePrefix ?? "",
                    path,
                    mountPoint));

                /* Set up file system watcher */
                _Watcher = new(path)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    Filter = "metadata.db",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _Watcher.Changed += bookFUSE.LibraryChanged;
            }
            catch (CommandLineUsageException ex)
            {
                Log(EVENTLOG_ERROR_TYPE, string.Format(
                    "{0}" +
                    "usage: {1} OPTIONS\n" +
                    "\n" +
                    "options:\n" +
                    "    -d DebugFlags       [-1: enable all debug logs]\n" +
                    "    -D DebugLogFile     [file path; use - for stderr]\n" +
                    "    -u \\Server\\Share    [UNC prefix (single backslash)]\n" +
                    "    -p Path             [path to calibre directory]\n" +
                    "    -m MountPoint       [X:|*|directory]\n",
                    ex.HasMessage ? ex.Message + "\n" : "",
                    PROGNAME));
                throw;
            }
            catch (Exception ex)
            {
                Log(EVENTLOG_ERROR_TYPE, string.Format("{0}", ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Executes cleanup logic when the service is stopped.
        /// </summary>
        /// <remarks>This method is called by the service framework when the service is being stopped.  It
        /// ensures that any resources associated with the service, such as the host, are properly released.</remarks>
        protected override void OnStop()
        {
            _Host?.Unmount();
            _Host = null;
        }

        /// <summary>
        /// Convert an argument to a string
        /// </summary>
        /// <param name="Args">The args array</param>
        /// <param name="I">The index into the args array</param>
        /// <param name="V">The value of the arg</param>
        /// <exception cref="CommandLineUsageException"></exception>
        private static void ArgToString(string[] Args, ref int I, ref string? V)
        {
            if (Args.Length > ++I)
                V = Args[I];
            else
                throw new CommandLineUsageException();
        }
        /// <summary>
        /// Convert an argument to an int
        /// </summary>
        /// <param name="Args">The args array</param>
        /// <param name="I">The index into the args array</param>
        /// <param name="V">The value of the arg</param>
        /// <exception cref="CommandLineUsageException"></exception>
        private static void ArgToInt(string[] Args, ref int I, ref uint V)
        {
            if (Args.Length > ++I)
                V = int.TryParse(Args[I], out int R) ? (uint)R : V;
            else
                throw new CommandLineUsageException();
        }

        /// <summary>
        /// The file system host instance used to mount the BookFUSE file system.
        /// </summary>
        private FileSystemHost? _Host;

        /// <summary>
        /// The file system watcher instance that monitors changes in the calibre library directory.
        /// </summary>
        private FileSystemWatcher? _Watcher;
    }

    /// <summary>
    /// Represents the entry point of the application.
    /// </summary>
    /// <remarks>This class initializes and executes the main application logic by invoking the <see
    /// cref="BookFUSEService"/>. The application's exit code is set based on the result of the service's
    /// execution.</remarks>
    internal class Program
    {
        /// <summary>
        /// The entry point of the application.
        /// </summary>
        /// <remarks>Initializes and runs the BookFUSE service, setting the application's exit code based
        /// on the service's execution result.</remarks>
        static void Main()
        {
            Environment.ExitCode = new BookFUSEService().Run();
        }
    }
}
