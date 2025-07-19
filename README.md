# BookFUSE

BookFUSE is a FUSE-based virtual filesystem for Windows that exposes a [calibre](https://calibre-ebook.com/) ebook library as a read-only filesystem, making it accessible to applications such as [Kavita](https://www.kavitareader.com/) or other ebook readers that expect a directory structure of series and books.

## Features

- **Read-only FUSE filesystem**: Presents your calibre library as a virtual drive.
- **Series and book mapping**: Each calibre series appears as a directory, and each book as a file within its series.
- **Automatic metadata parsing**: Reads calibre's `metadata.db` to build the virtual filesystem.
- **Windows support**: Built on [WinFsp](https://github.com/billziss-gh/winfsp) for native Windows filesystem integration.

## How it works

- The filesystem is implemented in [`BookFUSE`](BookFUSE.cs), which uses WinFsp to create a virtual drive.
- The calibre library is parsed by [`CalibreLibrary`](CalibreLibrary.cs), which loads series and book information from the calibre SQLite database.
- Each directory in the virtual filesystem represents a series, and each file represents a book in that series, with the correct file extension.
- File and directory metadata is provided by [`LibraryFileDesc`](LibraryFileDesc.cs).

## Requirements

- .NET 8.0 or later
- [WinFsp](https://github.com/billziss-gh/winfsp) (must be installed)
- [System.Data.SQLite](https://system.data.sqlite.org/index.html/doc/trunk/www/index.wiki) (NuGet package)

## Building

1. Clone this repository.
2. Open the solution file [`BookFUSE.sln`](BookFUSE.sln) in Visual Studio 2022 or later.
3. Restore NuGet packages.
4. Build the solution.

## Usage

Run the built executable with the following options:

    BookFUSE.exe -p <CalibreLibraryPath> -m <MountPoint>

**Options:**

- `-p <CalibreLibraryPath>`: Path to your calibre library folder (must contain `metadata.db`).
- `-m <MountPoint>`: Mount point for the virtual filesystem (e.g., `X:` or a directory).
- `-d <DebugFlags>`: (Optional) Enable debug logging.
- `-D <DebugLogFile>`: (Optional) Path to debug log file.
- `-u <UNC prefix>`: (Optional) UNC prefix for the volume.

**Example:**

    BookFUSE.exe -p "C:\Users\YourName\Calibre Library" -m "X:"

## License

This project is licensed under the MIT License. See [`LICENSE.txt`](LICENSE.txt) for details.

## Acknowledgements

- [calibre](https://calibre-ebook.com/) for the ebook library format.
- [WinFsp](https://github.com/billziss-gh/winfsp) for the FUSE-compatible filesystem library for Windows.
