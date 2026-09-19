using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace dvtui.Services;

internal static class SessionLog
{
    private static readonly object SyncRoot = new();
    private static readonly Regex QuerySecretPattern = new(
        @"(?i)(access_token|client_secret|password|refresh_token)=([^&\s]+)",
        RegexOptions.Compiled
    );
    private static readonly Regex AuthorizationPattern = new(
        @"(?i)(Authorization\s*:\s*Bearer\s+)[^\s]+",
        RegexOptions.Compiled
    );
    private static SessionLogWriter? _writer;
    private static long _operationNumber;

    public static string? FilePath => _writer?.FilePath;

    public static string? Start(string[] args)
    {
        lock( SyncRoot )
        {
            if( _writer != null )
            {
                return _writer.FilePath;
            }

            try
            {
                _writer = OpenWriter();
                Info(
                    "Application",
                    "Started"
                        + " | pid=" + Environment.ProcessId
                        + " | cwd=" + Directory.GetCurrentDirectory()
                        + " | commandLine=" + Environment.CommandLine
                        + " | args=" + FormatArguments(args)
                        + " | runtime=" + Environment.Version
                        + " | os=" + Environment.OSVersion
                );
                return _writer.FilePath;
            }
            catch( Exception ex )
            {
                _writer = null;
                Console.Error.WriteLine(
                    "Could not create the dvtui session log: " + ex.Message
                );
                return null;
            }
        }
    }

    public static void Stop()
    {
        SessionLogWriter? writer;
        lock( SyncRoot )
        {
            writer = _writer;
            _writer = null;
        }

        if( writer == null )
        {
            return;
        }

        writer.Write("INFO", "Application", "Stopped");
        writer.Dispose();
    }

    public static string NextOperationId()
    {
        var number = Interlocked.Increment(ref _operationNumber);
        return "dv-" + number.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static void Info(string category, string message)
    {
        Write("INFO", category, message);
    }

    public static void Debug(string category, string message)
    {
        Write("DEBUG", category, message);
    }

    public static void Warning(string category, string message)
    {
        Write("WARN", category, message);
    }

    public static void Exception(
        string category,
        Exception exception,
        string? message = null
    )
    {
        var prefix = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : message + " | ";
        Write("ERROR", category, prefix + exception);
    }

    public static void Screen(
        string screen,
        string eventName,
        string? details = null
    )
    {
        var suffix = string.IsNullOrWhiteSpace(details)
            ? string.Empty
            : " | " + details;
        Info("UI.Screen", "screen=" + screen + " event=" + eventName + suffix);
    }

    public static void Key(
        string screen,
        ConsoleKeyInfo key,
        string? state = null
    )
    {
        var character = key.KeyChar == '\0'
            ? "<none>"
            : "U+" + ((int)key.KeyChar).ToString("X4", CultureInfo.InvariantCulture)
                + " '" + key.KeyChar + "'";
        var suffix = string.IsNullOrWhiteSpace(state)
            ? string.Empty
            : " | state=" + state;
        Debug(
            "UI.Key",
            "screen=" + screen
                + " key=" + key.Key
                + " modifiers=" + key.Modifiers
                + " char=" + character
                + suffix
        );
    }

    public static void Write(
        string level,
        string category,
        string message
    )
    {
        var writer = _writer;
        writer?.Write(level, category, message);
    }

    internal static string SafeSingleLine(string value)
    {
        var result = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        result = QuerySecretPattern.Replace(result, "$1=<redacted>");
        return AuthorizationPattern.Replace(result, "$1<redacted>");
    }

    private static SessionLogWriter OpenWriter()
    {
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tmp",
            "logs"
        );
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.Now.ToString(
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture
        );

        for( var index = 0; index < 100; index++ )
        {
            var suffix = index == 0
                ? string.Empty
                : "-" + index.ToString("D2", CultureInfo.InvariantCulture);
            var path = Path.Combine(directory, timestamp + suffix + ".log");
            try
            {
                return new SessionLogWriter(path);
            }
            catch( IOException ) when( index < 99 )
            {
            }
        }

        throw new IOException("Could not allocate a unique session log file.");
    }

    private static string FormatArguments(IEnumerable<string> args)
    {
        var values = args.Select(
            value => "[" + SafeSingleLine(value) + "]"
        );
        return string.Join(",", values);
    }
}

internal sealed class SessionLogWriter : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public SessionLogWriter(string filePath)
    {
        FilePath = filePath;
        var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        _writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        )
        {
            AutoFlush = true
        };
        _writer.WriteLine("# dvtui session log");
        _writer.WriteLine("# file=" + filePath);
    }

    public string FilePath { get; }

    public void Write(string level, string category, string message)
    {
        var line = DateTimeOffset.Now.ToString(
            "O",
            CultureInfo.InvariantCulture
        )
            + " | +"
            + _stopwatch.ElapsedMilliseconds.ToString(
                CultureInfo.InvariantCulture
            )
            + "ms | tid="
            + Environment.CurrentManagedThreadId
            + " | "
            + level
            + " | "
            + category
            + " | "
            + SessionLog.SafeSingleLine(message);

        lock( _syncRoot )
        {
            if( _disposed )
            {
                return;
            }

            try
            {
                _writer.WriteLine(line);
            }
            catch( ObjectDisposedException )
            {
            }
            catch( IOException )
            {
            }
        }
    }

    public void Dispose()
    {
        lock( _syncRoot )
        {
            if( _disposed )
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
