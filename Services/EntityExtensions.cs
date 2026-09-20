using Microsoft.Xrm.Sdk;

namespace dvtui.Services;

internal static class EntityExtensions
{
    public static int? GetOptionValue(this Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        return entity.GetAttributeValue<OptionSetValue>(attribute)?.Value;
    }

    public static Guid? GetGuidValue(this Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        return entity[attribute] switch
        {
            Guid value => value,
            EntityReference reference => reference.Id,
            _ => null
        };
    }
}
