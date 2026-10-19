using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

public sealed record AuditRow(DateTime When, string Who, string Action, string? Details, string? Workstation);

/// <summary>Admin audit log viewer (FR-ADM-3) with tamper check of the hash chain.</summary>
public sealed partial class AuditLogViewModel : ViewModelBase, ILoadable
{
    private readonly AuditService _audit;

    public ObservableCollection<AuditRow> Rows { get; } = new();

    [ObservableProperty] private string? _chainStatus;

    public AuditLogViewModel(AuditService audit) => _audit = audit;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var entries = await _audit.GetRecentAsync();
        Rows.Clear();
        foreach (var e in entries)
            Rows.Add(new AuditRow(e.ActionDate.ToLocalTime(), e.AdminUser?.Email ?? "-", e.Action, e.DetailsJson, e.IPAddress));
    }

    [RelayCommand]
    private async Task VerifyChain()
    {
        var broken = await _audit.VerifyChainAsync();
        ChainStatus = broken is null
            ? "Audit log intact: every entry's hash chain verifies."
            : $"WARNING: the audit log was modified at entry #{broken}. Entries from there on cannot be trusted.";
    }
}
