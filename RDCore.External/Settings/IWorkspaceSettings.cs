namespace RDCore.External.Settings;

/// <summary>
/// The <em>workspace application settings</em> (<strong>RD-VBAL §2.1.1.1</strong>): the managed configuration of the workspace's <c>.json</c> files, addressed by
/// configuration path (<c>"Configuration:ConnectionStrings:AppDb"</c>). What <c>GetJsonSetting</c>, <c>GetAllJsonSettings</c> and <c>SaveJsonSetting</c> read and
/// write, and what the legacy settings members use unless the workspace asks for the registry.
/// </summary>
/// <remarks>
/// A path is read as an <c>IConfiguration</c> reads one: its sections separated by <c>:</c>, without regard to case.
/// </remarks>
public interface IWorkspaceSettings
{
    /// <summary>
    /// The value of a setting.
    /// </summary>
    /// <param name="path">The configuration path of the setting.</param>
    /// <param name="file">The configuration file, relative to the workspace; <see langword="null"/> for the default one.</param>
    /// <returns>The value, or <see langword="null"/> when there is no such setting, or it is a section with no value of its own.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is not a <c>.json</c> file of the workspace.</exception>
    /// <exception cref="FormatException">The file is not JSON.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    string? Get(string path, string? file = null);

    /// <summary>
    /// The settings of a section: the key and the value of each child that has a value.
    /// </summary>
    /// <param name="path">The configuration path of the section.</param>
    /// <param name="file">The configuration file, relative to the workspace; <see langword="null"/> for the default one.</param>
    /// <returns>The settings, or <see langword="null"/> when there is no such section.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is not a <c>.json</c> file of the workspace.</exception>
    /// <exception cref="FormatException">The file is not JSON.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    IReadOnlyList<KeyValuePair<string, string>>? GetAll(string path, string? file = null);

    /// <summary>
    /// Saves a setting, creating the sections of its path - and the file - as needed.
    /// </summary>
    /// <param name="path">The configuration path of the setting.</param>
    /// <param name="value">Its value.</param>
    /// <param name="file">The configuration file, relative to the workspace; <see langword="null"/> for the default one.</param>
    /// <exception cref="ArgumentException"><paramref name="file"/> is not a <c>.json</c> file of the workspace, or the path is empty.</exception>
    /// <exception cref="InvalidOperationException">A section of the path is a value, which a setting cannot be under.</exception>
    /// <exception cref="FormatException">The file is not JSON.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    void Save(string path, string value, string? file = null);

    /// <summary>
    /// Deletes a setting, or a section with every setting under it.
    /// </summary>
    /// <param name="path">The configuration path of the setting or section.</param>
    /// <param name="file">The configuration file, relative to the workspace; <see langword="null"/> for the default one.</param>
    /// <returns><see langword="false"/> when there was no such setting or section.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is not a <c>.json</c> file of the workspace.</exception>
    /// <exception cref="FormatException">The file is not JSON.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    bool Delete(string path, string? file = null);
}

/// <summary>
/// The settings the legacy members read and write - <c>SaveSetting</c>, <c>GetSetting</c>, <c>GetAllSettings</c>, <c>DeleteSetting</c> - addressed as MS-VBA
/// addresses them: an application, a section of it, and a key.
/// </summary>
/// <remarks>
/// MS-VBA keeps them in the Windows Registry, under <c>HKEY_CURRENT_USER\Software\VB and VBA Program Settings</c>. RD-VBA keeps them in the workspace application
/// settings (<see cref="JsonProgramSettings"/>), unless the workspace asks for the registry and the platform has one.
/// </remarks>
public interface IProgramSettings
{
    /// <summary>The value of a key, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="ArgumentException">A name is not one a setting can have.</exception>
    string? Get(string appName, string section, string key);

    /// <summary>The keys of a section and their values, or <see langword="null"/> when there is no such section.</summary>
    /// <exception cref="ArgumentException">A name is not one a setting can have.</exception>
    IReadOnlyList<KeyValuePair<string, string>>? GetAll(string appName, string section);

    /// <summary>Saves the value of a key, creating its application and section as needed.</summary>
    /// <exception cref="ArgumentException">A name is not one a setting can have.</exception>
    void Save(string appName, string section, string key, string value);

    /// <summary>
    /// Deletes a key; with no key, a section and every key of it; with no section either, every section of the application.
    /// </summary>
    /// <returns><see langword="false"/> when there was nothing to delete.</returns>
    /// <exception cref="ArgumentException">A name is not one a setting can have.</exception>
    bool Delete(string appName, string? section = null, string? key = null);
}

/// <summary>
/// The settings of a runtime that has none: nothing is read, and what is saved is not kept.
/// </summary>
/// <remarks>
/// What an analysis runs with, so that code that saves a setting can be analyzed without the analysis writing to anything.
/// </remarks>
public sealed class NoSettings : IWorkspaceSettings, IProgramSettings
{
    /// <summary>The one there is: it has nothing to hold.</summary>
    public static NoSettings Instance { get; } = new();

    private NoSettings() { }

    string? IWorkspaceSettings.Get(string path, string? file) => null;

    IReadOnlyList<KeyValuePair<string, string>>? IWorkspaceSettings.GetAll(string path, string? file) => null;

    void IWorkspaceSettings.Save(string path, string value, string? file) { }

    bool IWorkspaceSettings.Delete(string path, string? file) => false;

    string? IProgramSettings.Get(string appName, string section, string key) => null;

    IReadOnlyList<KeyValuePair<string, string>>? IProgramSettings.GetAll(string appName, string section) => null;

    void IProgramSettings.Save(string appName, string section, string key, string value) { }

    bool IProgramSettings.Delete(string appName, string? section, string? key) => false;
}
