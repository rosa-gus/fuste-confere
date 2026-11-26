using System.Text.Json;
using System.Text.Json.Serialization;
using Fuste.Api.Updates;
using Fuste.Application.Abstractions;
using Fuste.Application.Agreements;
using Fuste.Application.Companies;
using Fuste.Application.Common;
using Fuste.Application.Database;
using Fuste.Application.Imports;
using Fuste.Domain.Imports;
using Fuste.Application.Reconciliation;
using Fuste.Domain.Reconciliation;
using Fuste.Infrastructure.Imports;
using Fuste.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? 500;
    context.ProblemDetails.Title = ApiProblems.Title(status);
    context.ProblemDetails.Detail ??= ApiProblems.Detail(status);
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "PUT", "DELETE").AllowAnyHeader();
    }
}));

var repository = new SqliteFusteRepository();
builder.Services.AddSingleton<ICompanyRepository>(repository);
builder.Services.AddSingleton<IAgreementRepository>(repository);
builder.Services.AddSingleton<IReconciliationRepository>(repository);
builder.Services.AddSingleton<IDatabaseRepository>(repository);
builder.Services.AddSingleton<IImportProfileRepository>(repository);
builder.Services.AddSingleton<CsvSourceFileReader>();
builder.Services.AddSingleton<ISourceFileReader>(services => services.GetRequiredService<CsvSourceFileReader>());
builder.Services.AddSingleton<IImportFileReader>(services => services.GetRequiredService<CsvSourceFileReader>());
builder.Services.AddScoped<ImportUseCases>();
builder.Services.AddScoped<ImportProfileUseCases>();
builder.Services.AddScoped<CompanyUseCases>();
builder.Services.AddScoped<AgreementUseCases>();
builder.Services.AddScoped<ReconciliationUseCases>();
builder.Services.AddScoped<DatabaseUseCases>();
builder.Services.AddSingleton(UpdateInstallation.FromAssembly(typeof(Program).Assembly,
    builder.Configuration.GetValue("FusteUpdates:Enabled", defaultValue: true)));
builder.Services.AddHttpClient(nameof(UpdateChecker), client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Fuste-UpdateChecker/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
});
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddHostedService(services => services.GetRequiredService<UpdateChecker>());

var app = builder.Build();
await repository.InitializeAsync(app.Lifetime.ApplicationStopping);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    app.Urls.Add("http://127.0.0.1:5080");
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/health")
        context.Response.Headers.ContentLanguage = "pt-BR";
    try { await next(context); }
    catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
    {
        await Results.Problem(ApiProblems.Detail(exception.StatusCode), statusCode: exception.StatusCode).ExecuteAsync(context);
    }
});
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "Fuste" }));
app.MapGet("/api/updates", (UpdateChecker updates, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    return Results.Ok(updates.Current);
});

app.MapPost("/api/database/reset", async (
    ResetDatabaseRequest request,
    DatabaseUseCases useCases,
    CancellationToken cancellationToken) =>
    ToDeletionHttpResult(await useCases.ResetAsync(request.Confirmation, cancellationToken)));

app.MapGet("/api/companies", async (CompanyUseCases useCases, CancellationToken cancellationToken) =>
    Results.Ok(await useCases.ListAsync(cancellationToken)));

app.MapPost("/api/companies", async (CreateCompanyRequest request, CompanyUseCases useCases, CancellationToken cancellationToken) =>
    ToHttpResult(await useCases.CreateAsync(request.Name ?? string.Empty, cancellationToken)));

app.MapDelete("/api/companies/{companyId:guid}", async (
    Guid companyId,
    CompanyUseCases useCases,
    CancellationToken cancellationToken) =>
    ToDeletionHttpResult(await useCases.DeleteAsync(companyId, cancellationToken)));

app.MapGet("/api/companies/{companyId:guid}/agreements", async (
    Guid companyId,
    AgreementUseCases useCases,
    CancellationToken cancellationToken) => Results.Ok(await useCases.ListAsync(companyId, cancellationToken)));

app.MapDelete("/api/companies/{companyId:guid}/agreements/{agreementId:guid}", async (
    Guid companyId,
    Guid agreementId,
    AgreementUseCases useCases,
    CancellationToken cancellationToken) =>
    ToDeletionHttpResult(await useCases.DeleteAsync(companyId, agreementId, cancellationToken)));

app.MapPost("/api/companies/{companyId:guid}/agreements", async (
    Guid companyId,
    CreateAgreementRequest request,
    AgreementUseCases useCases,
    CancellationToken cancellationToken) => ToHttpResult(await useCases.CreateAsync(
        companyId,
        request.EffectiveFrom,
        request.Percentage,
        cancellationToken)));

app.MapGet("/api/companies/{companyId:guid}/import-profiles", async (
    Guid companyId,
    ImportProfileUseCases useCases,
    CancellationToken cancellationToken) =>
    ToHttpResult(await useCases.ListAsync(companyId, cancellationToken)));

app.MapPost("/api/companies/{companyId:guid}/import-profiles", async Task<IResult> (
    Guid companyId,
    CreateImportProfileRequest request,
    ImportProfileUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (request.Configuration is null) return Results.Problem("Informe a configuração de importação.", statusCode: 400);
    return ToHttpResult(await useCases.CreateAsync(companyId, request.Name ?? string.Empty, request.Configuration, cancellationToken));
});

app.MapPut("/api/companies/{companyId:guid}/import-profiles/{profileId:guid}", async Task<IResult> (
    Guid companyId,
    Guid profileId,
    UpdateImportProfileRequest request,
    ImportProfileUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (request.Configuration is null) return Results.Problem("Informe a configuração de importação.", statusCode: 400);
    return ToHttpResult(await useCases.UpdateAsync(companyId, profileId, request.Name ?? string.Empty, request.Revision, request.Configuration, cancellationToken));
});

app.MapDelete("/api/companies/{companyId:guid}/import-profiles/{profileId:guid}", async (
    Guid companyId,
    Guid profileId,
    ImportProfileUseCases useCases,
    CancellationToken cancellationToken) =>
    ToDeletionHttpResult(await useCases.DeleteAsync(companyId, profileId, cancellationToken)));

app.MapPost("/api/companies/{companyId:guid}/imports/preview", async Task<IResult> (
    Guid companyId,
    HttpRequest request,
    ImportUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.Problem("Envie os dois arquivos CSV.", statusCode: 400);
    var form = await request.ReadFormAsync(cancellationToken);
    var salesFile = form.Files.GetFile("sales");
    var statementFile = form.Files.GetFile("statement");
    if (salesFile is null || statementFile is null)
        return Results.Problem("Selecione os arquivos de vendas e de comissões.", statusCode: 400);
    var (configuration, error) = ReadImportConfiguration(form);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    await using var salesStream = salesFile.OpenReadStream();
    await using var statementStream = statementFile.OpenReadStream();
    return ToHttpResult(await useCases.PreviewAsync(companyId, salesStream, statementStream, configuration, cancellationToken));
});

app.MapGet("/api/companies/{companyId:guid}/reconciliations", async (
    Guid companyId,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) => Results.Ok(await useCases.ListAsync(companyId, cancellationToken)));

app.MapDelete("/api/companies/{companyId:guid}/reconciliations/{reconciliationId:guid}", async (
    Guid companyId,
    Guid reconciliationId,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) =>
    ToDeletionHttpResult(await useCases.DeleteAsync(companyId, reconciliationId, cancellationToken)));

app.MapGet("/api/reconciliations/{reconciliationId:guid}", async (
    Guid reconciliationId,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) =>
{
    var reconciliation = await useCases.FindAsync(reconciliationId, cancellationToken);
    return reconciliation is null ? Results.NotFound() : Results.Ok(reconciliation);
});

app.MapGet("/api/reconciliations/{reconciliationId:guid}/export", async (
    Guid reconciliationId,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) =>
{
    var reconciliation = await useCases.FindAsync(reconciliationId, cancellationToken);
    return reconciliation is null
        ? Results.Problem("A conferência solicitada não foi encontrada.", statusCode: 404)
        : Results.Stream(
            stream => ReconciliationCsv.WriteAsync(stream, reconciliation, cancellationToken),
            "text/csv; charset=utf-8",
            $"conferencia-{reconciliation.Id:D}.csv");
});

app.MapPost("/api/companies/{companyId:guid}/reconciliations", async Task<IResult> (
    Guid companyId,
    HttpRequest request,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.Problem("Envie os dois arquivos CSV.", statusCode: 400);
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var salesFile = form.Files.GetFile("sales");
    var statementFile = form.Files.GetFile("statement");
    if (salesFile is null || statementFile is null)
    {
        return Results.Problem("Selecione os arquivos de vendas e de comissões.", statusCode: 400);
    }

    await using var salesStream = salesFile.OpenReadStream();
    await using var statementStream = statementFile.OpenReadStream();
    var (configuration, error) = ReadImportConfiguration(form);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    var (requestId, requestError) = ReadOptionalGuid(form, "requestId");
    var (reprocessOf, reprocessError) = ReadOptionalGuid(form, "reprocessOf");
    if (requestError is not null || reprocessError is not null)
        return Results.Problem(requestError ?? reprocessError, statusCode: 400);
    var result = await useCases.RunAsync(companyId, salesStream, statementStream, configuration, requestId, reprocessOf, cancellationToken);
    return ToHttpResult(result);
});

app.MapFallbackToFile("index.html").Add(endpoint =>
{
    var serveFile = endpoint.RequestDelegate!;
    endpoint.RequestDelegate = context => context.Request.Path.StartsWithSegments("/api")
        ? Results.Problem("O recurso solicitado não foi encontrado.", statusCode: 404).ExecuteAsync(context)
        : serveFile(context);
});
app.Run();

static IResult ToDeletionHttpResult(OperationResult<bool> result) => result.IsSuccess
    ? Results.NoContent()
    : Results.Problem(result.Error, statusCode: result.StatusCode);

static IResult ToHttpResult<T>(OperationResult<T> result)
{
    if (result.IsSuccess) return Results.Ok(result.Value);
    var extensions = new Dictionary<string, object?>();
    if (result.Issues is not null) extensions["issues"] = result.Issues;
    if (result.Conflict is not null)
    {
        extensions["code"] = result.Conflict.Code;
        if (result.Conflict.ExistingReconciliation is not null)
            extensions["existingReconciliation"] = result.Conflict.ExistingReconciliation;
    }
    return Results.Problem(result.Error, statusCode: result.StatusCode, extensions: extensions);
}

static (Guid? Value, string? Error) ReadOptionalGuid(IFormCollection form, string name)
{
    if (!form.TryGetValue(name, out var values)) return (null, null);
    return values.Count == 1 && Guid.TryParse(values[0], out var value) && value != Guid.Empty
        ? (value, null) : (null, $"Envie um único UUID válido e não vazio no campo '{name}'.");
}

static (ImportConfiguration? Configuration, string? Error) ReadImportConfiguration(IFormCollection form)
{
    if (!form.TryGetValue("configuration", out var values)) return (null, null);
    if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        return (null, "Envie uma única configuração de importação em um objeto JSON válido.");
    try
    {
        var configuration = JsonSerializer.Deserialize<ImportConfiguration>(values[0]!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return configuration is null
            ? (null, "Informe a configuração de importação.")
            : (configuration, null);
    }
    catch (JsonException)
    {
        return (null, "A configuração de importação não contém um JSON válido.");
    }
}

internal sealed record CreateCompanyRequest(string? Name);
internal sealed record ResetDatabaseRequest(string? Confirmation);
internal sealed record CreateAgreementRequest(DateOnly EffectiveFrom, decimal Percentage);

internal sealed record CreateImportProfileRequest(string? Name, ImportConfiguration? Configuration);
internal sealed record UpdateImportProfileRequest(string? Name, int Revision, ImportConfiguration? Configuration);
