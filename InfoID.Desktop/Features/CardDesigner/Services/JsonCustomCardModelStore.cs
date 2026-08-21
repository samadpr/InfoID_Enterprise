using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class JsonCustomCardModelStore : ICustomCardModelStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlimLite _lock = new();

    public JsonCustomCardModelStore()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "CustomCardModels.json");
    }

    public async Task<IReadOnlyList<CustomCardModel>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadAllAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CustomCardModel> SaveAsync(CustomCardModel model)
    {
        await _lock.WaitAsync();
        try
        {
            var all = (await ReadAllAsync()).ToList();
            var index = all.FindIndex(m => m.Id == model.Id);
            if (index >= 0)
            {
                all[index] = model;
            }
            else
            {
                all.Add(model);
            }

            await WriteAllAsync(all);
            return model;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = (await ReadAllAsync()).Where(m => m.Id != id).ToList();
            await WriteAllAsync(all);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<CustomCardModel>> ReadAllAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new List<CustomCardModel>();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var models = await JsonSerializer.DeserializeAsync<List<CustomCardModel>>(stream);
            return models ?? new List<CustomCardModel>();
        }
        catch
        {
            // Corrupt file -- don't crash the designer over a broken preferences file.
            return new List<CustomCardModel>();
        }
    }

    private async Task WriteAllAsync(List<CustomCardModel> models)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(models, options);
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Copy(tempPath, _filePath, overwrite: true);
        File.Delete(tempPath);
    }

    /// <summary>Tiny async lock so concurrent saves can't interleave/corrupt the file --
    /// avoids pulling in SemaphoreSlim's IDisposable ceremony for one call site.</summary>
    private sealed class SemaphoreSlimLite
    {
        private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);
        public Task WaitAsync() => _semaphore.WaitAsync();
        public void Release() => _semaphore.Release();
    }
}