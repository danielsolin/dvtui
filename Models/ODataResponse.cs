namespace Dvtui.Models;

public class ODataResponse<T>
{
    public List<T> Value { get; set; } = new List<T>();
}
