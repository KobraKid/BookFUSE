using System.Data.SQLite;

namespace BookFUSE
{
    public sealed class CalibreLibrary(string path)
    {
        private readonly string _Path = path;

        private readonly SQLiteConnection sqlite = new($"Data Source={path}\\metadata.db;New=False;");

        /// <summary>
        /// Series list indexed by series ID.
        /// </summary>
        public Dictionary<int, Series> SeriesList { get => _SeriesList; }

        private readonly Dictionary<int, Series> _SeriesList = [];

        /// <summary>
        /// Series sorted by name for quick access.
        /// </summary>
        public KeyValuePair<int, Series>[] SortedSeries { get => _SortedSeries; }

        private KeyValuePair<int, Series>[] _SortedSeries = [];

        /// <summary>
        /// Initialize the Calibre library by loading series information from the database.
        /// </summary>
        public void Init()
        {
            SeriesList.Clear();
            _SortedSeries = [];
            LoadSeriesInfo();
        }

        /// <summary>
        /// Load the series information from the Calibre database.
        /// </summary>
        public void LoadSeriesInfo()
        {
            sqlite.Open();
            var command = sqlite.CreateCommand();
            command.CommandText =
                @"
                    SELECT 
                        books.title,
                        books.author_sort,
                        books.series_index,
                        books.path,
                        books.timestamp,
                        books.last_modified,
                        data.name,
                        data.format,
                        series.name,
                        series.id
                    FROM books
                    INNER JOIN books_series_link ON books.id = books_series_link.book
                    INNER JOIN data              ON books.id = data.book
                    INNER JOIN series            ON books_series_link.series = series.id
                ";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int col = 0;
                    string title = reader.GetString(col++);
                    string author = reader.GetString(col++);
                    double seriesIndex = reader.IsDBNull(col) ? 0 : reader.GetDouble(col++);
                    string path = reader.GetString(col++).Replace('/', '\\');
                    DateTime timestamp = reader.GetDateTime(col++);
                    DateTime lastModified = reader.GetDateTime(col++);
                    string fileName = reader.GetString(col++);
                    string format = FormatToFileExtension(reader.GetString(col++));
                    string seriesName = reader.GetString(col++);
                    int seriesId = reader.GetInt32(col++);
                    if (!SeriesList.TryGetValue(seriesId, out Series? value))
                    {
                        value = new(seriesName, []);
                        SeriesList[seriesId] = value;
                    }
                    if (!value.Books.Any(book => (book.Title == title) && (book.Format == format)))
                    {
                        value.Books.Add(new Book(
                            title, author, seriesIndex,
                            fileName, format, path,
                            timestamp, lastModified));
                    }
                }
            }
            sqlite.Close();
            _SortedSeries = [.. SeriesList.ToArray().OrderBy(kvp => kvp.Value.Name)];
        }

        /// <summary>
        /// Transform the format string to a file extension.
        /// </summary>
        /// <param name="format">The Calibre format string.</param>
        /// <returns>A file extension string.</returns>
        /// <exception cref="NotSupportedException">The format string doesn't have a matching file extension.</exception>
        private static string FormatToFileExtension(string format)
        {
            return format switch
            {
                "AZW3" => "azw3",
                "CBZ" => "cbz",
                "EPUB" => "epub",
                "ORIGINAL_EPUB" => "epub",
                "MOBI" => "mobi",
                "PDF" => "pdf",
                "ZIP" => "zip",
                _ => throw new NotSupportedException($"Unsupported format: {format}"),
            };
        }

        /// <summary>
        /// Check if the given file name is a series.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>Whether a series was found for the given filename.</returns>
        public bool IsSeries(string fileName)
        {
            if (!fileName.Contains('\\')) return false;
            string seriesName = fileName[(fileName.IndexOf('\\') + 1)..];
            return SeriesList.Values.Any(series => series.Name == seriesName);
        }

        /// <summary>
        /// Get the series for the given file name.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>The corresponding series.</returns>
        /// <exception cref="KeyNotFoundException">No series was found for the given filename.</exception>
        public Series GetSeries(string fileName)
        {
            string seriesName = fileName[(fileName.IndexOf('\\') + 1)..];
            return SeriesList.Values.FirstOrDefault(series => series.Name == seriesName)
                   ?? throw new KeyNotFoundException($"Series not found for file: {fileName}");
        }

        /// <summary>
        /// Check if the given file name is a book in a series.
        /// </summary>
        /// <remarks>The file name must start with \ and consist of a folder,
        /// another \ character, and finally a file name with extension.</remarks>
        /// <param name="fileName">The file name.</param>
        /// <returns>Whether a book was found for the given filename.</returns>
        public bool IsBook(string fileName)
        {
            if (!fileName.Contains('\\')) return false;
            string seriesName = fileName[..fileName.LastIndexOf('\\')];
            if (string.IsNullOrEmpty(seriesName)) return false;
            if (!IsSeries(seriesName)) return false;
            string bookName = fileName[(fileName.LastIndexOf('\\') + 1)..fileName.LastIndexOf('.')];
            return GetSeries(seriesName).Books.Any(book => book.Title == bookName);
        }

        /// <summary>
        /// Get the book for the given file name.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>The corresponding book.</returns>
        /// <exception cref="KeyNotFoundException">No book was found for the given filename.</exception>
        public Book GetBook(string fileName)
        {
            string seriesName = fileName[..fileName.LastIndexOf('\\')];
            string bookName = fileName[(fileName.LastIndexOf('\\') + 1)..fileName.LastIndexOf('.')];
            return GetSeries(seriesName).Books.FirstOrDefault(b => b.Title == bookName)
                   ?? throw new KeyNotFoundException($"Book not found for file: {fileName}");
        }

        /// <summary>
        /// Represents a series in the Calibre library.
        /// </summary>
        /// <param name="Name">The series name.</param>
        /// <param name="Books">The list of books in the series.</param>
        public sealed record Series(string Name, List<Book> Books);

        /// <summary>
        /// Represents a book in the Calibre library.
        /// </summary>
        /// <param name="Title">The book's title.</param>
        /// <param name="Author">The book's author.</param>
        /// <param name="SeriesIndex">The book's order in the series.</param>
        /// <param name="FileName">The file name.</param>
        /// <param name="Format">The file format.</param>
        /// <param name="Path">The path of the folder containing the book.</param>
        /// <param name="Created">The file creation date.</param>
        /// <param name="Modified">The file modified date.</param>
        public sealed record Book(
            string Title, string Author, double SeriesIndex,
            string FileName, string Format, string Path,
            DateTime Created, DateTime Modified)
        {
            public string FileNameWithExtension => $"{FileName}.{Format}";
            public string TitleWithExtension => $"{Title}.{Format}";
        };
    }
}
