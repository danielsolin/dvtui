using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Services;

namespace dvtui.Views;

internal sealed class ConfirmationScreen
{
    private readonly string _title;
    private readonly IReadOnlyList<(string Label, string Value)> _details;
    private readonly IReadOnlyList<string> _warnings;
    private readonly string _instruction;
    private readonly Func<string, bool> _validate;
    private string _value = string.Empty;
    private bool _cancelled;
    private bool _submitted;

    public ConfirmationScreen(
        string title,
        IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<string> warnings,
        string instruction,
        Func<string, bool> validate
    )
    {
        _title = title;
        _details = details;
        _warnings = warnings;
        _instruction = instruction;
        _validate = validate;
    }

    public bool Show()
    {
        SessionLog.Info("UI.Confirmation", "Shown title=" + _title);
        AnsiConsole.Clear();
        AnsiConsole.Live(Render())
            .StartAsync(async context =>
            {
                while( !_cancelled && !_submitted )
                {
                    var inputLocked = Progress.DiscardPendingInput(
                        "ConfirmationScreen"
                    );
                    while( !inputLocked && Console.KeyAvailable )
                    {
                        if( Progress.DiscardPendingInput("ConfirmationScreen") )
                        {
                            inputLocked = true;
                            break;
                        }

                        var key = Console.ReadKey(intercept: true);
                        SessionLog.Key("ConfirmationScreen", key, "title=" + _title);
                        HandleKey(key);
                    }

                    context.UpdateTarget(Render());
                    await Task.Delay(50);
                }
            })
            .GetAwaiter()
            .GetResult();
        var confirmed = _submitted && _validate(_value);
        SessionLog.Info(
            "UI.Confirmation",
            "Completed title=" + _title
                + " submitted=" + _submitted
                + " confirmed=" + confirmed
        );
        return confirmed;
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        if( key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003' )
        {
            _cancelled = true;
            return;
        }

        if( key.Key == ConsoleKey.Enter )
        {
            _submitted = true;
            return;
        }

        if( key.Key == ConsoleKey.Backspace
            || key.KeyChar == '\b'
            || key.KeyChar == '\u007f' )
        {
            if( _value.Length > 0 )
            {
                _value = _value[..^1];
            }

            return;
        }

        if( !char.IsControl(key.KeyChar) )
        {
            _value += key.KeyChar;
        }
    }

    private IRenderable Render()
    {
        var table = new Table()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");
        foreach( var detail in _details )
        {
            table.AddRow(new Text(detail.Label), new Text(detail.Value));
        }

        var content = new List<IRenderable> { table };
        foreach( var warning in _warnings )
        {
            content.Add(new Text(warning, Style.Parse("yellow")));
        }

        content.Add(new Text(_instruction));
        content.Add(new Text("> " + _value, Style.Parse("cyan")));
        var panel = new Panel(new Rows(content))
            .Header(_title)
            .RoundedBorder()
            .Expand();
        return new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(
                    new Text("  Enter: confirm  Esc: cancel", Style.Parse("dim"))
                )
            );
    }
}
