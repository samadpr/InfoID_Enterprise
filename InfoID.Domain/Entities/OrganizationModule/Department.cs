using InfoID.Domain.Common;

namespace InfoID.Domain.Entities.OrganizationModule;

/// <summary>
/// Self-referencing hierarchy (e.g. Faculty → Department → Class). Flagged in the
/// Domain Model doc as a design decision requiring stakeholder confirmation on
/// hierarchy depth before heavy UI is built on top of it — the shape below is a
/// safe, minimal starting point that won't need a breaking schema change later.
/// </summary>
public class Department : BaseEntity
{
    public long BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Self-referencing FK. Null = top-level department (e.g. a Faculty).</summary>
    public long? ParentDepartmentId { get; set; }
    public Department? ParentDepartment { get; set; }
}
