using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ShopLab.Api.Diagnostics;

public sealed class DiagnosticsOptions
{
    /// <summary>Commands slower than this are logged as warnings.</summary>
    public double SlowQueryThresholdMs { get; set; } = 200;

    /// <summary>How many requests GET /api/diagnostics/requests keeps.</summary>
    public int RecentRequestsToKeep { get; set; } = 50;
}

public sealed record CapturedCommand(
    string Sql,
    string Parameters,
    string Kind,
    double DurationMs,
    DateTimeOffset ExecutedAt);

/// <summary>
/// Scoped (one per HTTP request): collects every DbCommand EF Core sends while
/// handling the request. Filled by <see cref="SqlCaptureInterceptor"/>.
/// </summary>
public sealed class SqlCapture
{
    private readonly List<CapturedCommand> _commands = [];
    private readonly Lock _lock = new();

    public void Add(CapturedCommand command)
    {
        lock (_lock) _commands.Add(command);
    }

    public int Count
    {
        get { lock (_lock) return _commands.Count; }
    }

    public double TotalDurationMs
    {
        get { lock (_lock) return _commands.Sum(c => c.DurationMs); }
    }

    public IReadOnlyList<CapturedCommand> Snapshot()
    {
        lock (_lock) return _commands.ToArray();
    }
}

public sealed record RequestSqlLog(
    string Method,
    string Path,
    int StatusCode,
    DateTimeOffset At,
    int CommandCount,
    double SqlDurationMs,
    IReadOnlyList<CapturedCommand> Commands);

/// <summary>Singleton ring buffer of the most recent requests and the SQL they ran.</summary>
public sealed class RecentRequestStore(IOptions<DiagnosticsOptions> options)
{
    private readonly ConcurrentQueue<RequestSqlLog> _items = new();

    public void Add(RequestSqlLog log)
    {
        _items.Enqueue(log);
        while (_items.Count > options.Value.RecentRequestsToKeep && _items.TryDequeue(out _)) { }
    }

    public IReadOnlyList<RequestSqlLog> Latest() => _items.Reverse().ToArray();

    public void Clear() => _items.Clear();
}
