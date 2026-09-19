using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal enum FormAction
{
    None,
    Close,
    Submit
}

internal interface IFormScreen
{
    FormAction PendingAction { get; }
    void HandleKey(ConsoleKeyInfo key);
    IRenderable Render();
}
