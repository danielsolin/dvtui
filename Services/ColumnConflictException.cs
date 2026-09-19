namespace dvtui.Services;

public sealed class ColumnConflictException : Exception
{
    public ColumnConflictException(string message)
        : base(message)
    {
    }
}
