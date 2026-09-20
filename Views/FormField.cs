namespace dvtui.Views;

internal enum FormInputKind
{
    Text,
    Integer,
    Decimal,
    Choice
}

internal sealed class FormField
{
    public FormField(
        string label,
        Func<string> read,
        Action<string> write,
        FormInputKind inputKind = FormInputKind.Text,
        bool choice = false,
        Action<int>? changeChoice = null
    )
    {
        Label = label;
        Read = read;
        Write = write;
        InputKind = inputKind;
        Choice = choice;
        ChangeChoice = changeChoice;
    }

    public string Label { get; }
    public Func<string> Read { get; }
    public Action<string> Write { get; }
    public FormInputKind InputKind { get; }
    public bool Choice { get; }
    public Action<int>? ChangeChoice { get; }
}
