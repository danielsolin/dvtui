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
    bool HasError { get; }
    int Revision { get; }
    void HandleKey(ConsoleKeyInfo key);
    void RequestCancellation();
    void ResetForRetry();
    IRenderable Render();
}
