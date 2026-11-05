using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ShopLab.Api.Diagnostics;

/// <summary>
/// An EF Core command interceptor: sees every SQL command right after it runs.
/// - records it into the current request's <see cref="SqlCapture"/>
/// - logs a warning for slow commands
///
/// Note: for readers, Duration is the time until the first result is available,
/// not the time to stream all rows to the client.
/// </summary>
public sealed class SqlCaptureInterceptor(
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<DiagnosticsOptions> options,
    ILogger<SqlCaptureInterceptor> logger) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData, "Reader");
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "Reader");
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData, "Scalar");
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "Scalar");
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData, "NonQuery");
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "NonQuery");
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command, CommandExecutedEventData eventData, string kind)
    {
        var ms = eventData.Duration.TotalMilliseconds;

        // Only inside an HTTP request (not during migrations/seeding at startup).
        var capture = httpContextAccessor.HttpContext?.RequestServices.GetService<SqlCapture>();
        if (capture is not null)
        {
            var parameters = string.Join(", ",
                command.Parameters.Cast<DbParameter>().Select(p => $"{p.ParameterName}={FormatValue(p.Value)}"));
            capture.Add(new CapturedCommand(command.CommandText, parameters, kind, Math.Round(ms, 2), DateTimeOffset.UtcNow));
        }

        if (ms >= options.CurrentValue.SlowQueryThresholdMs)
        {
            logger.LogWarning("SLOW SQL ({Duration:N0} ms, {Kind}):{NewLine}{Sql}",
                ms, kind, Environment.NewLine, command.CommandText);
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => $"'{s}'",
        DateTime d => $"'{d:O}'",
        _ => value.ToString() ?? ""
    };
}
