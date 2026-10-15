using Microsoft.Win32;
using RDCore.External.Settings;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Settings;

/// <summary>
/// The settings of the legacy members where MS-VBA keeps them: the Windows Registry, under
/// <c>HKEY_CURRENT_USER\Software\VB and VBA Program Settings\appName\section</c>, each key a string value.
/// </summary>
/// <remarks>
/// What a workspace gets when it asks for <c>LegacyRegistrySettings</c> on Windows, so that a program shares its settings with the MS-VBA programs that read and
/// write the same ones.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RegistryProgramSettings : IProgramSettings
{
    /// <summary>
    /// The key of <c>HKEY_CURRENT_USER</c> that MS-VBA keeps every application's settings under.
    /// </summary>
    public const string RootKey = @"Software\VB and VBA Program Settings";

    /// <inheritdoc/>
    public string? Get(string appName, string section, string key)
    {
        using var opened = Registry.CurrentUser.OpenSubKey(PathOf(appName, section));
        return opened?.GetValue(Name(key))?.ToString();
    }

    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, string>>? GetAll(string appName, string section)
    {
        using var opened = Registry.CurrentUser.OpenSubKey(PathOf(appName, section));
        return opened is null
            ? null
            : [.. opened.GetValueNames().Where(name => name.Length > 0).Select(name => KeyValuePair.Create(name, opened.GetValue(name)?.ToString() ?? string.Empty))];
    }

    /// <inheritdoc/>
    public void Save(string appName, string section, string key, string value)
    {
        using var created = Registry.CurrentUser.CreateSubKey(PathOf(appName, section), writable: true);
        created.SetValue(Name(key), value, RegistryValueKind.String);
    }

    /// <inheritdoc/>
    public bool Delete(string appName, string? section = null, string? key = null)
    {
        if (section is null)
        {
            return DeleteTree(PathOf(appName));
        }

        if (key is null)
        {
            return DeleteTree(PathOf(appName, section));
        }

        using var opened = Registry.CurrentUser.OpenSubKey(PathOf(appName, section), writable: true);
        if (opened?.GetValue(Name(key)) is null)
        {
            return false;
        }

        opened.DeleteValue(Name(key));
        return true;
    }

    private static bool DeleteTree(string path)
    {
        using (var opened = Registry.CurrentUser.OpenSubKey(path))
        {
            if (opened is null)
            {
                return false;
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(path);
        return true;
    }

    private static string PathOf(params string[] names) => string.Join('\\', [RootKey, .. names.Select(Name)]);

    // a key path with a '\' in a name of it would be two keys, and a name with nothing in it is the default value - which is not a setting.
    private static string Name(string name)
        => string.IsNullOrWhiteSpace(name) || name.Contains('\\')
            ? throw new ArgumentException($"'{name}' is not the name of an application, a section or a key: it is empty, or has a '\\' in it.")
            : name;
}
