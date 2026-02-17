namespace Invoices;

public class BgAmountTranscriber : IAmountTranscriber
{
    private const int MaxCents = 999_999_99; // 999,999.99 EUR

    private enum AmountType { WholeEuros, Cents }  // евро=neuter (едно/две), евроцент=masculine (един/два)

    public string Transcribe(Amount amount)
    {
        if (amount.Currency != Currency.Eur)
            throw new ArgumentOutOfRangeException(nameof(amount), "Only EUR is supported.");

        var cents = amount.Cents;
        if (cents < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        if (cents > MaxCents)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be less than 1,000,000 EUR.");

        var wholeEuros = cents / 100;
        var remainingCents = cents % 100;

        if (wholeEuros == 0 && remainingCents == 0)
            return "Нула евро и нула евроцента";

        var wholePart = ToBulgarian(wholeEuros, AmountType.WholeEuros);
        var (centsPart, centsSuffix) = ToBulgarianWithSuffix(remainingCents);

        string result;
        if (remainingCents == 0)
            result = $"{wholePart} евро";
        else if (wholeEuros == 0)
            result = $"Нула евро и {centsPart} {centsSuffix}";
        else
            result = $"{wholePart} евро и {centsPart} {centsSuffix}";

        return Capitalize(result);
    }

    private static string Capitalize(string s) =>
        s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    /// <summary>Converts cents 0-99 to words and returns the correct noun form (евроцент/евроцента). Singular евроцент for 1,21,31,... (except 11).</summary>
    private static (string words, string suffix) ToBulgarianWithSuffix(int n)
    {
        if (n == 0) return ("нула", "евроцента");
        var words = ToBulgarian(n, AmountType.Cents);
        var suffix = (n % 10 == 1 && n != 11) ? "евроцент" : "евроцента";
        return (words, suffix);
    }

    /// <summary>Converts 0..999999 to Bulgarian words. AmountType selects 1/2 forms: WholeEuros=едно/две, Cents=един/два.</summary>
    private static string ToBulgarian(int n, AmountType amountType)
    {
        if (n == 0) return "нула";

        var parts = new List<string>();

        if (n >= 1000)
        {
            var thousands = n / 1000;
            n %= 1000;
            if (thousands == 1)
                parts.Add("хиляда");
            else if (thousands == 2)
                parts.Add("две хиляди");
            else
                parts.Add(ToBulgarianUpTo999(thousands, amountType) + " хиляди");
            if (n > 0 && NeedsConjunctionBeforeRemainder(n))
                parts.Add("и");
        }

        if (n > 0)
            parts.Add(ToBulgarianUpTo999(n, amountType));

        return string.Join(" ", parts);
    }

    /// <summary>Converts 1..999 to Bulgarian words. AmountType selects unit 1/2 forms.</summary>
    private static string ToBulgarianUpTo999(int n, AmountType amountType)
    {
        if (n == 0) return "";

        var parts = new List<string>();

        if (n >= 100)
        {
            var hundreds = n / 100;
            n %= 100;
            var hundredsWord = hundreds switch
            {
                1 => "сто",
                2 => "двеста",
                3 => "триста",
                4 => "четиристотин",
                5 => "петстотин",
                6 => "шестстотин",
                7 => "седемстотин",
                8 => "осемстотин",
                9 => "деветстотин",
                _ => ""
            };
            if (!string.IsNullOrEmpty(hundredsWord))
                parts.Add(hundredsWord);
            if (n == 0)
                return string.Join(" ", parts);
            if (NeedsConjunctionBeforeRemainder(n))
                parts.Add("и");
        }

        if (n >= 20)
        {
            var tens = n / 10;
            n %= 10;
            var tensWord = tens switch
            {
                2 => "двадесет",
                3 => "тридесет",
                4 => "четиридесет",
                5 => "петдесет",
                6 => "шестдесет",
                7 => "седемдесет",
                8 => "осемдесет",
                9 => "деветдесет",
                _ => ""
            };
            if (n == 0)
                parts.Add(tensWord);
            else
                parts.Add($"{tensWord} и {ToBulgarianUnit(n, amountType)}");
            return string.Join(" ", parts);
        }

        if (n >= 10)
        {
            var word = n switch
            {
                10 => "десет",
                11 => "единадесет",
                12 => "дванадесет",
                13 => "тринадесет",
                14 => "четиринадесет",
                15 => "петнадесет",
                16 => "шестнадесет",
                17 => "седемнадесет",
                18 => "осемнадесет",
                19 => "деветнадесет",
                _ => ""
            };
            if (!string.IsNullOrEmpty(word))
                parts.Add(word);
            return string.Join(" ", parts);
        }

        if (n > 0)
            parts.Add(ToBulgarianUnit(n, amountType));
        return string.Join(" ", parts);
    }

    /// <summary>True when "и" should precede the remainder: 1-19, round tens (20-90), or round hundreds (100-900).</summary>
    private static bool NeedsConjunctionBeforeRemainder(int n) =>
        (n >= 1 && n <= 19) || (n % 10 == 0 && n >= 20 && n < 100) || (n % 100 == 0 && n >= 100);

    private static string ToBulgarianUnit(int n, AmountType amountType) => n switch
    {
        1 => amountType == AmountType.Cents ? "един" : "едно",
        2 => amountType == AmountType.Cents ? "два" : "две",
        3 => "три",
        4 => "четири",
        5 => "пет",
        6 => "шест",
        7 => "седем",
        8 => "осем",
        9 => "девет",
        _ => ""
    };
}
