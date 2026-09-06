using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Dvtui.Models;

namespace Dvtui.Services;

public class DataverseService : IDataverseService, IDisposable
{
    private readonly ServiceClient _client;
    private readonly string _url;

    public DataverseService(string url)
    {
        _url = url.TrimEnd('/');
        var connectionString = 
            $"AuthType=OAuth;" +
            $"Url={_url};" +
            $"RedirectUri=http://localhost;" +
            $"AllowCreateUserDialog=true;";

        _client = new ServiceClient(connectionString);

        if (!_client.IsReady)
        {
            throw new Exception($"Connection failed: {_client.LastError}");
        }
    }

    public async Task<List<DataverseColumn>> GetColumnsAsync(
        string url,
        string entityName)
    {
        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Attributes,
            LogicalName = entityName
        };

        var response = await _client.ExecuteAsync(request);
        var entityMetadata = (RetrieveEntityResponse)response;
        
        var columns = new List<DataverseColumn>();

        if (entityMetadata.EntityMetadata.Attributes != null)
        {
            foreach (var attr in entityMetadata.EntityMetadata.Attributes)
            {
                var name = attr.LogicalName;
                var displayName = GetDisplayName(attr);

                columns.Add(new DataverseColumn
                {
                    Name = name ?? string.Empty,
                    DisplayName = displayName
                });
            }
        }

        return columns;
    }

    public async Task<List<DataverseRow>> GetDataAsync(
        string url,
        string entityName)
    {
        return await Task.FromResult(new List<DataverseRow>());
    }

    private string GetDisplayName(AttributeMetadata attribute)
    {
        return attribute.DisplayName?.UserLocalizedLabel?.Label ?? string.Empty;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
