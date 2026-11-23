using Shirabe.Client.Services;
using Xunit;

namespace Shirabe.Tests;

public class DebugLogTests
{
    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("the reason");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void A_thrown_exception_is_described_by_its_type_message_and_where_it_was_thrown()
    {
        var text = DebugLog.Describe("thrown", Thrown(), fullTrace: false);

        Assert.StartsWith("[Shirabe] thrown: InvalidOperationException: the reason", text);
        Assert.Contains(nameof(Thrown), text);
    }

    [Fact]
    public void An_unhandled_exception_is_written_whole_with_its_inner_exception()
    {
        var text = DebugLog.Describe("UNHANDLED", new IOException("outer", Thrown()), fullTrace: true);

        Assert.Contains("outer", text);
        Assert.Contains("the reason", text);
    }
}
