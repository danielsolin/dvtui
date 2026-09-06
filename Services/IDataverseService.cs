using System.Collections.Generic;
using System.Threading.Tasks;
using Dvtui.Models;

namespace Dvtui.Services;

public interface IDataverseService
{
    Task<List<DataverseColumn>> GetColumnsAsync(string url, string entityName);
    Task<List<DataverseRow>> GetDataAsync(string url, string entityName);
}
