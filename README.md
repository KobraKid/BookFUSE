# BookFUSE

BookFUSE is a FUSE-based virtual filesystem for Windows that exposes a [calibre](https://calibre-ebook.com/) ebook library as a read-only filesystem, making it accessible to applications such as [Kavita](https://www.kavitareader.com/) or other ebook readers that expect a directory structure of series and books.

> For a Docker implementation, see [BookFUSEDocker](https://github.com/KobraKid/BookFUSEDocker)

## Features

- **Read-only FUSE filesystem**: Presents your calibre library as a virtual drive.
- **Series and book mapping**: Each calibre series appears as a directory, and each book as a file within its series.
- **Automatic metadata parsing**: Reads calibre's `metadata.db` to build the virtual filesystem.
- **Windows support**: Built on [WinFsp](https://github.com/billziss-gh/winfsp) for native Windows filesystem integration.

## How it works

- The filesystem is implemented in [`BookFUSE`](BookFUSE.cs), which uses WinFsp to create a virtual drive.
- The calibre library is parsed by [`CalibreLibrary`](CalibreLibrary.cs), which loads series and book information from the calibre SQLite database.
- A library directory is created for each library located in the calibre installation directory.
- Each subdirectory represents a series, and each file represents a book in that series, with the correct file extension.
- File and directory metadata is provided by [`LibraryFileDesc`](LibraryFileDesc.cs).

## Requirements

- [calibre](https://calibre-ebook.com/)
- .NET 8.0 or later
- [WinFsp](https://github.com/billziss-gh/winfsp) (must be installed)
- [System.Data.SQLite](https://system.data.sqlite.org/index.html/doc/trunk/www/index.wiki) (NuGet package)

## Installation

1. Run the installation file.
2. During installation, enter the path to your calibre installation. This should be the folder where your library (or libraries) are located.
3. After installation has completed, open `File Explorer`.
4. Right-click on `This PC` and select `Map network drive...`.
5. Choose a drive letter to mount your library to, and in the folder field, enter `\\BookFUSE\Library`.
   > Note: you can enter `\\BookFUSE\<name>`, and `name` will be used to label your drive.

   > Note: if your calibre library is large, it may take some time to build the virtual directory.

## Building

1. Clone this repository.
2. Open the solution file [`BookFUSE.sln`](BookFUSE.sln) in Visual Studio 2022 or later.
3. Restore NuGet packages.
4. Build the solution.

### Usage

Run the built executable with the following options:

    BookFUSE.exe -p <CalibreLibraryPath> -m <MountPoint>

**Options:**

- `-p <CalibreLibraryPath>`: Path to your calibre library folder (must contain `metadata.db`).
- `-m <MountPoint>`: Mount point for the virtual filesystem (e.g., `X:` or a directory).
- `-d <DebugFlags>`: (Optional) Enable debug logging.
- `-D <DebugLogFile>`: (Optional) Path to debug log file. -1 logs to the terminal.
- `-u <UNC prefix>`: (Optional) UNC prefix for the volume.

**Example:**

    BookFUSE.exe -p "C:\Users\YourName\Calibre Library" -m "X:"

## License

This project is licensed under the MIT License. See [`LICENSE.txt`](LICENSE.txt) for details.

## Acknowledgements

- [calibre](https://calibre-ebook.com/) for the ebook library format.
- [WinFsp](https://github.com/billziss-gh/winfsp) for the FUSE-compatible filesystem library for Windows.
