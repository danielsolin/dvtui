using Dvtui.Models;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Auth;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using Label = Microsoft.Xrm.Sdk.Label;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Dvtui.Services;

public class DataverseService : IDisposable
{
    private readonly ServiceClient _client;

    public DataverseService(string url)
    {
        url = url.Replace("http://", "").TrimEnd('/');
        if (url.StartsWith("https://") == false)
            url = "https://" + url;

        var options = new ConnectionOptions
        {
            AuthenticationType = AuthenticationType.OAuth,
            ServiceUri = new Uri(url),
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

    public async Task<List<DataverseEntity>> GetEntitiesAsync(
        CancellationToken cancellationToken
    )
    {
        var request = new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = false
        };

        var response = (RetrieveAllEntitiesResponse)await _client.ExecuteAsync(
            request,
            cancellationToken
        );
        var entities = new List<DataverseEntity>();
        foreach (var metadata in response.EntityMetadata)
        {
            entities.Add(new DataverseEntity
            {
                LogicalName = metadata.LogicalName ?? string.Empty,
                SchemaName = metadata.SchemaName ?? string.Empty,
                DisplayName = GetLabel(metadata.DisplayName),
                CollectionName = GetLabel(metadata.DisplayCollectionName),
                Description = GetLabel(metadata.Description),
                EntitySetName = metadata.EntitySetName ?? string.Empty,
                PrimaryIdAttribute = metadata.PrimaryIdAttribute
                    ?? string.Empty,
                PrimaryNameAttribute = metadata.PrimaryNameAttribute
                    ?? string.Empty,
                OwnershipType = metadata.OwnershipType?.ToString(),
                ObjectTypeCode = metadata.ObjectTypeCode,
                IsCustom = metadata.IsCustomEntity,
                IsCustomizable = metadata.IsCustomizable?.Value,
                IsManaged = metadata.IsManaged,
                IsActivity = metadata.IsActivity
            });
        }

        return entities
            .OrderBy(entity => entity.LogicalName, StringComparer.Ordinal)
            .ToList();
    }

    private static string GetLabel(Label? label)
    {
        return label?.UserLocalizedLabel?.Label
            ?? label?.LocalizedLabels.FirstOrDefault()?.Label
            ?? string.Empty;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
