using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Wida.Dal;
using Wida.Bll;
using Wida.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Explicit operator recovery after restoring broker data.
// Re-publish a known ID only; this never scans PostgreSQL or creates a second run.
if (builder.Configuration["republish-analysis-run"] is { } replayId)
{
    if (!Guid.TryParse(replayId, out var runId))
        throw new ArgumentException("--republish-analysis-run requires a processing run GUID.");
    await new Wida.Api.Processing.RabbitMqTransport(builder.Configuration).PublishAsync(runId);
    Console.WriteLine($"RabbitMQ confirmed analysis run {runId}.");
    return;
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection using user secrets or ConnectionStrings__DefaultConnection.");
}

builder.Services.AddWidaAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddControllers(options => options.Filters.Add<ValidateSessionAntiforgeryFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

// Add services to the container.
builder.Services
    .AddDal(builder.Configuration)
    .AddBll();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpClient<Wida.Dal.Services.Interfaces.IQueuedDocumentAnalyzer, Wida.Dal.Services.QueuedAzureDocumentAnalyzer>(client =>
    client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<Wida.Api.Processing.RabbitMqTransport>();
builder.Services.AddSingleton<Wida.Bll.Services.Interfaces.IAnalysisJobPublisher>(services =>
    services.GetRequiredService<Wida.Api.Processing.RabbitMqTransport>());
if (builder.Configuration.GetValue("ProcessingQueue:Enabled", true))
    builder.Services.AddHostedService<Wida.Api.Processing.InvoiceQueueWorker>();

builder.Services.AddHostedService<Wida.Api.Processing.OriginalRetentionWorker>();
builder.Services.AddScoped<TrialChallenge>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = ClientRateLimits.Create();
});
var app = builder.Build();

// Roles can only be assigned by a trusted operator, never by a sign-up payload.
if (builder.Configuration["set-user-role"] is { } roleEmail)
{
    var roleName = builder.Configuration["role"];
    if (roleName is not ("User" or "Admin")) throw new ArgumentException("--role must be User or Admin.");
    var role = Enum.Parse<Wida.Dal.Enums.UserRole>(roleName);
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Wida.Dal.Persistence.WidaDbContext>();
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)");
    var normalized = roleEmail.Trim().ToLowerInvariant();
    var users = await db.Users.Where(x => x.Email == normalized).ToListAsync();
    if (users.Count != 1) throw new ArgumentException("Expected one registered Google account for this email. Sign in first; resolve ambiguous identities before assigning a role.");
    users[0].Role = role;
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"Role {role} assigned to {normalized}; effective on the next request.");
    return;
}

// Operator-only local command; no public endpoint can grant itself credit.
if (builder.Configuration["grant-credit-user"] is { } creditUser)
{
    if (!Guid.TryParse(creditUser, out var userId)
        || !int.TryParse(builder.Configuration["grant-credit-pages"], out var pages) || pages is < 1 or > 400)
        throw new ArgumentException("Supply --grant-credit-user UUID --grant-credit-pages 1..400.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Wida.Dal.Persistence.WidaDbContext>();
    var affected = await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(update => update
        .SetProperty(x => x.AnalysisPagesGranted, x => x.AnalysisPagesGranted + pages)
        .SetProperty(x => x.CreditRequestedAt, (DateTime?)null));
    if (affected != 1) throw new ArgumentException("Unknown user.");
    Console.WriteLine($"Granted {pages} pages to {userId}. The global monthly limit remains unchanged.");
    return;
}
if (builder.Configuration.GetValue("list-credit-requests", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Wida.Dal.Persistence.WidaDbContext>();
    foreach (var user in await db.Users.AsNoTracking().Where(x => x.CreditRequestedAt != null).OrderBy(x => x.CreditRequestedAt).ToListAsync())
        Console.WriteLine($"{user.Id} | {user.Email} | remaining: {user.AnalysisPagesGranted - user.AnalysisPagesUsed} | {user.CreditRequestedAt:O}");
    return;
}

var trustedClientIp = new TrustedClientIp(builder.Configuration, builder.Environment);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// The frontend is the public HTTPS origin. The private API may use HTTP behind it.
// Authentication callback URLs and cookie security use the configured public origin.
var publicScheme = new Uri(app.Services.GetRequiredService<PilotAccess>().PublicOrigin).Scheme;
app.Use(async (context, next) =>
{
    // Use server configuration, never caller-supplied forwarded headers.
    context.Request.Scheme = publicScheme;
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Wida.Bll.Exceptions.TrialLimitException ex)
    {
        context.Response.StatusCode = ex.Status;
        await context.Response.WriteAsJsonAsync(new { title = "Limite de la bêta", detail = ex.Message });
    }
});
app.Use(trustedClientIp.InvokeAsync);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var path = (context.Request.Path.Value ?? "").TrimEnd('/');
    if (HttpMethods.IsPost(context.Request.Method)
        && (path.Equals("/api/documents", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/invoice", StringComparison.OrdinalIgnoreCase))
        && !context.RequestServices.GetRequiredService<TrialChallenge>().IsVerified(context))
        throw new Wida.Bll.Exceptions.TrialLimitException("Complétez la vérification anti-robot dans le bandeau de votre espace, puis réessayez.", 403);
    await next(context);
});

app.MapControllers();

app.Run();

public partial class Program { }
