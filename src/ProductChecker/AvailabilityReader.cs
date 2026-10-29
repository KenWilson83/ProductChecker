using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProductChecker;

public record AvailabilityResult(string Status, string Message, string[] Evidence);

public static class AvailabilityReader
{
    // Read explicit Schema.org product offers; ordinary page text is not evidence of stock.
    public static AvailabilityResult Read(string html)
    {
        var evidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match script in Regex.Matches(html, @"<script\b(?<attrs>[^>]*)>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1)))
        {
            if (!Regex.IsMatch(script.Groups["attrs"].Value, "\\btype\\s*=\\s*(['\"])application/ld\\+json\\1", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) continue;
            try
            {
                using var document = JsonDocument.Parse(script.Groups["body"].Value, new JsonDocumentOptions { MaxDepth = 64 });
                Visit(document.RootElement, evidence);
            }
            catch (JsonException) { /* A malformed block must not hide other valid blocks. */ }
        }
        var values = evidence.Order().ToArray();
        var available = values.Any(v => v is "InStock" or "LimitedAvailability" or "OnlineOnly");
        var unavailable = values.Any(v => v is "OutOfStock" or "SoldOut" or "Discontinued" or "InStoreOnly");
        if (available && !unavailable && values.All(v => v is "InStock" or "LimitedAvailability" or "OnlineOnly"))
            return new("available", "The page reports this product available for online purchase. Confirm the selected variant and delivery location at checkout.", values);
        if (unavailable && !available && values.All(v => v is "OutOfStock" or "SoldOut" or "Discontinued" or "InStoreOnly"))
            return new("unavailable", "The page reports this product unavailable for online purchase.", values);
        return new("unknown", values.Length == 0
            ? "No supported product availability data was found. Check the store page directly."
            : "The page has mixed, preorder, backorder, or unrecognized availability. Check the specific offer or variant at the store.", values);
    }

    private static void Visit(JsonElement node, HashSet<string> evidence)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) Visit(child, evidence);
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("@type", out var type) && IsProduct(type) && node.TryGetProperty("offers", out var offers))
                ReadOffers(offers, evidence);
            foreach (var property in node.EnumerateObject()) Visit(property.Value, evidence);
        }
    }

    private static bool IsProduct(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString() is "Product" or "https://schema.org/Product" or "http://schema.org/Product",
        JsonValueKind.Array => type.EnumerateArray().Any(IsProduct),
        _ => false
    };

    private static void ReadOffers(JsonElement node, HashSet<string> evidence)
    {
        if (node.ValueKind == JsonValueKind.Array)
            foreach (var offer in node.EnumerateArray()) ReadOffers(offer, evidence);
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("availability", out var value) && value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString()!;
                if (raw.StartsWith("https://schema.org/", StringComparison.Ordinal)) raw = raw[19..];
                else if (raw.StartsWith("http://schema.org/", StringComparison.Ordinal)) raw = raw[18..];
                evidence.Add(raw);
            }
            else if (!node.TryGetProperty("offers", out _)) evidence.Add("Unspecified");
            if (node.TryGetProperty("offers", out var nested)) ReadOffers(nested, evidence);
        }
    }
}
