using System.Text.Json.Serialization;

namespace Orders;

public enum ProductCategory { Permanent, Temporary }
public enum WorkType { Crown, Bridge, TemporaryCrownBridge }
public enum Material { GlassCeramics, FullContourZirconia, Pfm, PfzLayeredZrCrown, Pmma }
public enum ConstructionType { Crown, InlayOverlay, Bridge }
public enum OrderStatus { Created, Cancelled }

[JsonConverter(typeof(ShadeJsonConverter))]
public enum Shade
{
    Unspecified = 0,
    A1 = 1,
    A2 = 2,
    A3 = 3,
    A3_5 = 4,
    A4 = 5,
    B1 = 6,
    B2 = 7,
    B3 = 8,
    B4 = 9,
    C1 = 10,
    C2 = 11,
    C3 = 12,
    C4 = 13,
    D2 = 14,
    D3 = 15,
    D4 = 16,
    BL1 = 17,
    BL2 = 18,
    BL3 = 19,
    BL4 = 20,
}
