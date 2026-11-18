using System.IO;
using System.Text.Json;

namespace Kandoo.App.Launcher;

internal sealed class LocalProfileStore : IUserProfileStore
{
    private readonly string _path;

    public LocalProfileStore(string? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(folderPath);
        _path = Path.Combine(folderPath, "kandoo.local-profile.role.v1.json");
    }

    public LocalUserProfile? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var json = File.ReadAllText(_path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("role", out var roleProp))
            {
                var roleStr = roleProp.GetString();
                if (AppRolePolicy.TryParse(roleStr, out var role))
                {
                    return new LocalUserProfile(role);
                }
            }
        }
        catch { }

        return null;
    }

    public void Save(LocalUserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var roleStr = AppRolePolicy.Serialize(profile.Role);
        var json = JsonSerializer.Serialize(new { role = roleStr });
        File.WriteAllText(_path, json);
    }
}
