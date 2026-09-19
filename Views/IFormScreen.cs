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
    bool OutcomeUnknown { get; }
    string ProgressMessage { get; }
    string Status { get; }
    int Revision { get; }
    void HandleKey(ConsoleKeyInfo key);
    void RequestCancellation();
    void MarkOutcomeUnknown(string message);
    void ResetForRetry();
    IRenderable Render();
}
