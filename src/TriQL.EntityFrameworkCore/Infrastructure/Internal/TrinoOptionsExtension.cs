using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TriQL.Client;
using TriQL.Data.ADO;

namespace TriQL.EntityFrameworkCore.Infrastructure.Internal;

/// <summary>
/// The EF Core options extension for the Trino provider. Carries how to connect — a connection
/// string, an existing <see cref="DbConnection"/>, a <see cref="TrinoSessionOptions"/> or a
/// <see cref="TrinoDataSource"/> — and the provider-specific options set through
/// <see cref="TrinoDbContextOptionsBuilder"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private TrinoSessionOptions? _sessionOptions;
    private TrinoDataSource? _dataSource;
    private bool _useUtcSessionTimeZone = true;

    /// <summary>Initializes an empty extension.</summary>
    public TrinoOptionsExtension()
    {
    }

    /// <summary>Initializes a copy of <paramref name="copyFrom"/>.</summary>
    protected TrinoOptionsExtension(TrinoOptionsExtension copyFrom)
        : base(copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);
        _sessionOptions = copyFrom._sessionOptions;
        _dataSource = copyFrom._dataSource;
        _useUtcSessionTimeZone = copyFrom._useUtcSessionTimeZone;
    }

    /// <inheritdoc />
    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>The session options connections are created from, when the context was configured with them.</summary>
    public virtual TrinoSessionOptions? SessionOptions => _sessionOptions;

    /// <summary>The data source connections are created from, when the context was configured with one.</summary>
    public virtual TrinoDataSource? DataSource => _dataSource;

    /// <summary>
    /// Whether connections created from a connection string use a <c>UTC</c> session time zone when
    /// the connection string does not set <c>TimeZone</c>. Default <see langword="true"/>.
    /// </summary>
    public virtual bool UseUtcSessionTimeZone => _useUtcSessionTimeZone;

    /// <summary>Returns a copy that connects with <paramref name="sessionOptions"/>, replacing any connection configured earlier.</summary>
    public virtual TrinoOptionsExtension WithSessionOptions(TrinoSessionOptions sessionOptions)
    {
        ArgumentNullException.ThrowIfNull(sessionOptions);

        var clone = (TrinoOptionsExtension)WithConnectionString(null);
        clone._sessionOptions = sessionOptions;
        return clone;
    }

    /// <summary>Returns a copy that creates connections from <paramref name="dataSource"/>, replacing any connection configured earlier.</summary>
    public virtual TrinoOptionsExtension WithDataSource(TrinoDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var clone = (TrinoOptionsExtension)WithConnectionString(null);
        clone._dataSource = dataSource;
        return clone;
    }

    /// <summary>Returns a copy with <see cref="UseUtcSessionTimeZone"/> changed.</summary>
    public virtual TrinoOptionsExtension WithUseUtcSessionTimeZone(bool useUtcSessionTimeZone)
    {
        var clone = (TrinoOptionsExtension)Clone();
        clone._useUtcSessionTimeZone = useUtcSessionTimeZone;
        return clone;
    }

    /// <summary>Returns a copy using <paramref name="connectionString"/>, replacing any other connection configured earlier.</summary>
    public override RelationalOptionsExtension WithConnectionString(string? connectionString) =>
        ((TrinoOptionsExtension)base.WithConnectionString(connectionString)).ClearOtherConnections(keep: Kept.ConnectionString);

    /// <summary>Returns a copy using <paramref name="connection"/>, replacing any other connection configured earlier.</summary>
    public override RelationalOptionsExtension WithConnection(DbConnection? connection, bool owned) =>
        ((TrinoOptionsExtension)base.WithConnection(connection, owned)).ClearOtherConnections(keep: Kept.Connection);

    /// <inheritdoc />
    protected override RelationalOptionsExtension Clone() => new TrinoOptionsExtension(this);

    /// <inheritdoc />
    public override void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkTrino();

    /// <summary>
    /// Called on a fresh clone: clears the session options and data source, then whichever of the
    /// base connection string or connection is not being kept. EF's base class does not clear one
    /// when the other is set, so repeated <c>UseTrino</c> calls would otherwise configure two.
    /// </summary>
    private TrinoOptionsExtension ClearOtherConnections(Kept keep)
    {
        _sessionOptions = null;
        _dataSource = null;

        // Non-virtual base calls on this instance, so they do not recurse into the overrides above. The
        // two-argument WithConnection is the one to call: EF's one-argument overload delegates to it
        // virtually, which would re-enter this class and clear the connection string being kept.
        return (TrinoOptionsExtension)(keep == Kept.ConnectionString
            ? base.WithConnection(null, owned: false)
            : base.WithConnectionString(null));
    }

    private enum Kept
    {
        ConnectionString,
        Connection,
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : RelationalExtensionInfo(extension)
    {
        private string? _logFragment;

        private new TrinoOptionsExtension Extension => (TrinoOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => true;

        // Never includes the connection string or any credential.
        public override string LogFragment
        {
            get
            {
                if (_logFragment is null)
                {
                    var builder = new StringBuilder(base.LogFragment);
                    if (!Extension._useUtcSessionTimeZone)
                    {
                        builder.Append("UseUtcSessionTimeZone=False ");
                    }

                    if (Extension._sessionOptions is not null)
                    {
                        builder.Append("SessionOptions ");
                    }

                    if (Extension._dataSource is not null)
                    {
                        builder.Append("DataSource ");
                    }

                    _logFragment = builder.ToString();
                }

                return _logFragment;
            }
        }

        // None of the Trino-specific options change the services EF builds, so contexts that differ
        // only in them share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            ArgumentNullException.ThrowIfNull(debugInfo);
            debugInfo["Trino:" + nameof(UseUtcSessionTimeZone)] =
                Extension._useUtcSessionTimeZone.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }
    }
}
