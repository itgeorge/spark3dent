using System.ComponentModel;

namespace Invoices;

public enum Currency
{
    Eur,
    Bgn
}

public record BillingAddress(string Name, string RepresentativeName, string CompanyIdentifier, string? VatIdentifier, string Address, string City, string PostalCode, string Country);

public record BankTransferInfo(string Iban, string BankName, string Bic);

public record Amount
{
    public Amount(int cents, Currency currency)
    {
        Cents = cents;
        Currency = currency;
    }

    public static Amount Zero(Currency currency)
    {
        return new Amount(0, currency);
    }

    public int Cents { get; }
    public Currency Currency { get; }

    public static Amount operator +(Amount a, Amount b)
    {
        if (a.Currency != b.Currency)
        {
            throw new InvalidOperationException($"Cannot add amounts with different currencies: {a.Currency} and {b.Currency}");
        }

        return new Amount(a.Cents + b.Cents, a.Currency);
    }
}

public record Invoice
{
    public record LineItem(string Description, Amount Amount);

    // TODO: it might be a good idea to client id (or other unique identifier) here, so that we can 
    //  easily retrieve the invoice by client id later - though we can probably do that through
    //  filtering by BillingAddress.CompanyIdentifier - so we should think this over more carefully once we need it
    // TODO: add validation that all line items have same currency - if not, throw an exception
    public record InvoiceContent(DateTime Date, BillingAddress SellerAddress, BillingAddress BuyerAddress, LineItem[] LineItems, BankTransferInfo BankTransferInfo);

    public Invoice(string number, InvoiceContent content, bool isCorrected = false, bool isLegacy = false)
    {
        Number = number;
        Content = content;
        IsCorrected = isCorrected;
        IsLegacy = isLegacy;
    }

    public string Number { get; }
    public InvoiceContent Content { get; }
    public bool IsCorrected { get; }
    public bool IsLegacy { get; }
    public Amount TotalAmount => Content.LineItems.Length == 0
        ? Amount.Zero(Currency.Eur)
        : Content.LineItems.Aggregate(Amount.Zero(Content.LineItems[0].Amount.Currency), (acc, li) => acc + li.Amount);
}