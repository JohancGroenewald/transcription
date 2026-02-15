namespace VoiceType.Tests;

public class PretextDetectorTests
{
    [Theory]
    [InlineData("Hello<flow>ignore</flow>world", "Hello world")]
    [InlineData("Hello <flow>ignore</flow> world", "Hello world")]
    [InlineData("Hello<flow>ignore</flow>!", "Hello!")]
    [InlineData("(<flow>ignore</flow>test)", "(test)")]
    [InlineData("A<flow>x</flow>B<flow>y</flow>C", "A B C")]
    [InlineData("Hello<FLOW>ignore</FLOW>world", "Hello world")]
    public void StripFlowDirectives_RemovesDirectiveBlocks(string input, string expected)
    {
        var result = PretextDetector.StripFlowDirectives(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void StripFlowDirectives_ReturnsInput_WhenNoDirectivesPresent()
    {
        const string input = "This is normal text.";

        var result = PretextDetector.StripFlowDirectives(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void StripFlowDirectives_DropsRemainder_WhenClosingTagMissing()
    {
        const string input = "Hello<flow>ignore the rest";

        var result = PretextDetector.StripFlowDirectives(input);

        Assert.Equal("Hello", result);
    }
}

