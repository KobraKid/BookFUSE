﻿using System.Runtime.Versioning;
using FileInfo = Fsp.Interop.FileInfo;

namespace BookFUSE
{
    /// <summary>
    /// Represents either a series (directory) or a book (file) in the file system.
    /// </summary>
    internal class LibraryFileDesc
    {
        private readonly string _Path;

        /// <summary>
        /// The file system root directory.
        /// </summary>
        public DirectoryInfo? Root;

        public CalibreLibrary.Library? Library;

        /// <summary>
        /// The series represented by this descriptor, if applicable.
        /// </summary>
        public CalibreLibrary.Series? Series;

        /// <summary>
        /// The book represented by this descriptor, if applicable.
        /// </summary>
        public CalibreLibrary.Book? Book;

        /// <summary>
        /// The file stream for the book, if applicable.
        /// </summary>
        public FileStream? Stream;

        /// <summary>
        /// Create a library file descriptor for the file system root.
        /// </summary>
        /// <param name="path">The root file path.</param>
        public LibraryFileDesc(string path) { _Path = path; Root = new(path); }

        /// <summary>
        /// Create a library file descriptor for a library.
        /// </summary>
        /// <param name="path">The root file path.</param>
        /// <param name="library">The library.</param>
        public LibraryFileDesc(string path, CalibreLibrary.Library library)
            : this(path) { Library = library; }

        /// <summary>
        /// Create a library file descriptor for a series (directory).
        /// </summary>
        /// <param name="path">The root file path.</param>
        /// <param name="series">The series.</param>
        public LibraryFileDesc(string path, CalibreLibrary.Library library, CalibreLibrary.Series series)
            : this(path, library) { Series = series; }

        /// <summary>
        /// Create a library file descriptor for a book (file).
        /// </summary>
        /// <param name="path">The root file path</param>
        /// <param name="book">The book.</param>
        public LibraryFileDesc(string path, CalibreLibrary.Library library, CalibreLibrary.Series series, CalibreLibrary.Book book)
            : this(path, library, series) { Book = book; }

        /// <summary>
        /// Gets the file or directory information.
        /// </summary>
        /// <param name="fileInfo">The object receiving the result.</param>
        public void GetFileInfo(out FileInfo fileInfo)
        {
            if (Root is null)
            {
                fileInfo = default;
                return;
            }

            if (Library is null)
            {
                fileInfo = new()
                {
                    FileAttributes = (uint)Root.Attributes,
                    ReparseTag = 0,
                    FileSize = 0,
                    AllocationSize = 0,
                    CreationTime = (ulong)Root.CreationTimeUtc.ToFileTimeUtc(),
                    ChangeTime = (ulong)Root.CreationTimeUtc.ToFileTimeUtc(),
                    LastAccessTime = (ulong)Root.LastAccessTimeUtc.ToFileTimeUtc(),
                    LastWriteTime = (ulong)Root.LastWriteTimeUtc.ToFileTimeUtc(),
                    IndexNumber = 0,
                    HardLinks = 0
                };
                return;
            }

            FileAttributes attributes = Book is null ? (FileAttributes.Directory | FileAttributes.ReadOnly) : FileAttributes.ReadOnly;
            fileInfo = new()
            {
                FileAttributes = (uint)attributes,
                ReparseTag = 0,
                FileSize = (ulong)(Book is null ? 0 : Book.FileSize),
                AllocationSize = 0,
                CreationTime = (ulong)(Book is null ? DateTime.Today : Book.Created).ToFileTimeUtc(),
                ChangeTime = (ulong)(Book is null ? DateTime.Today : Book.Modified).ToFileTimeUtc(),
                LastAccessTime = (ulong)(Book is null ? DateTime.Today : Book.Modified).ToFileTimeUtc(),
                LastWriteTime = (ulong)(Book is null ? DateTime.Today : Book.Modified).ToFileTimeUtc(),
                IndexNumber = 0,
                HardLinks = 0
            };
        }

        /// <summary>
        /// Gets the security descriptor for the file or directory.
        /// </summary>
        /// <returns>A binary representation of the security descriptor.</returns>
        [SupportedOSPlatform("windows")]
        public byte[] GetSecurityDescriptor()
        {
            if (Stream != null) { return Stream.GetAccessControl()?.GetSecurityDescriptorBinaryForm() ?? []; }
            if (Root != null) { return Root.GetAccessControl()?.GetSecurityDescriptorBinaryForm() ?? []; }
            return [];
        }
    }
}
