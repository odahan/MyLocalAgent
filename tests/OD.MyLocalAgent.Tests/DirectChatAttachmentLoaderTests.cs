using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies content-based Direct Chat attachment detection.</summary>
public sealed class DirectChatAttachmentLoaderTests
{
    /// <summary>Verifies a source file is accepted without explicitly whitelisting its extension.</summary>
    [Fact]
    public async Task LoadAsyncAcceptsTextWithAnUnlistedExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.source");
        try
        {
            await File.WriteAllTextAsync(path, "public sealed class Example { }\n");

            var attachment = await DirectChatAttachmentLoader.LoadAsync(path);

            attachment.Kind.Should().Be(DirectChatAttachmentKind.Text);
            attachment.Text.Should().Be("public sealed class Example { }\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies binary files receive a user-facing rejection.</summary>
    [Fact]
    public async Task LoadAsyncRejectsBinaryFilesWithAClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, [0, 159, 146, 150]);

            var action = () => DirectChatAttachmentLoader.LoadAsync(path);

            await action.Should().ThrowAsync<DirectChatAttachmentException>()
                .WithMessage("*appears to be binary*");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
