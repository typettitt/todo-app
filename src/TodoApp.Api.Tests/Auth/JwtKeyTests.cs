using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using TodoApp.Api.Features.Auth;

namespace TodoApp.Api.Tests.Auth;

[Trait("Category", "Auth")]
public sealed class JwtKeyTests
{
    [Fact]
    public void JwtKey_ProductionEnv_NoEnvVar_Startup_Throws()
    {
        var priorKey = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvVarName);
        var priorKeyFile = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvFileVarName);
        try
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, null);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, null);

            using var factory = new TestWebApplicationFactory(environmentName: "Production");

            var act = () => factory.CreateClient();
            act.Should().Throw<Exception>()
                .Where(e => e is InvalidOperationException
                            || (e.InnerException is InvalidOperationException));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, priorKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, priorKeyFile);
        }
    }

    [Fact]
    public async Task JwtKey_ProductionEnv_FileEnv_UsesMountedSecret()
    {
        var priorKey = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvVarName);
        var priorKeyFile = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvFileVarName);
        var keyFile = Path.Combine(Path.GetTempPath(), "todoapp-tests-jwt-" + Guid.NewGuid());
        var configuredKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        try
        {
            await File.WriteAllTextAsync(keyFile, configuredKey + Environment.NewLine);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, null);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, keyFile);

            await using var factory = new TestWebApplicationFactory(environmentName: "Production");
            var client = factory.CreateClient(); // forces host bootstrap
            _ = await client.GetAsync(new Uri("/", UriKind.Relative));

            using var scope = factory.Services.CreateScope();
            var keyProvider = scope.ServiceProvider.GetRequiredService<JwtKeyProvider>();
            keyProvider.Source.Should().Be("env-file");
            keyProvider.Key.Should().Equal(Convert.FromBase64String(configuredKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, priorKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, priorKeyFile);
            File.Delete(keyFile);
        }
    }

    [Fact]
    public async Task JwtKey_ProductionEnv_FileEnv_TakesPrecedenceOverRawEnv()
    {
        var priorKey = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvVarName);
        var priorKeyFile = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvFileVarName);
        var keyFile = Path.Combine(Path.GetTempPath(), "todoapp-tests-jwt-" + Guid.NewGuid());
        var fileKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        var rawEnvKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        try
        {
            await File.WriteAllTextAsync(keyFile, fileKey + Environment.NewLine);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, rawEnvKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, keyFile);

            await using var factory = new TestWebApplicationFactory(environmentName: "Production");
            var client = factory.CreateClient(); // forces host bootstrap
            _ = await client.GetAsync(new Uri("/", UriKind.Relative));

            using var scope = factory.Services.CreateScope();
            var keyProvider = scope.ServiceProvider.GetRequiredService<JwtKeyProvider>();
            keyProvider.Source.Should().Be("env-file");
            keyProvider.Key.Should().Equal(Convert.FromBase64String(fileKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, priorKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, priorKeyFile);
            File.Delete(keyFile);
        }
    }

    [Fact]
    public async Task JwtKey_DevEnv_GeneratesAndPersistsFile()
    {
        // Provide a fresh content root so the dev key location is isolated.
        var contentRoot = Path.Combine(Path.GetTempPath(), "todoapp-tests-jwt-" + Guid.NewGuid());
        Directory.CreateDirectory(contentRoot);
        try
        {
            await using var factory = new TestWebApplicationFactory();
            var client = factory.CreateClient(); // forces host bootstrap
            _ = await client.GetAsync(new Uri("/", UriKind.Relative));

            using var scope = factory.Services.CreateScope();
            var keyProvider = scope.ServiceProvider.GetRequiredService<JwtKeyProvider>();
            keyProvider.Source.Should().Be("dev-file");

            var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            var keyPath = Path.Combine(env.ContentRootPath, ".aspnet", "keys", "jwt.key");
            File.Exists(keyPath).Should().BeTrue();
            new FileInfo(keyPath).Length.Should().BeGreaterThanOrEqualTo(32);
        }
        finally
        {
            try
            {
                Directory.Delete(contentRoot, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // already gone
            }
        }
    }

    [Fact]
    public async Task JwtKey_StartupLog_DoesNotContainKeyBytes()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient(); // bootstrap
        _ = await client.GetAsync(new Uri("/", UriKind.Relative));

        using var scope = factory.Services.CreateScope();
        var keyProvider = scope.ServiceProvider.GetRequiredService<JwtKeyProvider>();

        var sourceLine = factory.StartupLogLines
            .FirstOrDefault(l => l.Contains("JWT signing key source:", StringComparison.Ordinal));
        sourceLine.Should().NotBeNull();
        sourceLine!.Should().Contain(keyProvider.Source);

        var base64 = Convert.ToBase64String(keyProvider.Key);
        var hex = Convert.ToHexString(keyProvider.Key);
        factory.StartupLogLines.Should().NotContain(l => l.Contains(base64, StringComparison.Ordinal));
        factory.StartupLogLines.Should().NotContain(l => l.Contains(hex, StringComparison.Ordinal));
    }
}
