using Azure.Core;

namespace TriQL.Client.Auth.Tests;

/// <summary>A scriptable <see cref="TokenCredential"/> stand-in for a stub Entra ID token endpoint.</summary>
internal sealed class FakeTokenCredential : TokenCredential
{
    private readonly Func<TokenRequestContext, AccessToken> _issue;

    public int CallCount { get; private set; }
    public List<string[]> RequestedScopes { get; } = [];

    public FakeTokenCredential(Func<TokenRequestContext, AccessToken> issue) => _issue = issue;

    public static FakeTokenCredential AlwaysReturning(string token, TimeSpan validFor) =>
        new(_ => new AccessToken(token, DateTimeOffset.UtcNow.Add(validFor)));

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedScopes.Add(requestContext.Scopes);
        return _issue(requestContext);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}
