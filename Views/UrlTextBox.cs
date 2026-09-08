using Spectre.Console;
using Spectre.Console.Rendering;

namespace Dvtui.Views;

internal sealed class UrlTextBox
{
    private int _cursor;

    public string Value { get; private set; }

    public UrlTextBox(string value)
    {
        Value = value;
        _cursor = value.Length;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch( key.Key )
        {
            case ConsoleKey.LeftArrow:
                _cursor = Math.Max(0, _cursor - 1);
                break;
            case ConsoleKey.RightArrow:
                _cursor = Math.Min(Value.Length, _cursor + 1);
                break;
            case ConsoleKey.Home:
                _cursor = 0;
                break;
            case ConsoleKey.End:
                _cursor = Value.Length;
                break;
            case ConsoleKey.Backspace when _cursor > 0:
                _cursor--;
                Value = Value.Remove(_cursor, 1);
                break;
            case ConsoleKey.Delete when _cursor < Value.Length:
                Value = Value.Remove(_cursor, 1);
                break;
            default:
                if( !char.IsControl(key.KeyChar) )
                {
                    Value = Value.Insert(_cursor, key.KeyChar.ToString());
                    _cursor++;
                }
                break;
        }
    }

    public IRenderable Render(int width, bool focused)
    {
        width = Math.Max(1, width);
        var start = Math.Max(0, _cursor - width + 1);
        var visible = Value[start..];
        if( visible.Length > width )
        {
            visible = visible[..width];
        }

        if( !focused )
        {
            return new Text(visible);
        }

        var cursor = _cursor - start;
        var before = Markup.Escape(visible[..cursor]);
        var current = cursor < visible.Length ? visible[cursor] : ' ';
        var after = cursor < visible.Length
            ? Markup.Escape(visible[(cursor + 1)..])
            : string.Empty;

        return new Markup(
            $"{before}[black on cyan1]{Markup.Escape(current.ToString())}[/]"
            + after
        );
    }
}
