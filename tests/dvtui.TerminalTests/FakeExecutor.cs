using System.Collections;
using System.Net.Http;

using dvtui.Services;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvtui.TerminalTests;

internal sealed class FakeExecutor : IDataverseExecutor, IDataverseWebExecutor
{
    public List<OrganizationRequest> ExecutedRequests { get; } = [];
    public List<QueryBase> Queries { get; } = [];
    public List<string> WebRequests { get; } = [];
    public Func<OrganizationRequest, OrganizationResponse?>? OnExecute;
    public Func<QueryBase, EntityCollection?>? OnRetrieve;
    public Func<HttpMethod, string, string, HttpResponseMessage?>? OnWebRequest;

    public Task<OrganizationResponse> ExecuteAsync(
        OrganizationRequest request,
        CancellationToken cancellationToken
    )
    {
        ExecutedRequests.Add(request);
        var response = OnExecute?.Invoke(request);
        if( response == null )
        {
            throw new InvalidOperationException(
                "No fake response for " + request.GetType().Name
            );
        }

        return Task.FromResult(response);
    }

    public Task<EntityCollection> RetrieveMultipleAsync(
        QueryBase query,
        CancellationToken cancellationToken
    )
    {
        Queries.Add(query);
        var response = OnRetrieve?.Invoke(query);
        if( response == null )
        {
            var name = query is QueryExpression qe ? qe.EntityName : "?";
            throw new InvalidOperationException(
                "No fake response for query " + name
            );
        }

        return Task.FromResult(response);
    }

    public Task<HttpResponseMessage> ExecuteWebRequestAsync(
        HttpMethod method,
        string queryString,
        string body,
        Dictionary<string, List<string>> customHeaders,
        string? contentType,
        CancellationToken cancellationToken
    )
    {
        WebRequests.Add(method.Method + " " + queryString);
        var response = OnWebRequest?.Invoke(method, queryString, body);
        if( response == null )
        {
            throw new InvalidOperationException(
                "No fake response for Web API " + method.Method
            );
        }

        return Task.FromResult(response);
    }
}
