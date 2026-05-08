using System.Security.Cryptography;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Source for the symmetric signing key used by <see cref="JwtTokenService"/>.
/// In any non-Development environment the key must be supplied by
/// <c>JWT_SIGNING_KEY_FILE</c> or <c>JWT_SIGNING_KEY</c> (>=32 bytes, base64 or raw).
/// In Development, a 64-byte key is generated on first run and persisted to
/// <c>{ContentRoot}/.aspnet/keys/jwt.key</c> (which is gitignored).
/// </summary>
public sealed class JwtKeyProvider
{
    public const string EnvVarName = "JWT_SIGNING_KEY";
    public const string EnvFileVarName = "JWT_SIGNING_KEY_FILE";
    private const int MinKeyBytes = 32;
    private const int DevKeyBytes = 64;

    public JwtKeyProvider(IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        (Key, Source) = Resolve(env);
    }

    public byte[] Key { get; }

    public string Source { get; }

    private static (byte[] Key, string Source) Resolve(IHostEnvironment env)
    {
        var fromEnvFile = Environment.GetEnvironmentVariable(EnvFileVarName);
        if (!string.IsNullOrWhiteSpace(fromEnvFile))
        {
            var configuredKey = File.ReadAllText(fromEnvFile).Trim();
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                throw new InvalidOperationException($"{EnvFileVarName} points to an empty key file.");
            }

            return (DecodeConfiguredKey(configuredKey, EnvFileVarName), "env-file");
        }

        var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return (DecodeConfiguredKey(fromEnv, EnvVarName), "env-var");
        }

        if (!env.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{EnvFileVarName} or {EnvVarName} is required outside Development.");
        }

        var dir = Path.Combine(env.ContentRootPath, ".aspnet", "keys");
        Directory.CreateDirectory(dir);
        RestrictPermissions(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var keyPath = Path.Combine(dir, "jwt.key");

        if (File.Exists(keyPath))
        {
            RestrictPermissions(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length >= MinKeyBytes)
            {
                return (existing, "dev-file");
            }
        }

        // Use the cryptographic RNG; this key signs every development JWT.
        var fresh = RandomNumberGenerator.GetBytes(DevKeyBytes);
        WriteAtomically(keyPath, fresh);
        return (fresh, "dev-file");
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("JWT key path must include a directory.");
        var tempPath = Path.Combine(directory, ".jwt-" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Options = FileOptions.WriteThrough,
                Share = FileShare.None,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RestrictPermissions(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tempPath, path, overwrite: true);
            RestrictPermissions(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void RestrictPermissions(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, mode);
    }

    private static byte[] DecodeConfiguredKey(string configuredKey, string sourceName)
    {
        var bytes = TryDecodeBase64(configuredKey) ?? System.Text.Encoding.UTF8.GetBytes(configuredKey);
        if (bytes.Length < MinKeyBytes)
        {
            throw new InvalidOperationException(
                $"{sourceName} must decode to at least {MinKeyBytes} bytes; got {bytes.Length}.");
        }

        return bytes;
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
