using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Services;

namespace dvtui.Views;

internal static class MessageScreen
{
    public static void Show(
        string title,
        IReadOnlyList<IRenderable> content
    )
    {
        SessionLog.Info("UI.Message", "Shown title=" + title);
        var panel = new Panel(new Rows(content))
            .Header(title)
            .RoundedBorder()
            .Expand();
        var layout = new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(
                    new Text("  Enter/Esc: back", Style.Parse("dim"))
                )
            );
        AnsiConsole.Clear();
        AnsiConsole.Live(layout)
            .StartAsync(async context =>
            {
                while( true )
                {
                    var inputLocked = Progress.DiscardPendingInput(
                        "MessageScreen"
                    );
                    while( !inputLocked && Console.KeyAvailable )
                    {
                        if( Progress.DiscardPendingInput("MessageScreen") )
                        {
                            inputLocked = true;
                            break;
                        }

                        var key = Console.ReadKey(intercept: true);
                        SessionLog.Key("MessageScreen", key, "title=" + title);
                        if( key.Key == ConsoleKey.Enter
                            || key.Key == ConsoleKey.Escape
                            || key.KeyChar == '\u0003' )
                        {
                            return;
                        }
                    }

                    context.UpdateTarget(layout);
                    await Task.Delay(50);
                }
            })
            .GetAwaiter()
            .GetResult();
        SessionLog.Info("UI.Message", "Completed title=" + title);
    }
}
