using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Agreements;
using ProjectConferidor.Application.Companies;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Application.Database;
using ProjectConferidor.Application.Imports;
using ProjectConferidor.Domain.Imports;
using ProjectConferidor.Application.Reconciliation;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Infrastructure.Imports;
using ProjectConferidor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
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

var repository = new SqliteConferidorRepository();
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

var app = builder.Build();
await repository.InitializeAsync(app.Lifetime.ApplicationStopping);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    app.Urls.Add("http://127.0.0.1:5080");
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "Project Conferidor" }));

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
    if (request.Configuration is null) return Results.Problem("Import configuration is required.", statusCode: 400);
    return ToHttpResult(await useCases.CreateAsync(companyId, request.Name ?? string.Empty, request.Configuration, cancellationToken));
});

app.MapPut("/api/companies/{companyId:guid}/import-profiles/{profileId:guid}", async Task<IResult> (
    Guid companyId,
    Guid profileId,
    UpdateImportProfileRequest request,
    ImportProfileUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (request.Configuration is null) return Results.Problem("Import configuration is required.", statusCode: 400);
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
        return Results.Problem("Upload the files as multipart/form-data.", statusCode: 400);
    var form = await request.ReadFormAsync(cancellationToken);
    var salesFile = form.Files.GetFile("sales");
    var statementFile = form.Files.GetFile("statement");
    if (salesFile is null || statementFile is null)
        return Results.Problem("Provide files in the 'sales' and 'statement' fields.", statusCode: 400);
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

app.MapPost("/api/companies/{companyId:guid}/reconciliations", async Task<IResult> (
    Guid companyId,
    HttpRequest request,
    ReconciliationUseCases useCases,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.Problem("Upload the files as multipart/form-data.", statusCode: 400);
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var salesFile = form.Files.GetFile("sales");
    var statementFile = form.Files.GetFile("statement");
    if (salesFile is null || statementFile is null)
    {
        return Results.Problem("Provide files in the 'sales' and 'statement' fields.", statusCode: 400);
    }

    await using var salesStream = salesFile.OpenReadStream();
    await using var statementStream = statementFile.OpenReadStream();
    var (configuration, error) = ReadImportConfiguration(form);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    var result = configuration is null
        ? await useCases.RunAsync(companyId, salesStream, statementStream, cancellationToken)
        : await useCases.RunAsync(companyId, salesStream, statementStream, configuration, cancellationToken);
    return ToHttpResult(result);
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult ToDeletionHttpResult(OperationResult<bool> result) => result.IsSuccess
    ? Results.NoContent()
    : Results.Problem(result.Error, statusCode: result.StatusCode);

static IResult ToHttpResult<T>(OperationResult<T> result) => result.IsSuccess
    ? Results.Ok(result.Value)
    : Results.Problem(result.Error, statusCode: result.StatusCode,
        extensions: result.Issues is null ? null : new Dictionary<string, object?> { ["issues"] = result.Issues });

static (ImportConfiguration? Configuration, string? Error) ReadImportConfiguration(IFormCollection form)
{
    if (!form.TryGetValue("configuration", out var values)) return (null, null);
    if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        return (null, "Provide a single valid import configuration JSON object.");
    try
    {
        var configuration = JsonSerializer.Deserialize<ImportConfiguration>(values[0]!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return configuration is null
            ? (null, "Import configuration is required.")
            : (configuration, null);
    }
    catch (JsonException)
    {
        return (null, "Import configuration is not valid JSON.");
    }
}

internal sealed record CreateCompanyRequest(string? Name);
internal sealed record ResetDatabaseRequest(string? Confirmation);
internal sealed record CreateAgreementRequest(DateOnly EffectiveFrom, decimal Percentage);

internal sealed record CreateImportProfileRequest(string? Name, ImportConfiguration? Configuration);
internal sealed record UpdateImportProfileRequest(string? Name, int Revision, ImportConfiguration? Configuration);
