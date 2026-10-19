using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Infrastructure.Persistence;
using InfoID.Shared.Contracts.Accounts;

namespace InfoID.Desktop.Features.Account.Services;

/// <summary>
/// The signed-in customer account (optional -- activation codes never need it).
/// The session token is remembered between app starts in a file in InfoID's AppData
/// folder, encrypted with Windows DPAPI for the current Windows user, so another user
/// on the PC (or a copied file) can't reuse it. Sign out deletes it.
/// </summary>
public sealed partial class AccountSession : ObservableObject
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("InfoID.AccountSession.v1");
    private readonly string _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "account.session");

    public AccountSession() => Load();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn))]
    private string? _token;

    [ObservableProperty] private AccountProfileDto? _profile;

    public bool IsSignedIn => !string.IsNullOrEmpty(Token);

    public void SignIn(string token, AccountProfileDto? profile)
    {
        Token = token;
        Profile = profile;
        Save();
    }

    public void SignOut()
    {
        Token = null;
        Profile = null;
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch
        {
            // Nothing sensitive is left usable: the server session is revoked separately.
        }
    }

    private sealed record Stored(string Token, AccountProfileDto? Profile);

    private void Save()
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(new Stored(Token!, Profile));
            File.WriteAllBytes(_filePath, Protect(json));
        }
        catch
        {
            // Not remembering the session only means signing in again next time.
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var stored = JsonSerializer.Deserialize<Stored>(Unprotect(File.ReadAllBytes(_filePath)));
            if (stored is null || string.IsNullOrWhiteSpace(stored.Token)) return;
            Token = stored.Token;
            Profile = stored.Profile;
        }
        catch
        {
            // Unreadable (other Windows user, damaged): start signed out.
            SignOut();
        }
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser) : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser) : data;
}
