namespace ShopLab.Api.Diagnostics;

/// <summary>
/// Adds X-Sql-Count / X-Sql-Duration-Ms headers to every response and stores the
/// request's SQL in <see cref="RecentRequestStore"/> (GET /api/diagnostics/requests).
/// </summary>
public sealed class SqlCaptureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SqlCapture capture, RecentRequestStore store)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Sql-Count"] = capture.Count.ToString();
            context.Response.Headers["X-Sql-Duration-Ms"] = capture.TotalDurationMs.ToString("F1");
            return Task.CompletedTask;
        });

        await next(context);

        if (capture.Count > 0 && !context.Request.Path.StartsWithSegments("/api/diagnostics"))
        {
            var commands = capture.Snapshot();
            store.Add(new RequestSqlLog(
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Response.StatusCode,
                DateTimeOffset.UtcNow,
                commands.Count,
                Math.Round(commands.Sum(c => c.DurationMs), 2),
                commands));
        }
    }
}
