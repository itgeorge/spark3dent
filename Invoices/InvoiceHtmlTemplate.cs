namespace Invoices;

public class InvoiceHtmlTemplate
{
    private InvoiceHtmlTemplate(string templateHtml, IAmountTranscriber amountTranscriber)
    {
    }
    
    public static Task<InvoiceHtmlTemplate> LoadAsync(IAmountTranscriber amountTranscriber, string? templateHtmlOverride = null)
    {
        // TODO: load template.html embedded resource or the one specified by templateHtmlOverride if not null, parse with Html Agility Pack 
        //  (make sure to add attribution to this repo), validate all necessary fields are present and valid, throw descriptive errors.
        throw new NotImplementedException();
    }

    public string Render(Invoice invoice)
    {
        // TODO: copy the template, populate it with invoiceData using Html Agility Pack and return the resulting html, 
        //  use the amountTranscriber to transcribe the total amount for the `totalWords` field.
        throw new NotImplementedException();
    }
}