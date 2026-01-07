using Microsoft.Extensions.Logging;

namespace Tools.ExternalDevServices.Utils;

public class DiagnosticsHelper : IDisposable
{
    public static bool ConsoleEnabled { get; set; }
    
    private enum Level
    {
        Info,
        Error
    }

    private readonly string _diagnosticsTypeName;
    private readonly string _diagnosticsDirectory;
    private readonly ILogger? _logger;

    private readonly List<(DateTime Time, Level Level, string Message)> _diagnostics = new();
    private readonly Lock _lock = new();

    public DiagnosticsHelper? Parent { get; set; }
    private string DiagnosticsDirectory => Parent?._diagnosticsDirectory ?? _diagnosticsDirectory;

    public DiagnosticsHelper(Type diagnosticsType, ILogger? logger = null, string diagnosticsDirectoryName = "")
        : this(diagnosticsType.Name, logger, diagnosticsDirectoryName)
    {
    }

    public DiagnosticsHelper(string diagnosticsTypeName, ILogger? logger = null, string diagnosticsDirectoryName = "")
    {
        _diagnosticsDirectory = Path.Combine("Diagnostics",
            string.IsNullOrEmpty(diagnosticsDirectoryName)
                ? $"{DateTime.Now:yyyy-MMM-dd_HH-mm-ss}"
                : $"{diagnosticsDirectoryName}_{DateTime.Now:yyyy-MMM-dd_HH-mm-ss}");
        _diagnosticsTypeName = diagnosticsTypeName;
        _logger = logger;
    }

    public void WriteContentToSeparateFile(string fileName, string content)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        File.WriteAllText(Path.Combine(DiagnosticsDirectory, fileName), content);
    }

    public void AddInformation(string message, bool diagnosticsFileOnly = false)
    {
        var now = DateTime.Now;
        using var _ = _lock.EnterScope();
        _diagnostics.Add((now, Level.Info, message));
        if (diagnosticsFileOnly) return;

        _logger?.LogInformation(message);
        if(!ConsoleEnabled) return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{DateTime.Now:yyyy MMM dd HH:mm:ss.fff}] [INFO]\r\n{message}\r\n");
        Console.ResetColor();
    }

    public void AddError(string message, Exception ex) =>
        AddError($"{message}\r\n{ex}");

    public void AddError(string message)
    {
        var now = DateTime.Now;
        using var _ = _lock.EnterScope();
        _diagnostics.Add((now, Level.Error, message));
        _logger?.LogError(message);
        if (!ConsoleEnabled) return;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[{DateTime.Now:yyyy MMM dd HH:mm:ss.fff}] [ERROR]\r\n{message}\r\n");
        Console.ResetColor();
    }

    public void Dispose()
    {
        using var _ = _lock.EnterScope();
        if (_diagnostics.Count == 0) return;

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            var diagnosticsFile = Path.Combine(DiagnosticsDirectory, $"{_diagnosticsTypeName}_Diagnostics.md");

            File.AppendAllText(diagnosticsFile, string.Join("\r\n\r\n", _diagnostics.OrderBy(d => d.Time)
                .Select(d => $"[{d.Time:yyyy MMM dd HH:mm:ss.fff}] [{d.Level.ToString().ToUpper()}]\r\n{d.Message}")));
        }
        catch
        {
            // Ignore
        }
    }
}