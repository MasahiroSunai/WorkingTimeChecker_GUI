using System.Text;

namespace WorkingTimeChecker.Core;

public sealed class AppLogger : IDisposable
{
    private readonly object _lockObject = new();
    private readonly StreamWriter _writer;

    public string LogFilePath { get; }

    public AppLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        LogFilePath = Path.Combine(
            logDirectory,
            $"WorkingTimeChecker_{DateTime.Now:yyyyMMdd}.log"
        );

        _writer = new StreamWriter(
            LogFilePath,
            append: true,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true
            )
        )
        {
            AutoFlush = true
        };
    }

    public void WriteLine(string message)
    {
        var logLine =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";

        lock (_lockObject)
        {
            Console.WriteLine(message);
            _writer.WriteLine(logLine);
        }
    }

    public void WriteError(string message)
    {
        var logLine =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";

        lock (_lockObject)
        {
            Console.Error.WriteLine(message);
            _writer.WriteLine(logLine);
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            _writer.Dispose();
        }
    }
}