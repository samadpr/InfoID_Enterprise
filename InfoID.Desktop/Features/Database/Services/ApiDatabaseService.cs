using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class ApiDatabaseService
{
    private readonly HttpClient _httpClient;

    public ApiDatabaseService()
    {
        _httpClient = new HttpClient();
    }



    public async Task<string> GetAsync(
      string url,
      string authenticationType = "None",
      string apiKey = "",
      string bearerToken = "",
      string username = "",
      string password = "")
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("API URL is required.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        switch (authenticationType)
        {
            case "API Key":
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new ArgumentException("API key is required.");

                request.Headers.Add("X-API-Key", apiKey);
                break;

            case "Bearer Token":
                if (string.IsNullOrWhiteSpace(bearerToken))
                    throw new ArgumentException("Bearer token is required.");

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        bearerToken);
                break;

            case "Basic Authentication":
                if (string.IsNullOrWhiteSpace(username))
                    throw new ArgumentException("Username is required.");

                var credentials =
                    Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(
                            $"{username}:{password}"));

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Basic",
                        credentials);
                break;
        }

        using var response =
            await _httpClient.SendAsync(request);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
    public DataTable ConvertJsonToDataTable(string json)
    {
        var table = new DataTable("API");

        if (string.IsNullOrWhiteSpace(json))
            return table;

        // Everything is read into plain strings INSIDE this using block,
        // so no JsonElement ever outlives the document.
        var records = new List<List<KeyValuePair<string, string>>>();

        using (var document = JsonDocument.Parse(json))
        {
            foreach (var item in FindRecords(document.RootElement))
            {
                var values = new List<KeyValuePair<string, string>>();

                if (item.ValueKind == JsonValueKind.Object)
                    Flatten(item, string.Empty, values);
                else
                    values.Add(new("Value", ToText(item)));

                records.Add(values);
            }
        }

        // Columns in first-seen order
        var columnNames = new List<string>();
        foreach (var record in records)
            foreach (var pair in record)
                if (!columnNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    columnNames.Add(pair.Key);

        // Make a numeric "id" column a real number so sorting is 1,2,3...10
        // instead of 1,10,2,3...
        foreach (var name in columnNames)
        {
            var isNumericId =
                name.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                records.All(r => r
                    .Where(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .All(p => long.TryParse(p.Value, out _)));

            table.Columns.Add(name, isNumericId ? typeof(long) : typeof(string));
        }

        foreach (var record in records)
        {
            var row = table.NewRow();

            foreach (DataColumn column in table.Columns)
            {
                if (column.DataType == typeof(string))
                    row[column] = string.Empty;
            }

            foreach (var pair in record)
            {
                var column = table.Columns[pair.Key]!;

                row[column] = column.DataType == typeof(long)
                    ? long.Parse(pair.Value)
                    : pair.Value;
            }

            table.Rows.Add(row);
        }

        table.AcceptChanges();
        return table;
    }

    private static List<JsonElement> FindRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().ToList();

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array &&
                    property.Value.EnumerateArray()
                        .Any(e => e.ValueKind == JsonValueKind.Object))
                {
                    return property.Value.EnumerateArray().ToList();
                }
            }

            return new List<JsonElement> { root };
        }

        return new List<JsonElement>();
    }

    // address.geo.lat -> address_geo_lat
    private static void Flatten(
        JsonElement element,
        string prefix,
        List<KeyValuePair<string, string>> output)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}_{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, output);
                    break;

                case JsonValueKind.Array:
                    output.Add(new(name, string.Join(", ",
                        property.Value.EnumerateArray().Select(ToText))));
                    break;

                default:
                    output.Add(new(name, ToText(property.Value)));
                    break;
            }
        }
    }

    private static string ToText(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.GetRawText()
        };


}
