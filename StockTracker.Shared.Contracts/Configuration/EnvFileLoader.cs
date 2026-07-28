namespace StockTracker.Shared.Contracts.Configuration;

// builder.Configuration ".env" değerlerini içermez (appsettings.json/appsettings.*.json'dan gelmez).
// Bu yüzden her servisin Program.cs'inde, builder oluşturulmadan önce .env dosyası bulunup process
// environment'ına yüklenmeli — aksi halde Environment.GetEnvironmentVariable(...) çağrıları null döner.
public static class EnvFileLoader
{
    public static void LoadFromNearestEnvFile()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, ".env")) && Directory.GetParent(root) != null)
        {
            root = Directory.GetParent(root)!.FullName;
        }

        var envPath = Path.Combine(root, ".env");
        if (!File.Exists(envPath)) return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
    }
}
