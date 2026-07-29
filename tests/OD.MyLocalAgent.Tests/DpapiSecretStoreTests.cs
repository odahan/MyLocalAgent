using System.Text;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies per-user DPAPI secret persistence without plaintext leakage.
/// </summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies set, reopen, get, exists, remove, and encrypted-at-rest behavior.
    /// </summary>
    [Fact]
    public async Task SecretsRoundTripAcrossInstancesAndRemainEncrypted()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "secrets.dat");
        var firstStore = new DpapiSecretStore(path);

        await firstStore.SetAsync(
            SecretKeys.OpenAIApiKey,
            "top-secret-value",
            CancellationToken.None);
        var rawFile = await File.ReadAllBytesAsync(path);
        var secondStore = new DpapiSecretStore(path);

        (await secondStore.ExistsAsync(
            SecretKeys.OpenAIApiKey,
            CancellationToken.None)).Should().BeTrue();
        (await secondStore.GetAsync(
            SecretKeys.OpenAIApiKey,
            CancellationToken.None)).Should().Be("top-secret-value");
        Encoding.UTF8.GetString(rawFile).Should().NotContain("top-secret-value");
        (await secondStore.RemoveAsync(
            SecretKeys.OpenAIApiKey,
            CancellationToken.None)).Should().BeTrue();
        (await secondStore.ExistsAsync(
            SecretKeys.OpenAIApiKey,
            CancellationToken.None)).Should().BeFalse();
    }

    /// <summary>
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
