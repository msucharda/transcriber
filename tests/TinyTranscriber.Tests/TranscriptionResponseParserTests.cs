namespace TinyTranscriber.Tests;

public sealed class TranscriptionResponseParserTests
{
    [Fact]
    public void ParseUsesCombinedPhrases()
    {
        const string response = """
            {
              "combinedPhrases": [
                { "text": "Ahoj world." }
              ],
              "phrases": [
                { "text": "ignored" }
              ]
            }
            """;

        var result = TranscriptionResponseParser.Parse(response);

        Assert.Equal("Ahoj world.", result);
    }

    [Fact]
    public void ParseCombinesSegmentedPhrasesWhenNeeded()
    {
        const string response = """
            {
              "phrases": [
                { "text": "Dobrý den." },
                { "text": "Let's start." }
              ]
            }
            """;

        var result = TranscriptionResponseParser.Parse(response);

        Assert.Equal("Dobrý den. Let's start.", result);
    }

    [Fact]
    public void ParseRejectsResponseWithoutText()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => TranscriptionResponseParser.Parse("""{ "combinedPhrases": [] }"""));

        Assert.Equal("Azure Speech returned no transcript text.", exception.Message);
    }
}
