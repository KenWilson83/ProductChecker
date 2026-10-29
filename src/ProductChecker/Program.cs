using ProductChecker;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ProductPageClient>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("checks", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/check", async (CheckRequest request, ProductPageClient client, CancellationToken cancellationToken) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !ProductPageClient.IsAllowedUrl(uri))
        return Results.BadRequest(new { error = "Enter a public HTTPS product page URL using the standard port." });
    try
    {
        var html = await client.FetchAsync(uri, cancellationToken);
        return Results.Ok(AvailabilityReader.Read(html));
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or System.Net.Sockets.SocketException or System.Text.RegularExpressions.RegexMatchTimeoutException)
    {
        return Results.Ok(new AvailabilityResult("unknown", "The page could not be checked. It may block automated access, be unreachable, or use an unsupported URL.", []));
    }
}).RequireRateLimiting("checks");
app.Run();

record CheckRequest(string? Url);
