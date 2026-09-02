using System;
using System.IO;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Copies user-picked files (images, photos, signatures) into InfoID's local
/// assets folder and hands back a small, portable reference string that Image/Photo/
/// SignatureElement store instead of an absolute path. Absolute paths break the moment
/// a design is opened on a different machine or the source file is moved/deleted, which
/// is exactly the "broken asset references" problem Part 53 asks to avoid.</summary>
public interface IDesignAssetService
{
    /// <summary>Copies the file at <paramref name="sourcePath"/> into the assets store
    /// under a new generated name (collisions are impossible) and returns the reference
    /// to save on the element (e.g. "3f9a1c2b.png").</summary>
    Task<string> ImportAsync(string sourcePath);

    /// <summary>Resolves a stored reference back to a full local path for rendering.
    /// Returns null if the reference is empty or the file is missing (e.g. assets
    /// folder was cleared, or a design was copied from another machine without its
    /// assets) -- callers must treat that as "no image", not throw.</summary>
    string? ResolveToFullPath(string? assetReference);
}

public sealed class LocalDesignAssetService : IDesignAssetService
{
    public async Task<string> ImportAsync(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var reference = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(InfoID.Infrastructure.Persistence.DatabaseLocation.GetAssetsDirectory(), reference);

        await using var source = File.OpenRead(sourcePath);
        await using var dest = File.Create(destination);
        await source.CopyToAsync(dest);

        return reference;
    }

    public string? ResolveToFullPath(string? assetReference)
    {
        if (string.IsNullOrWhiteSpace(assetReference)) return null;
        var fullPath = Path.Combine(InfoID.Infrastructure.Persistence.DatabaseLocation.GetAssetsDirectory(), assetReference);
        return File.Exists(fullPath) ? fullPath : null;
    }
}
