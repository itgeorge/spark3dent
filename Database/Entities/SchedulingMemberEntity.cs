using Orders;

namespace Database.Entities;

public class SchedulingMemberEntity
{
    public OrganizationType OrganizationType { get; set; }
    public string OrganizationCode { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
