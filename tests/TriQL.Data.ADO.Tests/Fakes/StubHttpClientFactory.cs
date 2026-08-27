using System.Net.Http;

namespace TriQL.Data.ADO.Tests.Fakes;

/// <summary>An <see cref="IHttpClientFactory"/> that always returns an <see cref="HttpClient"/> backed by a given handler.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
