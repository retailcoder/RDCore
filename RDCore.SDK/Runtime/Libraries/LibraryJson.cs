using System.Text.Json;
using System.Text.Json.Serialization;

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
    };

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

        return description.FormatVersion == CurrentFormatVersion
            ? description
            : throw new InvalidDataException(
                $"The library description of '{description.Name}' is of format version {description.FormatVersion}, and this reads version {CurrentFormatVersion}.");
    }

    /// <summary>
    /// Writes a description as the text of its file.
    /// </summary>
    /// <param name="description">The description.</param>
    public static string Write(LibraryDescription description) => JsonSerializer.Serialize(description, _options);
}
