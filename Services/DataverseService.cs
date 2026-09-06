using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Auth;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Dvtui.Models;

namespace Dvtui.Services;

public class DataverseService : IDisposable
{
    private readonly ServiceClient _client;

    public DataverseService(string url)
    {
        var options = new ConnectionOptions
        {
            AuthenticationType = AuthenticationType.OAuth,
            ServiceUri = new Uri(url.TrimEnd('/')),
            RedirectUri = new Uri("http://localhost"),
            LoginPrompt = PromptBehavior.Auto,
            SkipDiscovery = true
        };

        _client = new ServiceClient(
            options,
            deferConnection: true
        );
    }

    public void Connect()
    {
        _client.Connect();

        if (!_client.IsReady)
        {
            throw new InvalidOperationException(
                $"Connection failed: {_client.LastError}"
            );
        }
    }

    public async Task<List<DataverseColumn>> GetColumnsAsync(
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

    private static string GetDisplayName(AttributeMetadata attribute)
    {
        return attribute.DisplayName?.UserLocalizedLabel?.Label ?? string.Empty;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
