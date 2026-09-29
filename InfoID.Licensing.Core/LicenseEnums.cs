namespace InfoID.Licensing.Core;

/// <summary>Feature tier (SRS 3.3). Numeric values are part of the serial key format
/// (2 bits) -- never renumber, only append.</summary>
public enum LicenseTier : byte
{
    Standard = 0,
    Professional = 1,
    Enterprise = 2,
}

/// <summary>License model (FR-LIC-11, FR-LIC-14). Numeric values are part of the serial
/// key format (2 bits) -- never renumber, only append.</summary>
public enum LicenseType : byte
{
    Subscription = 0,
    Perpetual = 1,
    Trial = 2,
}

/// <summary>Period the card-issuance cap applies to (FR-LIC-12). 1 bit in the key.</summary>
public enum CardLimitPeriod : byte
{
    Monthly = 0,
    Yearly = 1,
}
