using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using InfoID.Application.Interfaces;
using InfoID.Licensing.Core;
using Microsoft.Win32;

namespace InfoID.Desktop.Features.Licensing.Services;

/// <summary>
/// Reads this computer's identity for license binding (FR-LIC-3) and turns it into the
/// shared 80-bit fingerprint (InfoID.Licensing.Core.MachineFingerprint).
///
/// Design choice: only STABLE identifiers that read instantly and deterministically are
/// used, because a fingerprint that changes -- or that a slow/failed hardware query
/// returns differently -- would lock a paying customer out. On Windows these come from
/// the registry (no WMI, no PowerShell):
///   - Windows MachineGuid (unique per Windows installation)
///   - BIOS system manufacturer + product, baseboard manufacturer + product
///   - CPU name
/// Deliberately NOT the MAC address (SRS lists it): VPNs, docks and Wi-Fi/Ethernet
/// switching change the "primary" adapter and would break licenses.
/// Known limit: a cloned Windows image shares MachineGuid; the board/CPU values only
/// distinguish different hardware models. Board/disk serial numbers (via WMI) can be
/// added later if cloning becomes an issue.
///
/// Computed once per app run and cached.
/// </summary>
public sealed class MachineFingerprintProvider : IMachineFingerprintProvider
{
    private readonly Lazy<MachineFingerprint> _fingerprint = new(Compute);

    public MachineFingerprint GetFingerprint() => _fingerprint.Value;

    private static MachineFingerprint Compute()
    {
        var components = new Dictionary<string, string?>();

        if (OperatingSystem.IsWindows())
            AddWindowsComponents(components);
        else if (OperatingSystem.IsLinux())
            components["MachineId"] = ReadFirstExisting("/etc/machine-id", "/var/lib/dbus/machine-id");
        else if (OperatingSystem.IsMacOS())
            components["PlatformUuid"] = ReadMacPlatformUuid();

        // Throws with a clear message if nothing could be read.
        return MachineFingerprint.FromComponents(components);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsComponents(Dictionary<string, string?> components)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        using (var crypto = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            components["MachineGuid"] = crypto?.GetValue("MachineGuid") as string;

        using (var bios = hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
        {
            components["SystemManufacturer"] = bios?.GetValue("SystemManufacturer") as string;
            components["SystemProductName"] = bios?.GetValue("SystemProductName") as string;
            components["BaseBoardManufacturer"] = bios?.GetValue("BaseBoardManufacturer") as string;
            components["BaseBoardProduct"] = bios?.GetValue("BaseBoardProduct") as string;
        }

        using (var cpu = hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            components["Processor"] = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();

        if (string.IsNullOrWhiteSpace(components["MachineGuid"]))
            throw new InvalidOperationException("The Windows machine ID could not be read.");
    }

    private static string? ReadFirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        return null;
    }

    private static string? ReadMacPlatformUuid()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ioreg", "-rd1 -c IOPlatformExpertDevice")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(5000);

            const string marker = "\"IOPlatformUUID\" = \"";
            var start = output.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = output.IndexOf('"', start);
            return end > start ? output[start..end] : null;
        }
        catch
        {
            return null;
        }
    }
}
