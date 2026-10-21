namespace InfoID.LicenseManager.Domain.Entities;

public static class ProductCodes
{
    public const string InfoID = "INFOID";
}

/// <summary>A product sold through the platform (InfoID now, other solutions later).</summary>
public class Product : EntityBase
{
    /// <summary>Stable code used everywhere, e.g. "INFOID". Unique.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
