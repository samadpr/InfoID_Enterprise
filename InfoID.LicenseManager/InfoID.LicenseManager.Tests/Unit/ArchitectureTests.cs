using System.Reflection;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure;

namespace InfoID.LicenseManager.Tests.Unit;

/// <summary>Guards the agreed dependency direction (Web -> Application/Infrastructure,
/// Infrastructure -> Application -> Domain) by inspecting compiled assembly references.</summary>
public class ArchitectureTests
{
    private const string Web = "InfoID.LicenseManager.Web";
    private const string Infrastructure = "InfoID.LicenseManager.Infrastructure";
    private const string Application = "InfoID.LicenseManager.Application";
    private const string Domain = "InfoID.LicenseManager.Domain";

    private static readonly Assembly DomainAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ICurrentUser).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(DependencyInjection).Assembly;

    private static List<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    private static bool IsFramework(string name) =>
        name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
        || name.StartsWith("Npgsql", StringComparison.Ordinal)
        || name.StartsWith("Avalonia", StringComparison.Ordinal);

    [Fact]
    public void Domain_depends_on_no_other_layer_and_no_framework_packages()
    {
        var refs = References(DomainAssembly);

        Assert.DoesNotContain(refs, r => r is Application or Infrastructure or Web);
        Assert.DoesNotContain(refs, IsFramework);
    }

    [Fact]
    public void Application_depends_on_Domain_only_and_not_on_EF_or_MVC()
    {
        var refs = References(ApplicationAssembly);

        Assert.DoesNotContain(refs, r => r is Infrastructure or Web);
        Assert.DoesNotContain(refs, IsFramework);
    }

    [Fact]
    public void Infrastructure_depends_on_Application_and_Domain_but_not_on_Web_or_MVC()
    {
        var refs = References(InfrastructureAssembly);

        Assert.Contains(Application, refs);
        Assert.Contains(Domain, refs);
        Assert.DoesNotContain(refs, r => r == Web || r.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
                                         || r.StartsWith("Avalonia", StringComparison.Ordinal));
    }
}
