using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlExpert.Core.SqlEditor;

/// <summary>
/// One SQL Editor's server session: a dedicated connection kept open between executions (so session state,
/// temp tables and an explicit transaction survive), GO batches, capped result sets, PRINT/InfoMessage,
/// rows affected, cancel (SqlCommand.Cancel) and classic-style transaction control.
/// <para>Autocommit (default): every statement commits on its own. Explicit mode: a transaction is opened
/// before the next execution (when none is open) and stays open until <see cref="CommitAsync"/> / <see cref="RollbackAsync"/>.</para>
/// Not thread-safe for concurrent executions: one execution at a time (guarded).
/// </summary>
public sealed class SqlEditorSession : IDisposable
{
    private readonly Func<CancellationToken, Task<SqlConnection>> _open;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sinkLock = new();
    private SqlConnection? _connection;
    private List<SqlMessage>? _sink;
    private int _sinkStartLine = 1;
    private int _sinkBatchStart = 1;
    private bool _disposed;

    /// <param name="openConnection">Opens a new connection for this editor (e.g. ISqlConnectionFactory.CreateOpenConnectionAsync).</param>
    /// <param name="database">Database to switch to after opening (null/empty = keep the login default).</param>
    public SqlEditorSession(Func<CancellationToken, Task<SqlConnection>> openConnection, string? database)
    {
        _open = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        InitialDatabase = database;
        CurrentDatabase = database;
    }

    public string? InitialDatabase { get; }

    /// <summary>Database of the session after the last run (changes with USE).</summary>
    public string? CurrentDatabase { get; private set; }

    /// <summary>True (default): no client-managed transaction. False: one stays open until Commit/Rollback.</summary>
    public bool Autocommit { get; set; } = true;

    /// <summary>@@TRANCOUNT read after the last run / commit / rollback.</summary>
    public int TranCount { get; private set; }

    public bool InTransaction => TranCount > 0;

    public bool IsConnected => _connection?.State == ConnectionState.Open;

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<SqlExecutionResult> ExecuteAsync(SqlExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            throw new InvalidOperationException("Já existe uma execução em andamento neste editor.");

        var result = new SqlExecutionResult();
        var sw = Stopwatch.StartNew();
        try
        {
            var batches = SqlBatchParser.Split(request.Sql);
            result.BatchCount = batches.Count;
            SqlConnection conn;
            try
            {
                conn = await EnsureConnectionAsync(result, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.Messages.Add(new SqlMessage(SqlMessageKind.Status, "Execução cancelada pelo usuário."));
                return result;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                result.Messages.Add(new SqlMessage(SqlMessageKind.Error, "Falha ao conectar: " + ex.Message));
                return result;
            }

            await Task.Run(() => RunBatches(conn, batches, request, result, cancellationToken), CancellationToken.None).ConfigureAwait(false);
            if (request.DescribeResults && !result.Cancelled)
                await DescribeResultsAsync(conn, result, cancellationToken).ConfigureAwait(false);
            await RefreshStateAsync(result).ConfigureAwait(false);
            return result;
        }
        finally
        {
            sw.Stop();
            result.Elapsed = sw.Elapsed;
            _gate.Release();
        }
    }

    private async Task<SqlConnection> EnsureConnectionAsync(SqlExecutionResult result, CancellationToken ct)
    {
        if (_connection is { State: ConnectionState.Open } open) return open;
        if (_connection is not null)
        {
            var hadTransaction = TranCount > 0;
            DropConnection();
            if (hadTransaction)
                result.Messages.Add(new SqlMessage(SqlMessageKind.Status, "A conexão anterior foi perdida; a transação aberta foi descartada pelo servidor."));
        }

        var conn = await _open(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(InitialDatabase) && !string.Equals(conn.Database, InitialDatabase, StringComparison.OrdinalIgnoreCase))
                conn.ChangeDatabase(InitialDatabase);
            conn.FireInfoMessageEventOnUserErrors = false;
            conn.InfoMessage += OnInfoMessage;
            _connection = conn;
            CurrentDatabase = conn.Database;
            TranCount = 0;
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private void RunBatches(SqlConnection conn, IReadOnlyList<SqlBatch> batches, SqlExecutionRequest request, SqlExecutionResult result, CancellationToken ct)
    {
        lock (_sinkLock) { _sink = result.Messages; _sinkStartLine = request.StartLine; _sinkBatchStart = 1; }
        var capturing = false;
        try
        {
            if (request.CapturePlan && batches.Count > 0)
                capturing = TryEnablePlanCapture(conn, result);

            if (!Autocommit && batches.Count > 0)
            {
                using var begin = conn.CreateCommand();
                begin.CommandText = "IF @@TRANCOUNT = 0 BEGIN TRANSACTION;";
                begin.ExecuteNonQuery();
            }

            var batchNumber = 0;
            foreach (var batch in batches)
            {
                batchNumber++;
                lock (_sinkLock) _sinkBatchStart = batch.StartLine;
                for (var rep = 0; rep < batch.RepeatCount; rep++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        MarkCancelled(result);
                        return;
                    }

                    var ok = RunOne(conn, batch, batchNumber, request, result, ct);
                    if (result.Cancelled) return;
                    if (!ok && request.StopOnError) return;
                }
                result.BatchesExecuted = batchNumber;
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) MarkCancelled(result);
            else result.Messages.Add(new SqlMessage(SqlMessageKind.Error, ex.Message));
        }
        finally
        {
            if (capturing) DisablePlanCapture(conn);
            lock (_sinkLock) _sink = null;
        }
    }

    /// <summary>
    /// SET STATISTICS XML ON for the user's batches (actual plans for the Plan / Performance info messages). Only when
    /// the login has SHOWPLAN in the current database: without it every statement would fail.
    /// </summary>
    private static bool TryEnablePlanCapture(SqlConnection conn, SqlExecutionResult result)
    {
        try
        {
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SHOWPLAN');";
                check.CommandTimeout = 15;
                if (Convert.ToInt32(check.ExecuteScalar() ?? 0) != 1)
                {
                    result.PlanNote = "Plano indisponível: o login não tem a permissão SHOWPLAN neste database.";
                    return false;
                }
            }
            using var on = conn.CreateCommand();
            on.CommandText = "SET STATISTICS XML ON;";
            on.CommandTimeout = 15;
            on.ExecuteNonQuery();
            result.PlanCaptured = true;
            return true;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            result.PlanNote = "Plano indisponível: " + ex.Message;
            return false;
        }
    }

    private static void DisablePlanCapture(SqlConnection conn)
    {
        if (conn.State != ConnectionState.Open) return;
        try
        {
            using var off = conn.CreateCommand();
            off.CommandText = "SET STATISTICS XML OFF;";
            off.CommandTimeout = 15;
            off.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException) { /* broken connection: dropped later */ }
    }

    /// <summary>Most result sets described per execution (sp_describe_first_result_set is one round trip each).</summary>
    public const int MaxDescribedResults = 20;

    /// <summary>Source table / key columns of each result set whose statement is known (editable results).</summary>
    private async Task DescribeResultsAsync(SqlConnection conn, SqlExecutionResult result, CancellationToken ct)
    {
        var n = 0;
        foreach (var set in result.ResultSets)
        {
            if (set.StatementText is not { } text) continue;
            if (++n > MaxDescribedResults || conn.State != ConnectionState.Open || ct.IsCancellationRequested) break;
            try
            {
                set.Described = await ResultSourceAnalyzer.DescribeAsync(conn, text, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                set.SourceNote = "Somente leitura: não foi possível identificar a origem do resultado (" + ex.Message + ").";
            }
        }
    }

    /// <summary>Links the result sets one batch produced to its SELECT statements (only when the count matches).</summary>
    private static void MapStatements(string batchText, SqlExecutionResult result, int firstSet)
    {
        var added = result.ResultSets.Count - firstSet;
        if (added <= 0) return;
        IReadOnlyList<string?>? texts;
        try { texts = ResultSourceAnalyzer.ResultStatements(batchText); }
        catch (Exception) { texts = null; }
        if (texts is null || texts.Count != added) return;
        for (var i = 0; i < added; i++) result.ResultSets[firstSet + i].StatementText = texts[i];
    }

    /// <summary>Runs one batch; false when it raised an error.</summary>
    private bool RunOne(SqlConnection conn, SqlBatch batch, int batchNumber, SqlExecutionRequest request, SqlExecutionResult result, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = batch.Text;
        cmd.CommandTimeout = Math.Max(0, request.CommandTimeoutSeconds);
        // Each plan result set (SET STATISTICS XML) ends with its own "1 row" completion: skipped, the user never sees it.
        var planCompletions = 0;
        cmd.StatementCompleted += (_, e) =>
        {
            if (planCompletions > 0 && e.RecordCount == 1) { planCompletions--; return; }
            AddMessage(new SqlMessage(SqlMessageKind.RowsAffected, $"({e.RecordCount} {(e.RecordCount == 1 ? "row" : "rows")} affected)"));
        };

        using var registration = ct.Register(() =>
        {
            try { cmd.Cancel(); } catch { /* best effort */ }
        });

        try
        {
            var firstSet = result.ResultSets.Count;
            using var reader = cmd.ExecuteReader();
            do
            {
                planCompletions = 0; // the previous plan set is fully read by now (SET NOCOUNT ON sends no count)
                if (reader.FieldCount <= 0) continue;
                if (request.CapturePlan && reader.FieldCount == 1
                    && string.Equals(reader.GetName(0), ShowplanSummary.ShowplanColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    // Actual plan of the statement just run: kept for the Messages panel, never shown as a result.
                    planCompletions++;
                    while (reader.Read())
                        if (!reader.IsDBNull(0) && reader.GetValue(0)?.ToString() is { } xml) result.Showplans.Add(xml);
                    continue;
                }
                result.ResultSets.Add(ReadResultSet(reader, request.MaxRows, batchNumber, ct));
            } while (reader.NextResult());
            reader.Close();
            if (request.DescribeResults && batch.RepeatCount == 1)
                MapStatements(batch.Text, result, firstSet);
            // RecordsAffected counts INSERT/UPDATE/DELETE/MERGE only (SELECT row counts are not included).
            if (reader.RecordsAffected >= 0)
                result.RowsAffected = Math.Max(0, result.RowsAffected) + reader.RecordsAffected;
            return true;
        }
        catch (SqlException ex)
        {
            if (ct.IsCancellationRequested)
            {
                MarkCancelled(result);
                return false;
            }

            var anyError = false;
            foreach (SqlError e in ex.Errors)
            {
                if (e.Class <= 10)
                {
                    AddMessage(new SqlMessage(SqlMessageKind.Info, e.Message));
                    continue;
                }
                anyError = true;
                var line = SqlErrorLineMapper.ToEditorLine(request.StartLine, batch.StartLine, e.LineNumber, e.Procedure);
                var header = SqlErrorLineMapper.FormatHeader(e.Number, e.Class, e.State, line, e.Procedure, e.LineNumber);
                int? textLine = e.LineNumber > 0 ? SqlErrorLineMapper.ToEditorLine(request.StartLine, batch.StartLine, e.LineNumber) : null;
                AddMessage(new SqlMessage(SqlMessageKind.Error, header + Environment.NewLine + e.Message, line, e.Number, e.Class, textLine));
            }
            if (!anyError)
            {
                AddMessage(new SqlMessage(SqlMessageKind.Error, ex.Message,
                    SqlErrorLineMapper.ToEditorLine(request.StartLine, batch.StartLine, 0)));
            }
            return false;
        }
        catch (InvalidOperationException) when (ct.IsCancellationRequested)
        {
            MarkCancelled(result);
            return false;
        }
    }

    private static SqlResultSet ReadResultSet(SqlDataReader reader, int maxRows, int batchNumber, CancellationToken ct)
    {
        var n = reader.FieldCount;
        var columns = new List<SqlResultColumn>(n);
        System.Collections.ObjectModel.ReadOnlyCollection<System.Data.Common.DbColumn> schema;
        try { schema = reader.GetColumnSchema(); }
        catch (Exception) { schema = new System.Collections.ObjectModel.ReadOnlyCollection<System.Data.Common.DbColumn>(Array.Empty<System.Data.Common.DbColumn>()); }
        for (var i = 0; i < n; i++)
        {
            Type type;
            try { type = reader.GetFieldType(i) ?? typeof(object); }
            catch (Exception) { type = typeof(object); }
            string typeName;
            try { typeName = reader.GetDataTypeName(i); }
            catch (Exception) { typeName = "?"; }
            var name = reader.GetName(i);
            columns.Add(new SqlResultColumn(string.IsNullOrEmpty(name) ? "(No column name)" : name, typeName, type)
            {
                Size = i < schema.Count ? schema[i].ColumnSize : null,
                Precision = i < schema.Count ? schema[i].NumericPrecision : null,
                Scale = i < schema.Count ? schema[i].NumericScale : null,
                AllowDbNull = i < schema.Count ? schema[i].AllowDBNull : null
            });
        }

        var set = new SqlResultSet { Columns = columns, BatchNumber = batchNumber };
        while (reader.Read())
        {
            set.TotalRowCount++;
            if (maxRows > 0 && set.Rows.Count >= maxRows)
            {
                // Keep counting (the rest of the rows must be read anyway to reach the next result set).
                if ((set.TotalRowCount & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
                continue;
            }
            var row = new object?[n];
            for (var i = 0; i < n; i++)
                row[i] = ReadValue(reader, i);
            set.Rows.Add(row);
        }
        return set;
    }

    private static object? ReadValue(SqlDataReader reader, int i)
    {
        try
        {
            if (reader.IsDBNull(i)) return null;
            return reader.GetValue(i);
        }
        catch (Exception)
        {
            // CLR UDTs (geography, hierarchyid…) need their assembly: show the raw bytes instead.
            try { return reader.GetSqlBytes(i).Value; }
            catch (Exception) { return "(valor não suportado)"; }
        }
    }

    private static void MarkCancelled(SqlExecutionResult result)
    {
        if (result.Cancelled) return;
        result.Cancelled = true;
        result.Messages.Add(new SqlMessage(SqlMessageKind.Status, "Execução cancelada pelo usuário."));
    }

    private void OnInfoMessage(object sender, SqlInfoMessageEventArgs e)
    {
        foreach (SqlError err in e.Errors)
        {
            if (err.Class > 10)
            {
                int start, batchStart;
                lock (_sinkLock) { start = _sinkStartLine; batchStart = _sinkBatchStart; }
                var line = SqlErrorLineMapper.ToEditorLine(start, batchStart, err.LineNumber, err.Procedure);
                int? textLine = err.LineNumber > 0 ? SqlErrorLineMapper.ToEditorLine(start, batchStart, err.LineNumber) : null;
                AddMessage(new SqlMessage(SqlMessageKind.Error,
                    SqlErrorLineMapper.FormatHeader(err.Number, err.Class, err.State, line, err.Procedure, err.LineNumber) + Environment.NewLine + err.Message,
                    line, err.Number, err.Class, textLine));
            }
            else
            {
                AddMessage(new SqlMessage(SqlMessageKind.Info, err.Message));
            }
        }
    }

    private void AddMessage(SqlMessage message)
    {
        lock (_sinkLock) _sink?.Add(message);
    }

    /// <summary>Reads @@TRANCOUNT / DB_NAME() after a run; a broken connection is dropped (its transaction is gone).</summary>
    private async Task RefreshStateAsync(SqlExecutionResult result)
    {
        var conn = _connection;
        if (conn is null) return;
        if (conn.State != ConnectionState.Open)
        {
            var had = TranCount > 0;
            DropConnection();
            result.TranCount = 0;
            if (had) result.Messages.Add(new SqlMessage(SqlMessageKind.Status, "A conexão foi encerrada; a transação aberta foi descartada pelo servidor."));
            return;
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@TRANCOUNT, DB_NAME();";
            cmd.CommandTimeout = 15;
            await using var r = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            if (await r.ReadAsync().ConfigureAwait(false))
            {
                TranCount = r.GetInt32(0);
                CurrentDatabase = r.IsDBNull(1) ? CurrentDatabase : r.GetString(1);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            if (conn.State != ConnectionState.Open)
            {
                DropConnection();
                TranCount = 0;
            }
        }
        result.TranCount = TranCount;
        result.CurrentDatabase = CurrentDatabase;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on this session's own connection (opened when needed), one call at a time with the
    /// executions. Object editors read their metadata here: while this session holds an open transaction, another
    /// connection would block on the uncommitted DDL, this one sees it.
    /// </summary>
    public async Task<T> RunOnConnectionAsync<T>(Func<SqlConnection, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            throw new InvalidOperationException("Já existe uma execução em andamento neste editor.");
        try
        {
            var conn = await EnsureConnectionAsync(new SqlExecutionResult(), cancellationToken).ConfigureAwait(false);
            try
            {
                return await work(conn, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await RefreshStateAsync(new SqlExecutionResult()).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Commits every open level (WHILE @@TRANCOUNT &gt; 0 COMMIT). Returns the messages of the operation.</summary>
    public Task<SqlExecutionResult> CommitAsync() => RunControlAsync("WHILE @@TRANCOUNT > 0 COMMIT TRANSACTION;", "Transação confirmada (COMMIT).");

    /// <summary>Rolls back the whole open transaction.</summary>
    public Task<SqlExecutionResult> RollbackAsync() => RunControlAsync("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", "Transação desfeita (ROLLBACK).");

    private async Task<SqlExecutionResult> RunControlAsync(string sql, string done)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            throw new InvalidOperationException("Aguarde o fim da execução em andamento.");
        var result = new SqlExecutionResult();
        var sw = Stopwatch.StartNew();
        try
        {
            if (_connection is not { State: ConnectionState.Open } conn)
            {
                TranCount = 0;
                result.Messages.Add(new SqlMessage(SqlMessageKind.Status, "Nenhuma transação aberta."));
                return result;
            }
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                result.Messages.Add(new SqlMessage(SqlMessageKind.Status, done));
            }
            catch (SqlException ex)
            {
                foreach (SqlError e in ex.Errors)
                    result.Messages.Add(new SqlMessage(e.Class > 10 ? SqlMessageKind.Error : SqlMessageKind.Info,
                        e.Class > 10 ? $"Msg {e.Number}, Level {e.Class}, State {e.State}{Environment.NewLine}{e.Message}" : e.Message, null, e.Number, e.Class));
            }
            await RefreshStateAsync(result).ConfigureAwait(false);
            return result;
        }
        finally
        {
            sw.Stop();
            result.Elapsed = sw.Elapsed;
            _gate.Release();
        }
    }

    private void DropConnection()
    {
        var c = _connection;
        _connection = null;
        if (c is null) return;
        c.InfoMessage -= OnInfoMessage;
        try { c.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>Closes the connection. An open transaction is rolled back by the server.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection is { State: ConnectionState.Open } conn && TranCount > 0)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;";
                cmd.CommandTimeout = 10;
                cmd.ExecuteNonQuery();
            }
            catch { /* the pool reset rolls it back anyway */ }
        }
        DropConnection();
        TranCount = 0;
    }
}
