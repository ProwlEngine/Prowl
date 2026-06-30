using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace MobaGame;

public class AccountData
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
}

public static class AccountManager
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "accounts.json");
    private static Dictionary<string, AccountData> _accounts = new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            _accounts = JsonConvert.DeserializeObject<Dictionary<string, AccountData>>(json)
                        ?? new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        var json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public static (bool success, string error) Register(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return (false, "Username must be at least 3 characters.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return (false, "Password must be at least 4 characters.");
        if (_accounts.ContainsKey(username))
            return (false, "Username already taken.");

        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);

        _accounts[username] = new AccountData {
            Username = username,
            PasswordHash = hash,
            Salt = salt
        };
        Save();
        return (true, "");
    }

    public static (bool success, string error) Login(string username, string password)
    {
        if (!_accounts.TryGetValue(username, out var account))
            return (false, "Invalid username or password.");

        var hash = HashPassword(password, account.Salt);
        if (hash != account.PasswordHash)
            return (false, "Invalid username or password.");

        return (true, "");
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        var combined = Encoding.UTF8.GetBytes(salt + password);
        var hash = SHA256.HashData(combined);
        return Convert.ToBase64String(hash);
    }
}
