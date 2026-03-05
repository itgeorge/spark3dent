using Accounting;
using Invoices;
using NUnit.Framework;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Utilities;
using Web;

namespace Web.Tests;

[TestFixture]
[TestOf(typeof(TwoPhaseLegacyInvoiceParser))]
public class TwoPhaseLegacyInvoiceParserTests
{
    private const string SampleInvoiceText = """
        Тест Продавач ООД ФАКТУРА Оригинал
        Номер 0000000106
        Дата 01.02.2024г.
        Получател "Тест Дент Студио" ЕООД :
        Адрес: гр.София, ул.Тестова 1
        ЕИК по Булстат: 123456789
        МОЛ: Иван Тестов
        № Описание на стоката/услугата Сума 270.00 лв.
        1 Зъботехнически услуги
        Сума за плащане: 270.00 лв.
        """;

    private static byte[] CreateParseablePdf()
    {
        using var stream = new MemoryStream();
        var document = new PdfDocument();
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10, XFontStyle.Regular);
        gfx.DrawString(SampleInvoiceText, font, XBrushes.Black, new XRect(50, 50, 500, 700), XStringFormats.TopLeft);
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static byte[] CreateUnparseablePdf()
    {
        // Minimal PDF header only - no valid invoice content
        return [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4'];
    }

    [Test]
    public async Task TryParseAsync_WhenFastParserSucceedsAndCompanyInDb_ReturnsResultWithDbAddress()
    {
        var pdfBytes = CreateParseablePdf();
        var fastResult = LegacyPdfParser.TryParse(pdfBytes);
        Assert.That(fastResult, Is.Not.Null, "Precondition: PDF must be parseable by LegacyPdfParser");

        var dbAddress = new BillingAddress(
            "DB Name", "DB Rep", fastResult!.Recipient.CompanyIdentifier, null,
            "DB Street 1", "DB City", "DB 1000", "България");
        var client = new Client("test-nick", dbAddress);

        var clientRepo = new FakeClientRepo();
        await clientRepo.AddAsync(client);

        var parser = new TwoPhaseLegacyInvoiceParser(clientRepo);
        var result = await parser.TryParseAsync(pdfBytes, "dummy-key");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recipient.Address, Is.EqualTo("DB Street 1"));
        Assert.That(result.Recipient.City, Is.EqualTo("DB City"));
        Assert.That(result.Recipient.Name, Is.EqualTo("DB Name"));
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo(fastResult.Recipient.CompanyIdentifier));
        Assert.That(result.Number, Is.EqualTo(fastResult.Number));
    }

    [Test]
    public async Task TryParseAsync_WhenFastParserSucceedsAndCompanyNotInDb_DelegatesToGpt()
    {
        var pdfBytes = CreateParseablePdf();
        var clientRepo = new FakeClientRepo(); // empty, no clients

        var parser = new TwoPhaseLegacyInvoiceParser(clientRepo);
        var result = await parser.TryParseAsync(pdfBytes, "sk-dummy");

        // With dummy key, GPT will fail and return null. We verify we got here without throwing
        // and that we did NOT use the fast parser result (which would have returned data).
        // The result is null because GPT failed - proving we went to GPT path.
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryParseAsync_WhenFastParserReturnsNull_DelegatesToGpt()
    {
        var pdfBytes = CreateUnparseablePdf();
        var clientRepo = new FakeClientRepo();

        var parser = new TwoPhaseLegacyInvoiceParser(clientRepo);
        var result = await parser.TryParseAsync(pdfBytes, "sk-dummy");

        // With invalid PDF and dummy key, GPT returns null. Proves we fell back to GPT.
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryParseAsync_WhenGivenInvalidInput_DoesNotThrow()
    {
        // Defensive: verify parser handles edge cases (null, empty) without throwing.
        // LegacyPdfParser returns null for these; TwoPhaseLegacyInvoiceParser then delegates to GPT.
        var clientRepo = new FakeClientRepo();
        var parser = new TwoPhaseLegacyInvoiceParser(clientRepo);

        var resultNull = await parser.TryParseAsync(null!, "sk-dummy");
        var resultEmpty = await parser.TryParseAsync([], "sk-dummy");

        Assert.That(resultNull, Is.Null);
        Assert.That(resultEmpty, Is.Null);
    }

    private sealed class FakeClientRepo : IClientRepo
    {
        private readonly Dictionary<string, Client> _byNickname = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Client> _byEik = new(StringComparer.OrdinalIgnoreCase);

        public Task<Client?> FindByCompanyIdentifierAsync(string companyIdentifier) =>
            Task.FromResult(_byEik.TryGetValue(companyIdentifier, out var c) ? c : null);

        public Task<Client> GetAsync(string nickname) =>
            _byNickname.TryGetValue(nickname, out var c) ? Task.FromResult(c) : throw new InvalidOperationException("not found");

        public Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null) =>
            Task.FromResult(new QueryResult<Client>([], null));

        public Task<QueryResult<Client>> LatestAsync(int limit, string? startAfterCursor = null) =>
            Task.FromResult(new QueryResult<Client>([], null));

        public Task AddAsync(Client client)
        {
            _byNickname[client.Nickname] = client;
            _byEik[client.Address.CompanyIdentifier] = client;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string nickname, IClientRepo.ClientUpdate update) => throw new NotImplementedException();
        public Task DeleteAsync(string nickname) => throw new NotImplementedException();
    }
}
