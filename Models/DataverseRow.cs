using System.Collections.Generic;

namespace Dvtui.Models;

public class DataverseRow
{
    public Dictionary<string, object> Values { get; set; } = 
        new Dictionary<string, object>();
}
