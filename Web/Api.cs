using System.Text.Json;
using Accounting;
using Invoices;

namespace Web;

public static class Api
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapRoutes(WebApplication app, AppSetup.AppBootstrap.SetupResult setup)
    {
        var invMgmt = setup.InvoiceManagement;
        var clientRepo = setup.ClientRepo;
        var pdfExporter = setup.PdfExporter;
        var imageExporter = setup.ImageExporter;

        // --- Clients API ---
        app.MapGet("/api/clients", async (int? limit) =>
        {
            var l = limit ?? 100;
            var result = await clientRepo.ListAsync(l);
            var items = result.Items.Select(ToClientDto).ToList();
            return Results.Json(new { items }, JsonOptions);
        });

        app.MapGet("/api/clients/latest", async (int? limit) =>
        {
            var l = limit ?? 10;
            var result = await clientRepo.LatestAsync(l);
            var items = result.Items.Select(ToClientDto).ToList();
            return Results.Json(new { items }, JsonOptions);
        });

        app.MapGet("/api/clients/{nickname}", async (string nickname) =>
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

        app.MapPost("/api/clients", async (HttpContext ctx) =>
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
                    body.RepresentativeName!.Trim(),
                    body.CompanyIdentifier!.Trim(),
                    body.VatIdentifier?.Trim(),
                    body.Address!.Trim(),
                    body.City!.Trim(),
                    body.PostalCode!.Trim(),
                    body.Country?.Trim() ?? ""));

            try
            {
                await clientRepo.AddAsync(client);
                return Results.Json(ToClientDto(client), statusCode: 201, options: JsonOptions);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
        });

        app.MapPut("/api/clients/{nickname}", async (string nickname, HttpContext ctx) =>
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

            var err = ValidateClientUpdate(newName, newRep, newCompanyId, newAddress, newCity, newPostal);
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
            return Results.Json(ToClientDto(updated), JsonOptions);
        });

        // --- Invoices API ---
        app.MapGet("/api/invoices", async (int? limit) =>
        {
            var l = limit ?? 100;
            var invoices = await invMgmt.ListInvoicesAsync(l);
            var clients = await clientRepo.ListAsync(1000);
            var clientByName = clients.Items.ToDictionary(c => c.Address.Name, c => c.Nickname, StringComparer.OrdinalIgnoreCase);

            var items = invoices.Items.Select(inv =>
            {
                var buyerName = inv.Content.BuyerAddress.Name;
                var clientNickname = clientByName.GetValueOrDefault(buyerName);
                return new
                {
                    number = inv.Number,
                    date = inv.Content.Date.ToString("yyyy-MM-dd"),
                    clientNickname,
                    buyerName,
                    totalCents = inv.TotalAmount.Cents,
                    status = inv.IsCorrected ? "Corrected" : "Issued"
                };
            }).ToList();

            return Results.Json(new { items }, JsonOptions);
        });

        app.MapGet("/api/invoices/{number}", async (string number) =>
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

        app.MapPost("/api/invoices/issue", async (HttpContext ctx) =>
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

        app.MapPost("/api/invoices/correct", async (HttpContext ctx) =>
        {
            var body = await ReadJson<CorrectInvoiceRequest>(ctx);
            if (body == null)
                return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.InvoiceNumber))
                return Results.Json(new { error = "invoiceNumber is required." }, statusCode: 400);

            if (!string.IsNullOrWhiteSpace(body.CorrectInvoiceNumber) &&
                !string.Equals(body.InvoiceNumber!.Trim(), body.CorrectInvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Invoice number cannot be changed when correcting. You are correcting invoice #" + body.CorrectInvoiceNumber.Trim() + "." }, statusCode: 400);

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

        app.MapPost("/api/invoices/preview", async (HttpContext ctx) =>
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

        app.MapGet("/api/invoices/{number}/pdf", async (string number) =>
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
        status = inv.IsCorrected ? "Corrected" : "Issued",
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
        if (string.IsNullOrWhiteSpace(r.RepresentativeName)) return "representativeName is required.";
        if (string.IsNullOrWhiteSpace(r.CompanyIdentifier)) return "companyIdentifier is required.";
        if (string.IsNullOrWhiteSpace(r.Address)) return "address is required.";
        if (string.IsNullOrWhiteSpace(r.City)) return "city is required.";
        if (string.IsNullOrWhiteSpace(r.PostalCode)) return "postalCode is required.";
        return null;
    }

    private static string? ValidateClientUpdate(string name, string representativeName, string companyIdentifier, string address, string city, string postalCode)
    {
        if (string.IsNullOrWhiteSpace(name)) return "name is required.";
        if (string.IsNullOrWhiteSpace(representativeName)) return "representativeName is required.";
        if (string.IsNullOrWhiteSpace(companyIdentifier)) return "companyIdentifier is required.";
        if (string.IsNullOrWhiteSpace(address)) return "address is required.";
        if (string.IsNullOrWhiteSpace(city)) return "city is required.";
        if (string.IsNullOrWhiteSpace(postalCode)) return "postalCode is required.";
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

    private record ClientCreateRequest(string? Nickname, string? Name, string? RepresentativeName, string? CompanyIdentifier, string? VatIdentifier, string? Address, string? City, string? PostalCode, string? Country);
    private record ClientUpdateRequest(string? Nickname, string? Name, string? RepresentativeName, string? CompanyIdentifier, string? VatIdentifier, string? Address, string? City, string? PostalCode, string? Country);
    private record IssueInvoiceRequest(string? ClientNickname, int? AmountCents, string? Date);
    private record CorrectInvoiceRequest(string? InvoiceNumber, int? AmountCents, string? Date, string? CorrectInvoiceNumber);
    private record PreviewInvoiceRequest(string? ClientNickname, int AmountCents, string? Date, string? Format, string? InvoiceNumber);
}
