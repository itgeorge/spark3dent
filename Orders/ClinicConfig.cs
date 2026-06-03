namespace Orders;

public sealed record ClinicCredentialConfig
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string PinHash { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public ActorRole Role { get; init; } = ActorRole.Clinic;
}

public sealed record ClinicConfig
{
    public string Code { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? LinkedClientNickname { get; init; }
    public bool IsActive { get; init; } = true;
    public List<ClinicCredentialConfig> Credentials { get; init; } = [];
}
