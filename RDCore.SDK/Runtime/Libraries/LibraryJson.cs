using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Reads and writes the file a library is described in (<see cref="LibraryDescription"/>).
/// </summary>
/// <remarks>
/// The format is JSON with camel-case names and enumerations as strings: it is read by people in a review as much as by the platform. What is not stated is
/// left out when the file is written, so a description does not grow a line for every default; comments and trailing commas are accepted when it is read,
/// for the file that is edited by hand.
/// </remarks>
public static class LibraryJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyLists } },
    };

    // a list that has nothing in it is not stated, as nothing else that is not stated is.
    private static void SkipEmptyLists(JsonTypeInfo type)
    {
        foreach (var property in type.Properties.Where(property => property.PropertyType.IsGenericType
            && property.PropertyType.GetGenericTypeDefinition() == typeof(ImmutableArray<>)))
        {
            property.ShouldSerialize = (_, value) => value is not null
                && value.GetType().GetProperty(nameof(ImmutableArray<int>.IsDefault))!.GetValue(value) is false
                && ((System.Collections.ICollection)value).Count > 0;
        }
    }

    /// <summary>
    /// The version of the format this writes and reads.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Reads a description.
    /// </summary>
    /// <param name="json">The text of the file.</param>
    /// <exception cref="InvalidDataException">The text is not a description this reads: it is not JSON of the format, has no name, or is of a format version this does not know.</exception>
    public static LibraryDescription Read(string json)
    {
        LibraryDescription? description;
        try
        {
            description = JsonSerializer.Deserialize<LibraryDescription>(json, _options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The library description is not valid: {exception.Message}", exception);
        }

        if (description is null || string.IsNullOrWhiteSpace(description.Name))
        {
            throw new InvalidDataException("The library description states no name.");
        }

        if (description.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"The library description of '{description.Name}' is of format version {description.FormatVersion}, and this reads version {CurrentFormatVersion}.");
        }

        // a name is one type of the library, whatever its case: VBA names are not case sensitive, and a library that declares one twice is not one a project can be
        // checked against.
        var twice = description.Classes.Select(declared => declared.Name).Concat(description.Enums.Select(declared => declared.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        return twice is null
            ? description
            : throw new InvalidDataException($"The library description of '{description.Name}' declares '{twice.Key}' more than once.");
    }

    /// <summary>
    /// Writes a description as the text of its file.
    /// </summary>
    /// <param name="description">The description.</param>
    public static string Write(LibraryDescription description) => JsonSerializer.Serialize(description, _options);
}
