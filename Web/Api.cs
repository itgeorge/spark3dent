using System.Text.Json;
using Accounting;
using Configuration;
using Database;
using Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Utilities;

namespace Web;

public static class Api
{
    private const int ImportMaxFileCount = 500;
    private const int ImportMaxFileSizeBytes = 1024 * 1024; // 1MB
    private const int DevFriendlyErrorStatusCode = 422; // Unprocessable Entity — client error with dev-friendly message

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapRoutes(WebApplication app)
    {
        var invMgmt = app.Services.GetRequiredService<IInvoiceOperations>();
        var clientRepo = app.Services.GetRequiredService<IClientRepo>();
        var pdfExporter = app.Services.GetRequiredService<IPdfInvoiceExporter>().Exporter;
        var imageExporter = app.Services.GetRequiredService<IImageInvoiceExporter>().Exporter;
        var config = app.Services.GetRequiredService<Config>();
        var logger = app.Services.GetRequiredService<Utilities.ILogger>();

        var invoicing = app.MapGroup("/api/invoicing")
            .AddEndpointFilter(SchedulingEndpointAuth.RequireTechnicianActorAsync);

        invoicing.MapGet("/audit", async (Func<AppDbContext> contextFactory, string? entityType, string? entityId, int? limit) =>
        {
            await using var ctx = contextFactory();
            var query = ctx.AuditEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(e => e.EntityType == entityType.Trim());
            if (!string.IsNullOrWhiteSpace(entityId))
                query = query.Where(e => e.EntityId == entityId.Trim());
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var items = await query
                .OrderByDescending(e => e.OccurredAtUnixTimeMilliseconds)
                .ThenByDescending(e => e.Id)
                .Take(take)
                .Select(e => new
                {
                    e.Id,
                    e.ServiceName,
                    e.Operation,
                    e.EntityType,
                    e.EntityId,
                    e.EntityDisplay,
                    e.ActorRole,
                    e.ActorClinicCode,
                    e.ActorCredentialId,
                    e.ActorCredentialLabel,
                    e.ActorSessionId,
                    e.OccurredAt,
                    e.Ip,
                    e.UserAgent,
                    e.MetadataJson
                })
                .ToListAsync();
            return Results.Json(new { items }, JsonOptions);
        });

        // --- Clients API ---
        invoicing.MapGet("/clients", async (int? limit, string? startAfter) =>
        {
            var l = limit ?? 100;
            var result = await clientRepo.ListAsync(l, startAfter);
            var items = result.Items.Select(ToClientDto).ToList();
            return Results.Json(new { items, nextStartAfter = result.NextStartAfter }, JsonOptions);
        });

        invoicing.MapGet("/clients/latest", async (int? limit) =>
        {
            var l = limit ?? 10;
            var result = await clientRepo.LatestAsync(l);
            var items = result.Items.Select(ToClientDto).ToList();
            return Results.Json(new { items }, JsonOptions);
        });

        invoicing.MapGet("/clients/{nickname}", async (string nickname) =>
        {
            try
            {
                var client = await clientRepo.GetAsync(nickname);
                return Results.Json(ToClientDto(client), JsonOptions);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = $"Client '{nickname}' not found." }, statusCode: 404);
            }
        });

        invoicing.MapPost("/clients", async (HttpContext ctx, IAuditLog auditLog, IClock clock) =>
        {
            var body = await ReadJson<ClientCreateRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            var err = ValidateClientCreate(body);
            if (err != null)
                return Results.Json(new { error = err }, statusCode: 400);

            var client = new Client(
                body.Nickname!.Trim(),
                new BillingAddress(
                    body.Name!.Trim(),
                    body.RepresentativeName?.Trim() ?? "",
                    body.CompanyIdentifier!.Trim(),
                    body.VatIdentifier?.Trim(),
                    body.Address!.Trim(),
                    body.City!.Trim(),
                    body.PostalCode?.Trim() ?? "",
                    body.Country?.Trim() ?? ""));

            try
            {
                await clientRepo.AddAsync(client);
                await AppendAuditAsync(
                    auditLog,
                    clock,
                    ctx,
                    "Clients",
                    "ClientCreated",
                    "Client",
                    client.Nickname,
                    client.Address.Name,
                    new { nickname = client.Nickname, companyIdentifier = client.Address.CompanyIdentifier, city = client.Address.City });
                return Results.Json(ToClientDto(client), statusCode: 201, options: JsonOptions);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
        });

        invoicing.MapPut("/clients/{nickname}", async (string nickname, HttpContext ctx, IAuditLog auditLog, IClock clock) =>
        {
            var body = await ReadJson<ClientUpdateRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            Client existing;
            try
            {
                existing = await clientRepo.GetAsync(nickname);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = $"Client '{nickname}' not found." }, statusCode: 404);
            }

            var newName = body.Name?.Trim() ?? existing.Address.Name;
            var newRep = body.RepresentativeName?.Trim() ?? existing.Address.RepresentativeName;
            var newCompanyId = body.CompanyIdentifier?.Trim() ?? existing.Address.CompanyIdentifier;
            var newAddress = body.Address?.Trim() ?? existing.Address.Address;
            var newCity = body.City?.Trim() ?? existing.Address.City;
            var newPostal = body.PostalCode?.Trim() ?? existing.Address.PostalCode;

            var err = ValidateClientUpdate(newName, newCompanyId, newAddress, newCity);
            if (err != null)
                return Results.Json(new { error = err }, statusCode: 400);

            var newNickname = body.Nickname?.Trim() ?? existing.Nickname;
            var mergedAddress = new BillingAddress(
                newName,
                newRep,
                newCompanyId,
                body.VatIdentifier?.Trim() ?? existing.Address.VatIdentifier,
                newAddress,
                newCity,
                newPostal,
                body.Country?.Trim() ?? existing.Address.Country);

            var update = new IClientRepo.ClientUpdate(newNickname, mergedAddress);
            await clientRepo.UpdateAsync(nickname, update);

            var updated = new Client(newNickname, mergedAddress);
            await AppendAuditAsync(
                auditLog,
                clock,
                ctx,
                "Clients",
                "ClientUpdated",
                "Client",
                updated.Nickname,
                updated.Address.Name,
                new
                {
                    oldNickname = nickname,
                    newNickname = updated.Nickname,
                    companyIdentifier = updated.Address.CompanyIdentifier,
                    renamed = !string.Equals(nickname, updated.Nickname, StringComparison.OrdinalIgnoreCase)
                });
            return Results.Json(ToClientDto(updated), JsonOptions);
        });

        invoicing.MapMethods("/clients/{nickname}/rename", new[] { "PATCH" }, async (string nickname, HttpContext ctx, IAuditLog auditLog, IClock clock) =>
        {
            var body = await ReadJson<ClientRenameRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            var newNickname = body.NewNickname?.Trim();
            if (string.IsNullOrEmpty(newNickname))
                return Results.Json(new { error = "newNickname is required." }, statusCode: 400);

            try
            {
                var existing = await clientRepo.GetAsync(nickname);
                var update = new IClientRepo.ClientUpdate(newNickname, null);
                await clientRepo.UpdateAsync(nickname, update);
                var updated = new Client(newNickname, existing.Address);
                await AppendAuditAsync(
                    auditLog,
                    clock,
                    ctx,
                    "Clients",
                    "ClientUpdated",
                    "Client",
                    updated.Nickname,
                    updated.Address.Name,
                    new { oldNickname = nickname, newNickname = updated.Nickname, renamed = true });
                return Results.Json(ToClientDto(updated), JsonOptions);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
        });

        // --- Invoices API ---
        invoicing.MapGet("/invoices", async (int? limit, string? startAfter) =>
        {
            var limitOrDefault = limit ?? 100;
            var invoices = await invMgmt.ListInvoicesAsync(limitOrDefault, startAfter);
            var clientByCompanyId = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var items = new List<object>();
            foreach (var inv in invoices.Items)
            {
                var companyId = inv.Content.BuyerAddress.CompanyIdentifier;
                if (!clientByCompanyId.TryGetValue(companyId, out var clientNickname))
                {
                    var client = await clientRepo.FindByCompanyIdentifierAsync(companyId);
                    clientNickname = client?.Nickname;
                    clientByCompanyId[companyId] = clientNickname;
                }
                items.Add(new
                {
                    number = inv.Number,
                    date = inv.Content.Date.ToString("yyyy-MM-dd"),
                    clientNickname,
                    buyerName = inv.Content.BuyerAddress.Name,
                    totalCents = inv.TotalAmount.Cents,
                    currency = inv.TotalAmount.Currency.ToString(),
                    status = inv.IsCorrected ? "Corrected" : "Issued",
                    isLegacy = inv.IsLegacy
                });
            }

            return Results.Json(new { items, nextStartAfter = invoices.NextStartAfter }, JsonOptions);
        });

        invoicing.MapGet("/invoices/{number}", async (string number) =>
        {
            try
            {
                var invoice = await invMgmt.GetInvoiceAsync(number);
                return Results.Json(ToInvoiceDetailDto(invoice), JsonOptions);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = $"Invoice '{number}' not found." }, statusCode: 404);
            }
        });

        invoicing.MapPost("/invoices/issue", async (HttpContext ctx, IAuditLog auditLog, IClock clock) =>
        {
            var body = await ReadJson<IssueInvoiceRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.ClientNickname))
                return Results.Json(new { error = "clientNickname is required." }, statusCode: 400);

            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(body.Date))
            {
                if (!DateTime.TryParse(body.Date, out var d))
                    return Results.Json(new { error = "Invalid date format. Use yyyy-MM-dd." }, statusCode: 400);
                date = d;
            }

            if (!body.AmountCents.HasValue)
                return Results.Json(new { error = "amountCents is required." }, statusCode: 400);
            if (body.AmountCents.Value < 0)
                return Results.Json(new { error = "amountCents must be non-negative." }, statusCode: 400);

            try
            {
                var result = await invMgmt.IssueInvoiceAsync(body.ClientNickname!.Trim(), body.AmountCents.Value, date, pdfExporter);
                await AppendAuditAsync(
                    auditLog,
                    clock,
                    ctx,
                    "Invoicing",
                    "InvoiceIssued",
                    "Invoice",
                    result.Invoice.Number,
                    result.Invoice.Number,
                    new
                    {
                        invoiceNumber = result.Invoice.Number,
                        clientNickname = body.ClientNickname!.Trim(),
                        totalCents = result.Invoice.TotalAmount.Cents,
                        date = result.Invoice.Content.Date.ToString("yyyy-MM-dd")
                    });

                return Results.Json(new
                {
                    invoice = new { result.Invoice.Number, date = result.Invoice.Content.Date.ToString("yyyy-MM-dd"), totalCents = result.Invoice.TotalAmount.Cents },
                    pdfUri = result.ExportResult.DataOrUri,
                    pngUri = (string?)null
                }, JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.Message.Contains("not found") ? 404 : 400);
            }
        });

        invoicing.MapPost("/invoices/correct", async (HttpContext ctx, IAuditLog auditLog, IClock clock) =>
        {
            var body = await ReadJson<CorrectInvoiceRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.InvoiceNumber))
                return Results.Json(new { error = "invoiceNumber is required." }, statusCode: 400);

            if (!string.IsNullOrWhiteSpace(body.CorrectInvoiceNumber) &&
                !string.Equals(body.InvoiceNumber!.Trim(), body.CorrectInvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Invoice number cannot be changed when correcting. You are correcting invoice #" + body.CorrectInvoiceNumber.Trim() + "." }, statusCode: 400);

            Invoice existing;
            try
            {
                existing = await invMgmt.GetInvoiceAsync(body.InvoiceNumber!.Trim());
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = $"Invoice '{body.InvoiceNumber!.Trim()}' not found." }, statusCode: 404);
            }
            if (existing.IsLegacy)
                return Results.Json(new { error = $"Legacy invoice {existing.Number} cannot be edited." }, statusCode: 400);

            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(body.Date))
            {
                if (!DateTime.TryParse(body.Date, out var d))
                    return Results.Json(new { error = "Invalid date format. Use yyyy-MM-dd." }, statusCode: 400);
                date = d;
            }

            int? amountCents = body.AmountCents;
            if (amountCents.HasValue && amountCents.Value < 0)
                return Results.Json(new { error = "amountCents must be non-negative." }, statusCode: 400);

            try
            {
                var result = await invMgmt.CorrectInvoiceAsync(body.InvoiceNumber!.Trim(), amountCents, date, pdfExporter);
                if (imageExporter != null)
                    _ = invMgmt.ReExportInvoiceAsync(result.Invoice.Number, imageExporter);
                await AppendAuditAsync(
                    auditLog,
                    clock,
                    ctx,
                    "Invoicing",
                    "InvoiceCorrected",
                    "Invoice",
                    result.Invoice.Number,
                    result.Invoice.Number,
                    new
                    {
                        invoiceNumber = result.Invoice.Number,
                        totalCents = result.Invoice.TotalAmount.Cents,
                        date = result.Invoice.Content.Date.ToString("yyyy-MM-dd")
                    });

                return Results.Json(new
                {
                    invoice = new { result.Invoice.Number, date = result.Invoice.Content.Date.ToString("yyyy-MM-dd"), totalCents = result.Invoice.TotalAmount.Cents },
                    pdfUri = result.ExportResult.DataOrUri,
                    pngUri = (string?)null
                }, JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.Message.Contains("not found") ? 404 : 400);
            }
        });

        invoicing.MapPost("/invoices/preview", async (HttpContext ctx) =>
        {
            var body = await ReadJson<PreviewInvoiceRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            var format = (body.Format ?? "html").Trim().ToLowerInvariant();
            if (format != "html" && format != "png")
                return Results.Json(new { error = "format must be 'html' or 'png'." }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.ClientNickname))
                return Results.Json(new { error = "clientNickname is required." }, statusCode: 400);

            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(body.Date))
            {
                if (!DateTime.TryParse(body.Date, out var d))
                    return Results.Json(new { error = "Invalid date format. Use yyyy-MM-dd." }, statusCode: 400);
                date = d;
            }

            if (body.AmountCents < 0)
                return Results.Json(new { error = "amountCents must be non-negative." }, statusCode: 400);

            if (!string.IsNullOrWhiteSpace(body.InvoiceNumber))
            {
                try
                {
                    var existing = await invMgmt.GetInvoiceAsync(body.InvoiceNumber.Trim());
                    if (existing.IsLegacy)
                        return Results.Json(new { error = "Preview is not available for legacy imported invoices. Use the PDF view instead." }, statusCode: 400);
                }
                catch (InvalidOperationException)
                {
                    // Invoice not found — PreviewInvoiceAsync will use the number as-is for correction preview
                }
            }

            if (format == "html")
            {
                try
                {
                    var result = await invMgmt.PreviewInvoiceAsync(body.ClientNickname!.Trim(), body.AmountCents, date, new InvoiceHtmlExporter(), body.InvoiceNumber?.Trim());
                    if (!result.Success || result.DataOrUri == null)
                        return Results.Json(new { error = "HTML preview failed." }, statusCode: 500);
                    return Results.Content(result.DataOrUri, "text/html; charset=utf-8");
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(new { error = ex.Message }, statusCode: ex.Message.Contains("not found") ? 404 : 400);
                }
            }

            // format == "png"
            try
            {
                var result = await invMgmt.PreviewInvoiceAsync(body.ClientNickname!.Trim(), body.AmountCents, date, imageExporter!, body.InvoiceNumber?.Trim());
                if (!result.Success || result.DataOrUri == null)
                    return Results.Json(new { error = "Image export unavailable: Chromium not found." }, statusCode: 503);

                if (result.DataOrUri.StartsWith("data:image/png;base64,"))
                {
                    var base64 = result.DataOrUri["data:image/png;base64,".Length..];
                    var bytes = Convert.FromBase64String(base64);
                    return Results.File(bytes, "image/png");
                }
                return Results.Json(new { error = "Unexpected export result." }, statusCode: 500);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.Message.Contains("not found") ? 404 : 400);
            }
        });

        // --- Invoice Import API (legacy PDF) ---
        invoicing.MapPost("/invoices/import/analyze", async (HttpContext ctx, IInvoiceImporter importer) =>
        {
            var apiKey = ResolveOpenAiKey(config);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogError(GetMissingOpenAiKeyError(), new InvalidOperationException("OpenAI API key not configured"));
                return Results.Json(new { error = "An internal server error occurred." }, statusCode: 500);
            }

            IFormCollection form;
            try
            {
                form = await ctx.Request.ReadFormAsync();
            }
            catch (InvalidDataException ex)
            {
                return DevFriendlyError($"Invalid multipart form data: {ex.Message}");
            }
            var fileList = form.Files.ToList();
            if (fileList.Count == 0)
                return Results.Json(new { error = "At least one PDF file is required." }, statusCode: 400);

            var validateErr = ValidateImportAnalyzeFiles(fileList);
            if (validateErr != null)
                return Results.Json(new { error = validateErr }, statusCode: 400);

            var nicknameFromMol = string.Equals(form["nicknameFromMol"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            var limitStr = form["limit"].FirstOrDefault();
            int? limit = int.TryParse(limitStr, out var n) && n > 0 ? n : null;

            var options = new ImportAnalyzeOptions(nicknameFromMol, limit);
            var request = new ImportAnalyzeRequest(fileList, options);
            var response = await importer.AnalyzeAsync(request, ctx.RequestAborted);
            return Results.Json(response, JsonOptions);
        });

        invoicing.MapPost("/invoices/import/commit", async (HttpContext ctx, IInvoiceImporter importer, IAuditLog auditLog, IClock clock) =>
        {
            var apiKey = ResolveOpenAiKey(config);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogError(GetMissingOpenAiKeyError(), new InvalidOperationException("OpenAI API key not configured"));
                return Results.Json(new { error = "An internal server error occurred." }, statusCode: 500);
            }

            var body = await ReadJson<ImportCommitRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            var response = await importer.CommitAsync(body, ctx.RequestAborted);
            if (!body.DryRun)
            {
                await AppendAuditAsync(
                    auditLog,
                    clock,
                    ctx,
                    "Invoicing",
                    "InvoiceImportCommitted",
                    "InvoiceImport",
                    Guid.NewGuid().ToString("N"),
                    "Legacy invoice import",
                    new
                    {
                        imported = response.Imported,
                        skipped = response.Skipped,
                        failed = response.Failed,
                        itemCount = body.Items?.Length ?? 0
                    });
            }
            return Results.Json(response, JsonOptions);
        });

        invoicing.MapGet("/invoices/{number}/pdf", async (string number) =>
        {
            try
            {
                var (stream, downloadFileName) = await invMgmt.GetInvoicePdfStreamAsync(number);
                return Results.File(stream, "application/pdf", downloadFileName);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = $"Invoice '{number}' not found." }, statusCode: 404);
            }
            catch (FileNotFoundException)
            {
                return Results.Json(new { error = $"PDF for invoice '{number}' not found." }, statusCode: 404);
            }
        });
    }

    private static Task AppendAuditAsync(
        IAuditLog auditLog,
        IClock clock,
        HttpContext ctx,
        string serviceName,
        string operation,
        string entityType,
        string entityId,
        string? entityDisplay,
        object metadata)
    {
        var actor = SchedulingEndpointAuth.CurrentActor(ctx);
        var auditEvent = new AuditEvent(
            0,
            serviceName,
            operation,
            entityType,
            entityId,
            entityDisplay,
            actor?.Role.ToString() ?? "Unknown",
            actor?.ClinicCode,
            actor?.CredentialId,
            actor?.CredentialLabel,
            actor?.SessionId,
            clock.UtcNow,
            RemoteIp(ctx),
            UserAgent(ctx),
            JsonSerializer.Serialize(metadata, JsonOptions));
        return auditLog.AppendAsync(auditEvent, ctx.RequestAborted);
    }

    private static string? RemoteIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
    private static string? UserAgent(HttpContext ctx)
    {
        var userAgent = ctx.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }

    private static object ToClientDto(Client c) => new
    {
        nickname = c.Nickname,
        name = c.Address.Name,
        representativeName = c.Address.RepresentativeName,
        companyIdentifier = c.Address.CompanyIdentifier,
        vatIdentifier = c.Address.VatIdentifier,
        address = c.Address.Address,
        city = c.Address.City,
        postalCode = c.Address.PostalCode,
        country = c.Address.Country
    };

    private static object ToInvoiceDetailDto(Invoice inv) => new
    {
        number = inv.Number,
        date = inv.Content.Date.ToString("yyyy-MM-dd"),
        totalCents = inv.TotalAmount.Cents,
        currency = inv.TotalAmount.Currency.ToString(),
        status = inv.IsCorrected ? "Corrected" : "Issued",
        isLegacy = inv.IsLegacy,
        sellerAddress = ToAddressDto(inv.Content.SellerAddress),
        buyerAddress = ToAddressDto(inv.Content.BuyerAddress),
        lineItems = inv.Content.LineItems.Select(li => new { description = li.Description, amountCents = li.Amount.Cents }).ToArray(),
        bankTransferInfo = new
        {
            iban = inv.Content.BankTransferInfo.Iban,
            bankName = inv.Content.BankTransferInfo.BankName,
            bic = inv.Content.BankTransferInfo.Bic
        }
    };

    private static object ToAddressDto(BillingAddress a) => new
    {
        name = a.Name,
        representativeName = a.RepresentativeName,
        companyIdentifier = a.CompanyIdentifier,
        vatIdentifier = a.VatIdentifier,
        address = a.Address,
        city = a.City,
        postalCode = a.PostalCode,
        country = a.Country
    };

    private static string? ValidateClientCreate(ClientCreateRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nickname)) return "nickname is required.";
        if (string.IsNullOrWhiteSpace(r.Name)) return "name is required.";
        if (string.IsNullOrWhiteSpace(r.CompanyIdentifier)) return "companyIdentifier is required.";
        if (string.IsNullOrWhiteSpace(r.Address)) return "address is required.";
        if (string.IsNullOrWhiteSpace(r.City)) return "city is required.";
        return null;
    }

    private static string? ValidateClientUpdate(string name, string companyIdentifier, string address, string city)
    {
        if (string.IsNullOrWhiteSpace(name)) return "name is required.";
        if (string.IsNullOrWhiteSpace(companyIdentifier)) return "companyIdentifier is required.";
        if (string.IsNullOrWhiteSpace(address)) return "address is required.";
        if (string.IsNullOrWhiteSpace(city)) return "city is required.";
        return null;
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<T>(JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string? ResolveOpenAiKey(Config config) =>
        config.App.OpenAiKey?.Trim() ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();

    private static IResult DevFriendlyError(string message) =>
        Results.Json(new { error = message }, statusCode: DevFriendlyErrorStatusCode);

    private static string? ValidateImportAnalyzeFiles(IReadOnlyList<IFormFile> files)
    {
        if (files.Count > ImportMaxFileCount)
            return $"Maximum {ImportMaxFileCount} files allowed per request.";

        foreach (var file in files)
        {
            var fileName = file.FileName ?? "";
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return "Only PDF files are accepted. Rejected: " + (string.IsNullOrEmpty(fileName) ? "(unnamed)" : fileName);

            if (file.Length > ImportMaxFileSizeBytes)
                return $"File size must not exceed {ImportMaxFileSizeBytes / (1024 * 1024)}MB. Rejected: " + (string.IsNullOrEmpty(fileName) ? "(unnamed)" : fileName);
        }

        return null;
    }

    private static string GetMissingOpenAiKeyError()
    {
        var envKey = Config.ToEnvKey(nameof(Config.App), nameof(AppConfig.OpenAiKey));
        return $"OpenAI API key is not configured. Set {envKey} in appsettings.json or pass {envKey} / OPENAI_API_KEY via environment variables. Never commit real keys to source control.";
    }

    private record ClientCreateRequest(string? Nickname, string? Name, string? RepresentativeName, string? CompanyIdentifier, string? VatIdentifier, string? Address, string? City, string? PostalCode, string? Country);
    private record ClientUpdateRequest(string? Nickname, string? Name, string? RepresentativeName, string? CompanyIdentifier, string? VatIdentifier, string? Address, string? City, string? PostalCode, string? Country);
    private record ClientRenameRequest(string? NewNickname);
    private record IssueInvoiceRequest(string? ClientNickname, int? AmountCents, string? Date);
    private record CorrectInvoiceRequest(string? InvoiceNumber, int? AmountCents, string? Date, string? CorrectInvoiceNumber);
    private record PreviewInvoiceRequest(string? ClientNickname, int AmountCents, string? Date, string? Format, string? InvoiceNumber);
}
