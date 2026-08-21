namespace InfoID.Domain.Common.Enums;

public enum LicenseTier
{
    Standard, Professional, Enterprise
}

public enum LicenseType
{
    Subscription, Perpetual, Trial
}

public enum ActivationMode
{
    Online, Offline
}

public enum LicenseStatus
{
    Active, Expired, GracePeriod, Deactivated
}

public enum ValidationResult
{
    Success, Failed, OfflineCached
}
