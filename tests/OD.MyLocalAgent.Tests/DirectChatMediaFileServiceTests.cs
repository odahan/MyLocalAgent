using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies explicit local media saving.</summary>
public sealed class DirectChatMediaFileServiceTests
{
    /// <summary>Verifies the exact persisted bytes are written to the selected path.</summary>
    [Fact]
    public async Task SaveWritesExactMediaBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "saved.wav");
        var data = new byte[] { 82, 73, 70, 70, 1, 2, 3 };
        try
        {
            await new DirectChatMediaFileService().SaveAsync(
                new DirectChatContent
                {
                    Position = 0,
                    Kind = DirectChatContentKind.Audio,
                    MediaType = "audio/wav",
                    Data = data,
                    SizeBytes = data.Length,
                },
                path,
                CancellationToken.None);

            (await File.ReadAllBytesAsync(path)).Should().Equal(data);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
