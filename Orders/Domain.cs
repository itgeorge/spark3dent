using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orders;

public enum ProductCategory { Permanent, Temporary }
public enum WorkType { Crown, Bridge, TemporaryCrownBridge }
public enum Material { GlassCeramics, FullContourZirconia, Pfm, PfzLayeredZrCrown, Pmma }
public enum ConstructionType { Crown, Facet, Bridge }
public enum OrderStatus { Created }

public sealed record ToothRange(int Start, int End)
{
    public int Min => Math.Min(Start, End);
    public int Max => Math.Max(Start, End);

    public bool IsSingle => Start == End;

    public void Validate(ConstructionType constructionType)
    {
        if (!IsValidFdiTooth(Start)) throw new InvalidOperationException($"Invalid FDI tooth number: {Start}.");
        if (!IsValidFdiTooth(End)) throw new InvalidOperationException($"Invalid FDI tooth number: {End}.");
        if (constructionType == ConstructionType.Crown && !IsSingle)
            throw new InvalidOperationException("Crown orders must select exactly one tooth.");
        if (constructionType != ConstructionType.Crown && IsSingle)
            throw new InvalidOperationException("Non-crown orders must select a tooth range.");
        if (Quadrant(Start) != Quadrant(End))
            throw new InvalidOperationException("Walking skeleton supports ranges within one FDI quadrant only.");
    }

    public int[] DefaultAbutments(ConstructionType constructionType) =>
        constructionType == ConstructionType.Bridge ? [Start, End] : [];

    public static bool IsValidFdiTooth(int tooth)
    {
        var q = tooth / 10;
        var n = tooth % 10;
        return q is >= 1 and <= 4 && n is >= 1 and <= 8;
    }

    private static int Quadrant(int tooth) => tooth / 10;
}

public sealed record WorkRule(
    ProductCategory ProductCategory,
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    int MinBusinessDays);

public sealed record ClinicCredentialConfig
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string PinHash { get; init; } = "";
    public bool IsActive { get; init; } = true;
}

public sealed record ClinicConfig
{
    public string Code { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? LinkedClientNickname { get; init; }
    public bool IsActive { get; init; } = true;
    public List<ClinicCredentialConfig> Credentials { get; init; } = [];
}

public sealed record SchedulingOptions
{
    public int SessionSlidingDays { get; init; } = 30;
    public int? SessionAbsoluteDays { get; init; } = 180;
    public int DefaultMinBusinessDays { get; init; } = 3;
    public List<ClinicConfig> Clinics { get; init; } = [];
    public List<WorkRule> WorkRules { get; init; } = [];
}

public sealed record SchedulingConfigSnapshot(SchedulingOptions Options, DateTimeOffset LoadedAt, string SourcePath)
{
    public ClinicConfig GetClinic(string code)
    {
        var clinic = Options.Clinics.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        if (clinic == null || !clinic.IsActive)
            throw new InvalidOperationException("Clinic not found or inactive.");
        return clinic;
    }

    public WorkRule FindWorkRule(ProductCategory productCategory, WorkType workType, Material material, ConstructionType constructionType)
    {
        var rule = Options.WorkRules.FirstOrDefault(r =>
            r.ProductCategory == productCategory && r.WorkType == workType && r.Material == material && r.ConstructionType == constructionType);
        if (rule != null) return rule;
        return new WorkRule(productCategory, workType, material, constructionType, Options.DefaultMinBusinessDays);
    }
}

public interface ISchedulingConfigProvider
{
    SchedulingConfigSnapshot Current { get; }
    Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default);
}

public sealed class JsonSchedulingConfigProvider : ISchedulingConfigProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private SchedulingConfigSnapshot _current;

    public JsonSchedulingConfigProvider(string path)
    {
        _path = path;
        _current = Load(path);
    }

    public SchedulingConfigSnapshot Current => _current;

    public Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default)
    {
        _current = Load(_path);
        return Task.FromResult(_current);
    }

    private static SchedulingConfigSnapshot Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Scheduling config file not found: {path}");
        var json = File.ReadAllText(path);
        var options = JsonSerializer.Deserialize<SchedulingOptions>(json, JsonOptions)
            ?? throw new InvalidOperationException("Scheduling config is empty or invalid.");
        Validate(options);
        return new SchedulingConfigSnapshot(options, DateTimeOffset.UtcNow, path);
    }

    private static void Validate(SchedulingOptions options)
    {
        if (options.SessionSlidingDays <= 0) throw new InvalidOperationException("SessionSlidingDays must be positive.");
        if (options.DefaultMinBusinessDays < 0) throw new InvalidOperationException("DefaultMinBusinessDays must be non-negative.");
        var clinicCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clinic in options.Clinics)
        {
            if (string.IsNullOrWhiteSpace(clinic.Code)) throw new InvalidOperationException("Clinic code is required.");
            if (!clinicCodes.Add(clinic.Code)) throw new InvalidOperationException($"Duplicate clinic code: {clinic.Code}");
            var credentialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var credentialLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var credential in clinic.Credentials)
            {
                if (string.IsNullOrWhiteSpace(credential.Id)) throw new InvalidOperationException($"Credential id is required for clinic {clinic.Code}.");
                if (!credentialIds.Add(credential.Id)) throw new InvalidOperationException($"Duplicate credential id '{credential.Id}' for clinic {clinic.Code}.");
                if (!string.IsNullOrWhiteSpace(credential.Label) && !credentialLabels.Add(credential.Label)) throw new InvalidOperationException($"Duplicate credential label '{credential.Label}' for clinic {clinic.Code}.");
                if (credential.IsActive && string.IsNullOrWhiteSpace(credential.PinHash)) throw new InvalidOperationException($"Active credential '{credential.Id}' must have a PIN hash.");
            }
        }
        foreach (var rule in options.WorkRules)
        {
            if (rule.MinBusinessDays < 0) throw new InvalidOperationException("Work-rule MinBusinessDays must be non-negative.");
        }
    }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
}

public sealed class PinHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;
    private readonly string _pepper;

    public PinHasher(string? pepper = null) => _pepper = pepper ?? "";

    public string Hash(string pin, int iterations = DefaultIterations)
    {
        ValidatePinShape(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, iterations);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string pin, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;
        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch
        {
            return false;
        }
        var actual = Derive(pin, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Fingerprint(string storedHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(storedHash));
        return Convert.ToHexString(bytes)[..16];
    }

    public static void ValidatePinShape(string pin)
    {
        if (pin.Length != 6 || pin.Any(ch => ch < '0' || ch > '9'))
            throw new InvalidOperationException("PIN must be exactly 6 digits.");
    }

    private byte[] Derive(string pin, byte[] salt, int iterations)
    {
        var material = Encoding.UTF8.GetBytes(pin + ":" + _pepper);
        return Rfc2898DeriveBytes.Pbkdf2(material, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
    }
}

public sealed record AuthenticatedActor(
    string ClinicCode,
    string ClinicDisplayName,
    string CredentialId,
    string CredentialLabel,
    string CredentialPinHashFingerprint,
    string SessionId);

public sealed record AuthSession(
    string Id,
    string ClinicCode,
    string CredentialId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    string CreatedIp,
    string CreatedUserAgent);

public sealed record OrderDraft(
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    ToothRange TeethRange,
    DateOnly RequestedDeliveryDate,
    string? Notes);

public sealed record OrderRecord(
    long Id,
    string OrderCode,
    string ClinicCode,
    string ClinicDisplayName,
    string CredentialId,
    string CredentialLabel,
    string CredentialPinHashFingerprint,
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    WorkType WorkType,
    Material Material,
    ConstructionType ConstructionType,
    int ToothStart,
    int ToothEnd,
    string AbutmentTeeth,
    DateOnly RequestedDeliveryDate,
    OrderStatus Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedIp,
    string CreatedUserAgent);
