using ProductChecker;
using System.Net;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}"); passed++;
}
string Page(string offers) => "<script type=\"application/ld+json\">{\"@type\":\"Product\",\"offers\":" + offers + "}</script>";
string Offer(string status) => "{\"availability\":\"https://schema.org/" + status + "\"}";
foreach (var status in new[] { "InStock", "LimitedAvailability", "OnlineOnly" })
    Check(status, AvailabilityReader.Read(Page(Offer(status))).Status == "available");
foreach (var status in new[] { "OutOfStock", "SoldOut", "Discontinued", "InStoreOnly" })
    Check(status, AvailabilityReader.Read(Page(Offer(status))).Status == "unavailable");
foreach (var status in new[] { "PreOrder", "BackOrder", "PreSale", "Unexpected" })
    Check(status, AvailabilityReader.Read(Page(Offer(status))).Status == "unknown");
Check("mixed variants", AvailabilityReader.Read(Page("[" + Offer("InStock") + "," + Offer("OutOfStock") + "]")).Status == "unknown");
Check("unspecified variant", AvailabilityReader.Read(Page("[" + Offer("InStock") + ",{}]")).Status == "unknown");
Check("plain text is not stock evidence", AvailabilityReader.Read("<p>In stock! Add to cart</p>").Status == "unknown");
Check("malformed JSON", AvailabilityReader.Read("<script type='application/ld+json'>{bad}</script>").Status == "unknown");
Check("graph and type array", AvailabilityReader.Read("<script type='application/ld+json'>{\"@graph\":[{\"@type\":[\"Thing\",\"Product\"],\"offers\":" + Offer("InStock") + "}]}</script>").Status == "available");
foreach (var address in new[] { "127.0.0.1", "10.0.0.1", "172.16.0.1", "192.168.1.1", "169.254.169.254", "100.64.0.1", "::1", "::ffff:127.0.0.1", "fc00::1", "fe80::1", "2001:db8::1", "2002:7f00:1::1" })
    Check("reject " + address, !ProductPageClient.IsPublicAddress(IPAddress.Parse(address)));
Check("public IPv4", ProductPageClient.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
Check("public IPv6", ProductPageClient.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")));
foreach (var url in new[] { "http://example.com", "file:///etc/passwd", "https://example.com:8443", "https://user:pass@example.com", "https://127.0.0.1" })
    Check("reject URL " + url, !ProductPageClient.IsAllowedUrl(new Uri(url)));
Check("HTTPS URL", ProductPageClient.IsAllowedUrl(new Uri("https://example.com/product")));
Console.WriteLine($"{passed} tests passed.");
