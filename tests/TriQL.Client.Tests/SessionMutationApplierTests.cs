using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

public sealed class SessionMutationApplierTests
{
    private static TrinoSession CreateSession() => new(new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") });

    private static HttpResponseMessage CreateResponse(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage();
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    [Fact]
    public void SetCatalog_ReplacesCatalog()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Catalog", "hive"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("hive", session.Catalog);
    }

    [Fact]
    public void SetSchema_ReplacesSchema()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Schema", "default"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("default", session.Schema);
    }

    [Fact]
    public void SetPath_ReplacesPath()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Path", "catalog.schema"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("catalog.schema", session.Path);
    }

    [Fact]
    public void SetSession_UpsertsProperty_AndUrlDecodesValue()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Session", "query_max_run_time=1%20day"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("1 day", session.SessionProperties["query_max_run_time"]);
    }

    [Fact]
    public void ClearSession_RemovesProperty()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.SessionProperties["query_max_run_time"] = "1 day";
        var session = new TrinoSession(options);
        using var response = CreateResponse(("X-Trino-Clear-Session", "query_max_run_time"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.False(session.SessionProperties.ContainsKey("query_max_run_time"));
    }

    [Fact]
    public void SetRole_UpsertsRole()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Role", "hive=ROLE%7Badmin%7D"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal(TrinoSelectedRole.Named("admin"), session.Roles["hive"]);
    }

    [Fact]
    public void SetOriginalRoles_ReplacesOriginalRoles()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Original-Roles", "ALL"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal([TrinoSelectedRole.All], session.OriginalRoles);
    }

    [Fact]
    public void AddedPrepare_AddsPreparedStatement()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Added-Prepare", "stmt1=SELECT%201"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("SELECT 1", session.PreparedStatements["stmt1"]);
    }

    [Fact]
    public void DeallocatedPrepare_RemovesPreparedStatement()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.PreparedStatements["stmt1"] = "SELECT 1";
        var session = new TrinoSession(options);
        using var response = CreateResponse(("X-Trino-Deallocated-Prepare", "stmt1"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.False(session.PreparedStatements.ContainsKey("stmt1"));
    }

    [Fact]
    public void SetAuthorizationUser_ReplacesAuthorizationUser()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Set-Authorization-User", "bob"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("bob", session.AuthorizationUser);
    }

    [Fact]
    public void ResetAuthorizationUser_ClearsAuthorizationUser()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), AuthorizationUser = "bob" };
        var session = new TrinoSession(options);
        using var response = CreateResponse(("X-Trino-Reset-Authorization-User", ""));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Null(session.AuthorizationUser);
    }

    [Fact]
    public void StartedTransactionId_IsRecorded()
    {
        var session = CreateSession();
        using var response = CreateResponse(("X-Trino-Started-Transaction-Id", "txn-1"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("txn-1", session.TransactionId);
    }

    [Fact]
    public void ClearTransactionId_ClearsTransactionId()
    {
        var session = CreateSession();
        using var started = CreateResponse(("X-Trino-Started-Transaction-Id", "txn-1"));
        session.Apply(SessionMutationApplier.Parse(started));

        using var cleared = CreateResponse(("X-Trino-Clear-Transaction-Id", ""));
        session.Apply(SessionMutationApplier.Parse(cleared));

        Assert.Null(session.TransactionId);
    }

    [Fact]
    public void CombinedMutations_AreAllAppliedTogether()
    {
        var session = CreateSession();
        using var response = CreateResponse(
            ("X-Trino-Set-Catalog", "hive"),
            ("X-Trino-Set-Schema", "default"),
            ("X-Trino-Set-Session", "k=v"),
            ("X-Trino-Set-Role", "ALL"));

        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal("hive", session.Catalog);
        Assert.Equal("default", session.Schema);
        Assert.Equal("v", session.SessionProperties["k"]);
        Assert.Equal(TrinoSelectedRole.All, session.Roles[""]);
    }

    [Fact]
    public void Apply_RaisesSessionChanged_OnceForTheBatch_WithAppliedHeaderNames()
    {
        var session = CreateSession();
        var raisedCount = 0;
        IReadOnlyList<string>? appliedHeaders = null;
        session.SessionChanged += (_, args) =>
        {
            raisedCount++;
            appliedHeaders = args.AppliedHeaders;
        };

        using var response = CreateResponse(("X-Trino-Set-Catalog", "hive"), ("X-Trino-Set-Schema", "default"));
        session.Apply(SessionMutationApplier.Parse(response));

        Assert.Equal(1, raisedCount);
        Assert.Equal(["X-Trino-Set-Catalog", "X-Trino-Set-Schema"], appliedHeaders);
    }

    [Fact]
    public void Apply_DoesNotRaiseSessionChanged_WhenNoMutationHeadersPresent()
    {
        var session = CreateSession();
        var raised = false;
        session.SessionChanged += (_, _) => raised = true;

        using var response = CreateResponse();
        session.Apply(SessionMutationApplier.Parse(response));

        Assert.False(raised);
    }
}
