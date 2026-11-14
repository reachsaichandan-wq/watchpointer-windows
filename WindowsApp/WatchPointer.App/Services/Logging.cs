using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WatchPointer.App.Services;

public sealed record LogLine(DateTime Time, LogLevel Level, string Category, string Message)
{
    public string LevelText => Level switch
    {
        LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF",
        LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "   ",
    };
    public override string ToString() => $"{Time:HH:mm:ss.fff} {LevelText} {Category}: {Message}";
}

/// <summary>Keeps the last few hundred log lines in memory for the Diagnostics page.</summary>
public sealed class MemoryLogProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();
    public const int Capacity = 500;

    public event Action<LogLine>? LineAdded;

    public IReadOnlyCollection<LogLine> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Logger(this, Shorten(categoryName));

    internal void Add(LogLine line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > Capacity && _lines.TryDequeue(out _)) { }
        LineAdded?.Invoke(line);
    }

    internal static string Shorten(string category) => category[(category.LastIndexOf('.') + 1)..];

    public void Dispose() { }

    private sealed class Logger(MemoryLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += $" ({exception.GetType().Name}: {exception.Message})";
            owner.Add(new LogLine(DateTime.Now, logLevel, category, message));
        }
    }
}

/// <summary>Appends to %LOCALAPPDATA%\WatchPointer\logs\watchpointer-YYYYMMDD.log on a background writer.</summary>
public sealed class FileLogProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writer;

    public FileLogProvider(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _writer = new Thread(Write) { IsBackground = true, Name = "WatchPointer log writer" };
        _writer.Start();
        CleanUpOldFiles();
    }

    public string Directory { get; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatchPointer", "logs");

    public ILogger CreateLogger(string categoryName) => new Logger(this, MemoryLogProvider.Shorten(categoryName));

    private void Write()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try { File.AppendAllText(Path.Combine(Directory, $"watchpointer-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { /* disk full or locked: drop the line */ }
        }
    }

    private void CleanUpOldFiles()
    {
        try
        {
            foreach (var f in new DirectoryInfo(Directory).GetFiles("watchpointer-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14)))
                f.Delete();
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(1000);
    }

    private sealed class Logger(FileLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = new LogLine(DateTime.Now, logLevel, category, formatter(state, exception)).ToString();
            if (exception is not null) line += Environment.NewLine + exception;
            owner._queue.TryAdd(line);
        }
    }
}
