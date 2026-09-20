namespace dvtui.Services;

internal static class MetadataUtilities
{
    internal static bool IsMetadataNotFound(Exception exception)
    {
        for( var current = exception; current != null; current = current.InnerException )
        {
            var message = current.Message;
            if( message.Contains("could not find", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cannot be found", StringComparison.OrdinalIgnoreCase) )
            {
                return true;
            }
        }

        return false;
    }
}
