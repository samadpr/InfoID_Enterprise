namespace InfoID.Domain.Common.Enums;

public enum VisitorApprovalStatus
{
    Pending, Approved, Rejected
}

/// <summary>How a visitor registration reached InfoID (stored by name).</summary>
public enum VisitorSource
{
    Manual = 0,
    File = 1,
    GoogleForm = 2,
}
