using Accounting;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
[TestOf(typeof(InvoiceImporter))]
public class InvoiceImporterTest : InvoiceImporterContractTest
{
    protected override Task<FixtureBase> SetUpAsync()
    {
        var parser = new SequentialParser();
        var clientRepo = new FakeClientRepoImpl();
        var invoiceOps = new FakeInvoiceOps();
        var blobStorage = new FakeBlobStorage();
        var importer = new InvoiceImporter(clientRepo, invoiceOps, parser, blobStorage, "temp-imports");
        return Task.FromResult<FixtureBase>(new Fixture(importer, parser, clientRepo, invoiceOps));
    }

    private sealed class Fixture : FixtureBase
    {
        private readonly SequentialParser _parser;
        private readonly FakeClientRepoImpl _clientRepo;
        private readonly FakeInvoiceOps _invoiceOps;

        public Fixture(IInvoiceImporter importer, SequentialParser parser, FakeClientRepoImpl clientRepo, FakeInvoiceOps invoiceOps)
        {
            Importer = importer;
            _parser = parser;
            _clientRepo = clientRepo;
            _invoiceOps = invoiceOps;
        }

        public override IInvoiceImporter Importer { get; }
        public override void SetParserResults(params Invoices.LegacyInvoiceData?[] results) => _parser.SetResults(results);
        public override Task SeedClientAsync(Client client) => _clientRepo.AddAsync(client);
        public override void SetDuplicateInvoices(params string[] numbers) => _invoiceOps.SetDuplicates(numbers);
        public override IReadOnlyCollection<string> AddedClientNicknames => _clientRepo.AddedNicknames;
        public override IReadOnlyCollection<string> ImportedInvoiceNumbers => _invoiceOps.ImportedNumbers;
    }
}
