using System.Globalization;
using System.Text.Json;

namespace DCF.Api.Charts;

/// <summary>
/// Raised when a chart request carries missing or malformed parameters. The engine turns
/// this into a 400 rather than letting it bubble out as a 500.
/// </summary>
public class ChartParameterException(string message) : Exception(message);

/// <summary>
/// Loosely-typed parameter bag for chart requests. Charts read what they need out of it,
/// so adding a chart never changes the request contract.
/// </summary>
public sealed class ChartParameters(IReadOnlyDictionary<string, JsonElement>? values = null)
{
    private readonly Dictionary<string, JsonElement> values =
        new(values ?? new Dictionary<string, JsonElement>(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, JsonElement> Values => values;

    public static ChartParameters FromObjects(IReadOnlyDictionary<string, object?> source)
    {
        var converted = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in source)
        {
            converted[key] = JsonSerializer.SerializeToElement(value);
        }

        return new ChartParameters(converted);
    }

    public bool TryGet(string name, out JsonElement value)
    {
        return values.TryGetValue(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    public Guid GetGuid(string name)
    {
        return TryGetGuid(name, out var value)
            ? value
            : throw new ChartParameterException($"Parameter '{name}' is required and must be a GUID.");
    }

    public bool TryGetGuid(string name, out Guid result)
    {
        result = Guid.Empty;

        return TryGet(name, out var element) && TryReadGuid(element, out result);
    }

    /// <summary>Reads a list of GUIDs, accepting either a JSON array or a single scalar.</summary>
    public IReadOnlyList<Guid> GetGuidList(string name, bool required)
    {
        if (!TryGet(name, out var element))
        {
            return required
                ? throw new ChartParameterException($"Parameter '{name}' is required and must be a list of GUIDs.")
                : [];
        }

        var result = new List<Guid>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!TryReadGuid(item, out var parsed))
                {
                    throw new ChartParameterException($"Parameter '{name}' must contain only GUIDs.");
                }

                result.Add(parsed);
            }
        }
        else if (TryReadGuid(element, out var single))
        {
            result.Add(single);
        }
        else
        {
            throw new ChartParameterException($"Parameter '{name}' must be a list of GUIDs.");
        }

        var distinct = result.Distinct().ToList();

        if (required && distinct.Count == 0)
        {
            throw new ChartParameterException($"Parameter '{name}' must contain at least one GUID.");
        }

        return distinct;
    }

    public int? GetInt(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ChartParameterException($"Parameter '{name}' must be an integer.");
    }

    public string? GetString(string name)
    {
        return TryGet(name, out var element)
            ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString()
            : null;
    }

    public bool? GetBool(string name)
    {
        if (!TryGet(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => throw new ChartParameterException($"Parameter '{name}' must be a boolean.")
        };
    }

    private static bool TryReadGuid(JsonElement element, out Guid result)
    {
        result = Guid.Empty;

        return element.ValueKind switch
        {
            JsonValueKind.String => Guid.TryParse(element.GetString(), out result),
            _ => false
        };
    }
}
