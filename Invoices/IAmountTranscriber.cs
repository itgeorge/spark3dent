namespace Invoices;

public interface IAmountTranscriber
{
    public string Transcribe(Amount amount);
}