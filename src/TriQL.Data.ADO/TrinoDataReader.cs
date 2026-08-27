using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using TriQL.Client;
using TriQL.Data.ADO.Internal;

namespace TriQL.Data.ADO;

/// <summary>
/// A <see cref="DbDataReader"/> over a <see cref="TrinoResultSet"/>. See FR-9.3.
/// </summary>
#pragma warning disable CA1010 // The required DbDataReader base only implements non-generic IEnumerable; there is no generic ADO.NET contract to satisfy instead.
public sealed class TrinoDataReader : DbDataReader, IDbColumnSchemaGenerator
#pragma warning restore CA1010
{
    private readonly TrinoResultSet _resultSet;
    private readonly CommandBehavior _behavior;
    private readonly Action? _onClosed;
    private IAsyncEnumerator<TrinoRow>? _enumerator;
    private Dictionary<string, int>? _ordinalCache;
    private IReadOnlyList<TrinoColumn> _columns = [];
    private TrinoRow? _current;
    private TrinoRow? _pending;
    private bool _hasPending;
    private bool _closed;
    private bool _singleRowConsumed;

    private TrinoDataReader(TrinoResultSet resultSet, CommandBehavior behavior, Action? onClosed)
    {
        _resultSet = resultSet;
        _behavior = behavior;
        _onClosed = onClosed;
    }

    /// <summary>
    /// Creates and primes a reader: waits for the schema, and — unless <see cref="CommandBehavior.SchemaOnly"/>
    /// is set — eagerly fetches the first row so <see cref="HasRows"/> is answerable before the
    /// first <see cref="Read"/> (FR-9.3.5).
    /// </summary>
    internal static async Task<TrinoDataReader> CreateAsync(
        TrinoResultSet resultSet, CommandBehavior behavior, Action? onClosed, CancellationToken cancellationToken)
    {
        var reader = new TrinoDataReader(resultSet, behavior, onClosed);
        reader._columns = await resultSet.WaitForSchemaAsync(cancellationToken).ConfigureAwait(false);

        if ((behavior & CommandBehavior.SchemaOnly) == 0)
        {
            var enumerator = resultSet.ReadRowsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            reader._enumerator = enumerator;
            reader._hasPending = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (reader._hasPending)
            {
                reader._pending = enumerator.Current;
            }
        }
        else
        {
            // FR-9.2.13: SchemaOnly must resolve columns without buffering row data. Dispose() is the
            // deliberate non-blocking cancel SIGNAL documented on TrinoResultSet, not a blocking call;
            // awaiting DisposeAsync() here would wait for the background pump to fully drain instead.
#pragma warning disable CA1849
            resultSet.Dispose();
#pragma warning restore CA1849
        }

        return reader;
    }

    /// <inheritdoc/>
    public override int Depth => 0;

    /// <inheritdoc/>
    public override int FieldCount => _closed ? 0 : _columns.Count;

    /// <inheritdoc/>
    public override bool HasRows => _hasPending || _current is not null;

    /// <inheritdoc/>
    public override bool IsClosed => _closed;

    /// <inheritdoc/>
    public override int RecordsAffected
    {
        get
        {
            if (_columns.Count > 0)
            {
                return -1;
            }

            var count = _resultSet.UpdateCount;
            if (count is null)
            {
                return -1;
            }

            return count > int.MaxValue ? int.MaxValue : (int)count;
        }
    }

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc/>
    public override bool Read() => SyncBridge.Run(() => ReadAsync(CancellationToken.None));

    /// <inheritdoc/>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        if (_singleRowConsumed)
        {
            return false;
        }

        if (_hasPending)
        {
            _current = _pending;
            _hasPending = false;
            _pending = null;
        }
        else if (_enumerator is not null && await _enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            _current = _enumerator.Current;
        }
        else
        {
            _current = null;
            return false;
        }

        if ((_behavior & CommandBehavior.SingleRow) != 0)
        {
            // FR-9.2.13: SingleRow returns at most one row and cancels the query afterwards. Dispose()
            // is the deliberate non-blocking cancel signal (see the CreateAsync comment above).
#pragma warning disable CA1849
            _singleRowConsumed = true;
            _resultSet.Dispose();
#pragma warning restore CA1849
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool NextResult() => false;

    /// <inheritdoc/>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <inheritdoc/>
    public override string GetName(int ordinal) => Column(ordinal).Name;

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => Column(ordinal).TypeName;

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2093", Justification = "TrinoColumn.ClrType returns only statically-known CLR types; no reflection-dependent members are required.")]
    public override Type GetFieldType(int ordinal) => Column(ordinal).ClrType;

    /// <inheritdoc/>
    /// <exception cref="IndexOutOfRangeException">No column named <paramref name="name"/> exists (FR-9.3.8).</exception>
    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _ordinalCache ??= BuildOrdinalCache();

        // CA2201: IndexOutOfRangeException is reserved by the runtime, but FR-9.3.8 mandates it
        // specifically to match the real-world DbDataReader.GetOrdinal contract (e.g. SqlDataReader).
#pragma warning disable CA2201
        return _ordinalCache.TryGetValue(name, out var ordinal)
            ? ordinal
            : throw new IndexOutOfRangeException($"No column named '{name}' exists.");
#pragma warning restore CA2201
    }

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => RequireRow().GetBoolean(ordinal);

    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => checked((byte)RequireRow().GetInt16(ordinal));

    /// <inheritdoc/>
    public override char GetChar(int ordinal) =>
        RequireRow().GetString(ordinal) is { Length: > 0 } s ? s[0] : throw new InvalidCastException("An empty string cannot be returned as a char.");

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => RequireRow().GetDateTime(ordinal);

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => RequireRow().GetDecimal(ordinal);

    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => RequireRow().GetDouble(ordinal);

    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => RequireRow().GetFloat(ordinal);

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => RequireRow().GetGuid(ordinal);

    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => RequireRow().GetInt16(ordinal);

    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => RequireRow().GetInt32(ordinal);

    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => RequireRow().GetInt64(ordinal);

    /// <inheritdoc/>
    public override string GetString(int ordinal) => RequireRow().GetString(ordinal);

    /// <inheritdoc/>
    public override object GetValue(int ordinal) => RequireRow().GetValue(ordinal) ?? DBNull.Value;

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var row = RequireRow();
        var count = Math.Min(values.Length, row.FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = row.GetValue(i) ?? DBNull.Value;
        }

        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal) => RequireRow().IsDBNull(ordinal);

    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal) => RequireRow().GetFieldValue<T>(ordinal);

    /// <inheritdoc/>
    public override Stream GetStream(int ordinal) => GetValue(ordinal) switch
    {
        byte[] bytes => new MemoryStream(bytes, writable: false),
        string s => new MemoryStream(Encoding.UTF8.GetBytes(s), writable: false),
        _ => throw new InvalidCastException($"Column '{GetName(ordinal)}' cannot be returned as a Stream."),
    };

    /// <inheritdoc/>
    public override TextReader GetTextReader(int ordinal) => GetValue(ordinal) switch
    {
        string s => new StringReader(s),
        _ => throw new InvalidCastException($"Column '{GetName(ordinal)}' cannot be returned as a TextReader."),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// Implements the documented ADO.NET contract precisely (FR-9.3.2): a <see langword="null"/>
    /// <paramref name="buffer"/> returns the total length; offsets are range-checked; the return
    /// value is the number of bytes actually copied, clamped to what remains.
    /// </remarks>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataOffset);
        var value = RequireRow().GetBytes(ordinal);

        if (buffer is null)
        {
            return value.Length;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bufferOffset, buffer.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (dataOffset >= value.Length)
        {
            return 0;
        }

        var available = value.Length - dataOffset;
        var toCopy = (int)Math.Min(length, Math.Min(available, buffer.Length - bufferOffset));
        if (toCopy <= 0)
        {
            return 0;
        }

        Array.Copy(value, dataOffset, buffer, bufferOffset, toCopy);
        return toCopy;
    }

    /// <inheritdoc/>
    /// <remarks>Implements the same contract as <see cref="GetBytes"/> for character data (FR-9.3.2).</remarks>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataOffset);
        var value = RequireRow().GetString(ordinal);

        if (buffer is null)
        {
            return value.Length;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bufferOffset, buffer.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (dataOffset >= value.Length)
        {
            return 0;
        }

        var available = value.Length - dataOffset;
        var toCopy = (int)Math.Min(length, Math.Min(available, buffer.Length - bufferOffset));
        if (toCopy <= 0)
        {
            return 0;
        }

        value.CopyTo((int)dataOffset, buffer, bufferOffset, toCopy);
        return toCopy;
    }

    /// <inheritdoc/>
    /// <remarks>Populates at minimum every column required by FR-9.3.10. No key information is available (FR-9.2.13, <c>KeyInfo</c>).</remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "The DataType column legitimately stores System.Type values per the ADO.NET GetSchemaTable contract (FR-9.3.10); this is not app-controlled reflection.")]
    public override DataTable GetSchemaTable()
    {
        var table = new DataTable("SchemaTable");
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("ColumnSize", typeof(int));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("DataTypeName", typeof(string));
        table.Columns.Add("ProviderType", typeof(int));
        table.Columns.Add("AllowDBNull", typeof(bool));
        table.Columns.Add("IsKey", typeof(bool));
        table.Columns.Add("IsUnique", typeof(bool));
        table.Columns.Add("IsReadOnly", typeof(bool));
        table.Columns.Add("BaseCatalogName", typeof(string));
        table.Columns.Add("BaseSchemaName", typeof(string));
        table.Columns.Add("BaseTableName", typeof(string));
        table.Columns.Add("BaseColumnName", typeof(string));

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var row = table.NewRow();
            row["ColumnName"] = column.Name;
            row["ColumnOrdinal"] = i;
            row["ColumnSize"] = column.TypeSignature.Length ?? column.TypeSignature.Precision ?? -1;
            row["NumericPrecision"] = column.TypeSignature.Precision ?? -1;
            row["NumericScale"] = column.TypeSignature.Scale ?? -1;
            row["DataType"] = column.ClrType;
            row["DataTypeName"] = column.TypeName;
            row["ProviderType"] = (int)TrinoDbTypeMapping.ToDbType(column.TypeSignature);
            row["AllowDBNull"] = true;
            row["IsKey"] = false;
            row["IsUnique"] = false;
            row["IsReadOnly"] = true;
            row["BaseCatalogName"] = string.Empty;
            row["BaseSchemaName"] = string.Empty;
            row["BaseTableName"] = string.Empty;
            row["BaseColumnName"] = column.Name;
            table.Rows.Add(row);
        }

        return table;
    }

    /// <inheritdoc/>
    public ReadOnlyCollection<DbColumn> GetColumnSchema()
    {
        var list = new List<DbColumn>(_columns.Count);
        for (var i = 0; i < _columns.Count; i++)
        {
            list.Add(new TrinoDbColumn(_columns[i], i));
        }

        return list.AsReadOnly();
    }

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc/>
    public override void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        // Non-blocking cancel signal (NFR-REL-2); the background pump completes on its own (FR-9.3.12).
        _resultSet.Dispose();
        _enumerator = null;
        _onClosed?.Invoke();
    }

    /// <inheritdoc/>
    public override Task CloseAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    private TrinoColumn Column(int ordinal) => _columns[ordinal];

    private TrinoRow RequireRow() =>
        _current ?? throw new InvalidOperationException("No current row: call Read() first, or the reader is past the last row (FR-9.3.13).");

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("The reader is closed.");
        }
    }

    private Dictionary<string, int> BuildOrdinalCache()
    {
        var cache = new Dictionary<string, int>(_columns.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _columns.Count; i++)
        {
            cache.TryAdd(_columns[i].Name, i);
        }

        return cache;
    }
}
