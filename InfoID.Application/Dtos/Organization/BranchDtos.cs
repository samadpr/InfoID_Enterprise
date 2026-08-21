namespace InfoID.Application.Dtos.Organization;

public class BranchDto
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? NumberingPrefix { get; set; }
    public long? DefaultPrinterProfileId { get; set; }
    public long? DefaultTemplateId { get; set; }
}

public class CreateBranchRequest
{
    public long OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? NumberingPrefix { get; set; }
}

public class UpdateBranchRequest
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? NumberingPrefix { get; set; }
    public long? DefaultPrinterProfileId { get; set; }
    public long? DefaultTemplateId { get; set; }
}
