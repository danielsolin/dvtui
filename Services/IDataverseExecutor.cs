using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvtui.Services;

public interface IDataverseExecutor
{
    Task<OrganizationResponse> ExecuteAsync(
        OrganizationRequest request,
        CancellationToken cancellationToken
    );

    Task<EntityCollection> RetrieveMultipleAsync(
        QueryBase query,
        CancellationToken cancellationToken
    );
}

public sealed class ServiceClientExecutor : IDataverseExecutor
{
    private readonly Microsoft.PowerPlatform.Dataverse.Client.ServiceClient
        _client;

    public ServiceClientExecutor(
        Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client
    )
    {
        _client = client;
    }

    public Task<OrganizationResponse> ExecuteAsync(
        OrganizationRequest request,
        CancellationToken cancellationToken
    )
    {
        return _client.ExecuteAsync(request, cancellationToken);
    }

    public Task<EntityCollection> RetrieveMultipleAsync(
        QueryBase query,
        CancellationToken cancellationToken
    )
    {
        return _client.RetrieveMultipleAsync(query, cancellationToken);
    }
}
