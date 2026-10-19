using Microsoft.EntityFrameworkCore;
using Zapper.Application.AcmePay;
using Zapper.Application.Payments;
using Zapper.Application.Notifications;
using Zapper.Application.Reporting;
using Zapper.Api.Controllers.Payments;
using Zapper.Infrastructure.AcmePay;
using Zapper.Infrastructure.Persistence;
using Zapper.Api.Operations;
using Serilog;
using Serilog.Context;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Zapper.Api")
        .WriteTo.Console();

    var seqUrl = context.Configuration["Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        loggerConfiguration.WriteTo.Seq(seqUrl);
});
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(45));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
var applyMigrationsAtStartup = builder.Configuration.GetValue<bool?>("Database:ApplyMigrations")
    ?? builder.Environment.IsDevelopment();

var acmeMerchantId = builder.Configuration["AcmePay:MerchantId"]
    ?? (builder.Environment.IsDevelopment() ? "8b67bf88-bb8d-4eb5-adfc-0f5c97c7ed67" : throw new InvalidOperationException("AcmePay:MerchantId must be configured."));
if (!Guid.TryParse(acmeMerchantId, out _))
    throw new InvalidOperationException("AcmePay:MerchantId must be the provider-issued GUID.");
var acmeBaseUrl = builder.Configuration["AcmePay:BaseUrl"]
    ?? (builder.Environment.IsDevelopment() ? "http://localhost:5291" : throw new InvalidOperationException("AcmePay:BaseUrl must be configured."));
if (!Uri.TryCreate(acmeBaseUrl, UriKind.Absolute, out var acmeBaseUri) || acmeBaseUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("AcmePay:BaseUrl must be an absolute HTTP or HTTPS URL.");
var acmeApiKey = builder.Configuration["AcmePay:ApiKey"]
    ?? (builder.Environment.IsDevelopment() ? "local-acme-api-key" : throw new InvalidOperationException("AcmePay:ApiKey must be configured."));
var acmeTimeoutSeconds = builder.Configuration.GetValue<int?>("AcmePay:TimeoutSeconds") ?? 35;
if (acmeTimeoutSeconds is < 1 or > 35)
    throw new InvalidOperationException("AcmePay:TimeoutSeconds must be between 1 and 35 seconds.");
var paymentService = AllowedSetting(builder.Configuration, "AcmePay:PaymentService", "CardNotPresent", "CardNotPresent", "CardNotPresentRecurring", "AcmeEft", "GooglePay", "ApplePay");
var cardOnFileScenario = AllowedSetting(builder.Configuration, "AcmePay:CardOnFileScenario", "CIT", "CIT", "MIT");
var channel = AllowedSetting(builder.Configuration, "AcmePay:Channel", "App", "App", "Web", "POS");

builder.Services.AddZapperPersistence(builder.Configuration);
builder.Services.AddSingleton(new PaymentServiceOptions
{
    AcmeMerchantId = acmeMerchantId,
    FacilitatorName = builder.Configuration["AcmePay:FacilitatorName"]
        ?? (builder.Environment.IsDevelopment() ? "ZAP" : throw new InvalidOperationException("AcmePay:FacilitatorName must be configured.")),
    PaymentService = paymentService,
    CardOnFileScenario = cardOnFileScenario,
    Channel = channel
});
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<NotificationApplicationService>();
builder.Services.AddScoped<IDailySettlementSummaryService, DailySettlementSummaryService>();
builder.Services.AddSingleton(new NotificationOptions
{
    AdviceUsername = RequiredNotificationSetting(builder.Configuration, builder.Environment, "AcmeNotifications:AdviceUsername", "local-advice-user"),
    AdvicePassword = RequiredNotificationSetting(builder.Configuration, builder.Environment, "AcmeNotifications:AdvicePassword", "local-advice-password"),
    WebhookSecret = RequiredNotificationSetting(builder.Configuration, builder.Environment, "AcmeNotifications:WebhookSecret", "local-acme-webhook-secret"),
    MerchantId = acmeMerchantId
});
builder.Services.AddHttpClient<IAcmePayClient, AcmePayHttpClient>((_, client) =>
{
    client.BaseAddress = new Uri(acmeBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(acmeTimeoutSeconds);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 20
});
builder.Services.AddSingleton(new AcmePayClientOptions
{
    BaseUrl = acmeBaseUrl,
    ApiKey = acmeApiKey
});

builder.Services.AddControllers()
    .AddApplicationPart(typeof(PaymentsController).Assembly)
    .AddApplicationPart(typeof(Zapper.Api.Controllers.Reports.ReportsController).Assembly)
    .AddApplicationPart(typeof(Zapper.Api.Controllers.Notifications.NotificationsController).Assembly);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(() =>
{
    using (LogContext.PushProperty("LogCategory", "Application"))
        app.Logger.LogInformation("Zapper API is stopping; draining active requests.");
});

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = !string.IsNullOrWhiteSpace(supplied)
        && supplied.Length <= 64
        && supplied.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? supplied
            : context.TraceIdentifier;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (LogContext.PushProperty("CorrelationId", correlationId))
        await next();
});
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
    {
        var path = httpContext.Request.Path;
        var isHealthCheck = path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);
        if (isHealthCheck) return LogEventLevel.Verbose;
        if (exception is not null) return LogEventLevel.Error;

        if (httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;

        return LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Response.Headers["X-Correlation-ID"].ToString());
        var path = httpContext.Request.Path;
        var category = path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase) ? "HealthCheck"
            : path.StartsWithSegments("/api/payments") ? "Payment"
            : path.StartsWithSegments("/api/notifications") ? "Notification"
            : path.StartsWithSegments("/api/reports") ? "Report"
            : "Api";
        diagnosticContext.Set("LogCategory", category);
    };
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

if (applyMigrationsAtStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ZapperDbContext>();
    await dbContext.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
}
else
{
    using (LogContext.PushProperty("LogCategory", "Application"))
        app.Logger.LogInformation("Automatic database migrations are disabled; apply the EF Core migration bundle or CLI as a controlled release step.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();

static string RequiredNotificationSetting(IConfiguration configuration, IHostEnvironment environment, string key, string developmentDefault) =>
    configuration[key] ?? (environment.IsDevelopment() ? developmentDefault : throw new InvalidOperationException($"{key} must be configured."));

static string AllowedSetting(IConfiguration configuration, string key, string defaultValue, params string[] allowedValues)
{
    var value = configuration[key] ?? defaultValue;
    return allowedValues.Contains(value, StringComparer.Ordinal)
        ? value
        : throw new InvalidOperationException($"{key} must be one of: {string.Join(", ", allowedValues)}.");
}