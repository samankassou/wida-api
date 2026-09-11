using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
