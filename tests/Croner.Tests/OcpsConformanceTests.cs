using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Croner.Tests;

/// <summary>
/// Runs the conformance test vectors of the Open Cron Pattern Specification (OCPS 1.0 to 1.4),
/// see <c>Ocps/README.md</c>.
/// </summary>
/// <remarks>
/// OCPS is a guideline: where croner for JavaScript and croner-rust deviate from it, this library
/// follows them, and the vector is listed in <see cref="Deviations"/> with the reason.
/// </remarks>
public class OcpsConformanceTests
{
    /// <summary>Vectors where Croner deliberately differs from OCPS, with the reason.</summary>
    private static readonly Dictionary<string, string> Deviations = new()
    {
        ["ocps-1.1-nicknames-are-case-sensitive"] =
            "Nicknames are case-insensitive (@Daily is @daily), as in croner for JavaScript and croner-rust.",
        ["ocps-1.1-reboot-parse-and-runtime-handling"] =
            "@reboot is an event, not a time: parsing rejects it with a clear error, as croner for JavaScript does.",
    };

    private static readonly JsonElement Versions = LoadVersions();

    private static JsonElement LoadVersions()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Ocps", "conformance-tests.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("versions").Clone();
    }

    public static TheoryData<string, string> Vectors()
    {
        var data = new TheoryData<string, string>();
        foreach (JsonProperty version in Versions.EnumerateObject())
        {
            foreach (JsonElement test in version.Value.GetProperty("tests").EnumerateArray())
            {
                data.Add(version.Name, test.GetProperty("id").GetString()!);
            }
        }
        return data;
    }

    private static JsonElement Find(string version, string id) =>
        Versions.GetProperty(version).GetProperty("tests").EnumerateArray().Single(t => t.GetProperty("id").GetString() == id);

    private static DateTime ParseTime(JsonElement value) =>
        DateTime.ParseExact(value.GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Vector(string version, string id)
    {
        JsonElement test = Find(version, id);
        string pattern = test.GetProperty("pattern").GetString()!;
        JsonElement expectations = test.GetProperty("expectations");
        bool parsed = Cron.TryParse(pattern, out Cron? cron);

        if (Deviations.TryGetValue(id, out string? reason))
        {
            Assert.False(string.IsNullOrEmpty(reason));
            // A deviation still has to be the one documented: accepted where OCPS rejects, or the other way round.
            if (expectations.TryGetProperty("parse", out JsonElement deviatingParse))
            {
                Assert.Equal(deviatingParse.GetProperty("result").GetString() != "accept", parsed);
            }
            return;
        }

        switch (test.GetProperty("type").GetString())
        {
            case "parse-accept":
                Assert.True(parsed, $"{pattern} should parse");
                break;

            case "parse-reject":
                Assert.False(parsed, $"{pattern} should be rejected");
                break;

            case "datetime-match":
                Assert.True(parsed, $"{pattern} should parse");
                foreach (JsonElement time in Times(expectations, "mustMatch"))
                {
                    Assert.True(cron!.IsMatch(ParseTime(time)), $"{pattern} should match {time}");
                }
                foreach (JsonElement time in Times(expectations, "mustNotMatch"))
                {
                    Assert.False(cron!.IsMatch(ParseTime(time)), $"{pattern} should not match {time}");
                }
                break;

            case "portability":
                // Years before 1970 are non-portable unless documented: Croner documents years 1 to 5000.
                Assert.True(parsed, $"{pattern} should parse, as the documented year range includes it");
                break;

            default:
                Assert.Fail($"Unknown vector type in {id}; add support for it.");
                break;
        }
    }

    private static IEnumerable<JsonElement> Times(JsonElement expectations, string name) =>
        expectations.TryGetProperty(name, out JsonElement list) ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
