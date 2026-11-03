using GdsII;

namespace GDSViewer.Tests;

///
///A layer's pair as text, both ways: `65/20` is how KLayout, Magic, a layermap and every tool here writes one, and
///<see cref="LayerKey.TryParse"/> is the one grammar for reading it back - the CLI's filters and operations, the rule
///decks and the assistant each had a parser of their own until 2026-11-03, and they disagreed about `65/?`.
///
public class LayerKeyTests
{
    [Theory]
    [InlineData("65/20", 65, 20)]
    [InlineData(" 65 / 20 ", 65, 20)]
    [InlineData("0/0", 0, 0)]
    [InlineData("32767/32767", 32767, 32767)]
    public void A_pair_reads_as_its_two_numbers(string text, int number, int dataType)
    {
        Assert.True(LayerKey.TryParse(text, out LayerKey key));
        Assert.Equal(new LayerKey((short)number, (short)dataType), key);
    }

    ///<summary>The way a data type nobody could read is written, so a list that printed one reads back as it was.</summary>
    [Fact]
    public void A_question_mark_is_the_unknown_data_type()
    {
        Assert.True(LayerKey.TryParse("65/?", out LayerKey key));
        Assert.Equal(new LayerKey(65, LayerKey.UnknownDataType), key);
        Assert.Equal("65/?", key.ToString());
    }

    [Theory]
    [InlineData("65")]
    [InlineData("65/20/1")]
    [InlineData("-1/0")]
    [InlineData("65/-1")]
    [InlineData("a/b")]
    [InlineData("65/")]
    [InlineData("/20")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_pair(string? text)
    {
        Assert.False(LayerKey.TryParse(text, out _));
    }

    ///<summary>Invariant both ways: a culture whose minus sign is not one would write the unknown data type's -1 into a deck or a file name.</summary>
    [Fact]
    public void The_pair_is_written_and_read_invariantly()
    {
        string written = GdsTestData.UnderHostileCulture(() => new LayerKey(65, LayerKey.UnknownDataType).ToString());

        Assert.Equal("65/?", written);
        Assert.Equal("65/20", GdsTestData.UnderHostileCulture(() => new LayerKey(65, 20).ToString()));
        Assert.True(GdsTestData.UnderHostileCulture(() => LayerKey.TryParse("65/20", out _)));
    }
}
