using System.Text;

namespace JmServer.GameIntegration;

internal sealed class D2TsvTable
{
    private readonly Dictionary<string, int> _columnIndexes;

    private D2TsvTable(string[] columns, List<string[]> rows)
    {
        Columns = columns;
        Rows = rows;
        _columnIndexes = columns
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
    }

    public string[] Columns { get; }

    public List<string[]> Rows { get; }

    public static D2TsvTable Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new InvalidDataException($"D2 table '{path}' is empty.");
        }

        var columns = Split(lines[0]);
        if (columns.Length == 0 || columns.Any(string.IsNullOrEmpty))
        {
            throw new InvalidDataException($"D2 table '{path}' has an invalid header.");
        }

        var rows = new List<string[]>(lines.Length - 1);
        for (var lineNumber = 1; lineNumber < lines.Length; lineNumber++)
        {
            if (lines[lineNumber].Length == 0)
            {
                continue;
            }

            var cells = Split(lines[lineNumber]);
            if (cells.Length > columns.Length)
            {
                throw new InvalidDataException(
                    $"D2 table '{path}' row {lineNumber + 1} has {cells.Length} cells; " +
                    $"the header has {columns.Length}.");
            }

            if (cells.Length != columns.Length)
            {
                Array.Resize(ref cells, columns.Length);
                for (var index = 0; index < cells.Length; index++)
                {
                    cells[index] ??= string.Empty;
                }
            }

            rows.Add(cells);
        }

        return new D2TsvTable(columns, rows);
    }

    public string Get(string[] row, string column) => row[GetColumnIndex(column)];

    public void Set(string[] row, string column, string? value) =>
        row[GetColumnIndex(column)] = value ?? string.Empty;

    public string[] CloneRow(string[] row) => (string[])row.Clone();

    public string[] NewRow() => Enumerable.Repeat(string.Empty, Columns.Length).ToArray();

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync(string.Join('\t', Columns).AsMemory(), cancellationToken);
        foreach (var row in Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join('\t', row).AsMemory(), cancellationToken);
        }
    }

    private int GetColumnIndex(string column) =>
        _columnIndexes.TryGetValue(column, out var index)
            ? index
            : throw new InvalidDataException($"Required D2 table column '{column}' is missing.");

    private static string[] Split(string line) => line.TrimEnd('\r').Split('\t');
}
