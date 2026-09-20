using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal static class Progress
{
    private const int MinimumPulseWidth = 4;
    private const int MaximumPulseWidth = 12;
    private static readonly object SyncRoot = new();
    private static ProgressState? _current;
    private static bool _drainAfterRelease;

    public static bool IsActive => Volatile.Read(ref _current) != null;

    public static bool DiscardPendingInput(string screen)
    {
        ProgressState? current;
        var releaseDrain = false;
        lock( SyncRoot )
        {
            current = _current;
            if( current == null && _drainAfterRelease )
            {
                _drainAfterRelease = false;
                releaseDrain = true;
            }
        }

        if( current == null && !releaseDrain )
        {
            return false;
        }

        var message = current?.Message ?? "completed";
        while( Console.KeyAvailable )
        {
            var key = Console.ReadKey(intercept: true);
            SessionLog.Key(
                screen,
                key,
                "ignored=true progress=" + message
            );
        }

        return true;
    }

    public static Task Show(
        string message,
        Func<CancellationToken, Task> callback
    )
    {
        return Show(message, CancellationToken.None, callback);
    }

    public static Task Show(
        string message,
        Func<Task> callback
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Show(message, _ => callback());
    }

    public static Task Show(
        string message,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> callback
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(callback);
        return Run(message, cancellationToken, callback);
    }

    public static Task<T> Show<T>(
        string message,
        Func<CancellationToken, Task<T>> callback
    )
    {
        return Show(message, CancellationToken.None, callback);
    }

    public static Task<T> Show<T>(
        string message,
        Func<Task<T>> callback
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Show(message, _ => callback());
    }

    public static Task<T> Show<T>(
        string message,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> callback
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(callback);
        return Run(message, cancellationToken, callback);
    }

    public static IRenderable RenderStatus(
        int width,
        string idleMessage,
        Style idleStyle,
        bool showActiveMessage = true
    )
    {
        var current = Volatile.Read(ref _current);
        if( current == null )
        {
            return new Text(idleMessage, idleStyle);
        }

        if( !showActiveMessage )
        {
            return new Markup(RenderPulse(width, current));
        }

        var messageWidth = Math.Min(
            current.Message.Length,
            Math.Max(1, width / 2)
        );
        var progressWidth = Math.Max(1, width - messageWidth - 1);
        var message = current.Message.Length > messageWidth
            ? current.Message[..Math.Max(1, messageWidth - 1)] + "…"
            : current.Message;
        var pulse = RenderPulse(progressWidth, current);
        return new Markup(
            pulse + " [silver]" + Markup.Escape(message) + "[/]"
        );
    }

    private static async Task Run(
        string message,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> callback
    )
    {
        using var state = Begin(message, cancellationToken);
        await Task.Run(
            () => callback(state.Token),
            CancellationToken.None
        );
    }

    private static async Task<T> Run<T>(
        string message,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> callback
    )
    {
        using var state = Begin(message, cancellationToken);
        return await Task.Run(
            () => callback(state.Token),
            CancellationToken.None
        );
    }

    private static ProgressState Begin(
        string message,
        CancellationToken cancellationToken
    )
    {
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var state = new ProgressState(message, linkedCancellation);
        lock( SyncRoot )
        {
            state.Previous = _current;
            _current = state;
        }

        return state;
    }

    private static string RenderPulse(int width, ProgressState state)
    {
        width = Math.Max(1, width);
        var pulseWidth = Math.Clamp(
            width / 8,
            MinimumPulseWidth,
            MaximumPulseWidth
        );
        pulseWidth = Math.Min(width, pulseWidth);
        var travel = Math.Max(0, width - pulseWidth - 1);
        var cycle = Math.Max(1, travel * 2);
        var position = Interlocked.Increment(ref state.Frame) % cycle;
        var offset = position <= travel
            ? position
            : cycle - position;
        var before = new string('─', offset);
        var pulse = new string('━', pulseWidth);
        var after = new string('─', Math.Max(0, width - offset - pulseWidth));
        return "[grey23]" + before + "[/][cyan1]" + pulse + "[/]"
            + "[grey23]" + after + "[/]";
    }

    private sealed class ProgressState : IDisposable
    {
        private int _disposed;

        public ProgressState(
            string message,
            CancellationTokenSource cancellation
        )
        {
            Message = message;
            Cancellation = cancellation;
        }

        public string Message { get; }
        public CancellationTokenSource Cancellation { get; }
        public ProgressState? Previous { get; set; }
        public CancellationToken Token => Cancellation.Token;
        public int Frame;

        public void Dispose()
        {
            if( Interlocked.Exchange(ref _disposed, 1) != 0 )
            {
                return;
            }

            lock( SyncRoot )
            {
                if( ReferenceEquals(_current, this) )
                {
                    _current = Previous;
                }
                else
                {
                    RemoveFromChain();
                }

                if( _current == null )
                {
                    _drainAfterRelease = true;
                }
            }

            Cancellation.Dispose();
        }

        private void RemoveFromChain()
        {
            var next = _current;
            while( next != null )
            {
                if( ReferenceEquals(next.Previous, this) )
                {
                    next.Previous = Previous;
                    return;
                }

                next = next.Previous;
            }
        }
    }
}
