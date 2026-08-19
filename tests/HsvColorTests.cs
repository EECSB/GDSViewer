using System.Globalization;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///The color model behind the inline picker.
///
///Worth testing directly rather than through the browser, because it is where a picker goes wrong quietly:
///a hue that drifts a degree per drag, or a gray that comes back with a hue attached, looks like nothing
///until a color cannot be got back to.
///</summary>
public class HsvColorTests
{
    #region Round trips ****************************************************************

    ///<summary>
    ///Every color the app can store has to survive being taken apart and put back together, or dragging
    ///the saturation to one end and back would not return the color it started at.
    ///</summary>
    [Theory]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#ff0000")]
    [InlineData("#00ff00")]
    [InlineData("#0000ff")]
    [InlineData("#ffff00")]
    [InlineData("#00ffff")]
    [InlineData("#ff00ff")]
    [InlineData("#808080")]
    [InlineData("#b30000")]
    [InlineData("#5ad45a")]
    [InlineData("#8be04e")]
    [InlineData("#123456")]
    public void A_color_survives_the_trip_through_hue_saturation_and_value(string hex)
    {
        Assert.Equal(hex, HsvColor.FromHex(hex).ToHex());
    }

    ///<summary>
    ///The whole palette, since that is what a layer starts on and what "reset to palette" puts back. One
    ///of 255 rounding badly would be a color the picker could not return a layer to.
    ///</summary>
    [Fact]
    public void Every_color_in_the_palette_round_trips()
    {
        var gds = new GdsII.GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        foreach (var layer in gds.AdditionalInformation.Layers.Values)
            Assert.Equal(layer.Color, HsvColor.FromHex(layer.Color).ToHex());
    }

    #endregion *************************************************************************



    #region What the parts mean ********************************************************

    [Theory]
    [InlineData("#ff0000", 0)]
    [InlineData("#ffff00", 60)]
    [InlineData("#00ff00", 120)]
    [InlineData("#00ffff", 180)]
    [InlineData("#0000ff", 240)]
    [InlineData("#ff00ff", 300)]
    public void Hue_is_where_the_color_sits_on_the_wheel(string hex, double expected)
    {
        Assert.Equal(expected, HsvColor.FromHex(hex).Hue, 3);
    }

    ///<summary>
    ///A gray has no hue to report, and reporting one anyway is the bug this pins: the slider would jump as
    ///soon as the saturation reached zero, so dragging in and back out would land on a different color.
    ///</summary>
    [Theory]
    [InlineData("#000000")]
    [InlineData("#808080")]
    [InlineData("#ffffff")]
    public void A_gray_has_no_saturation_and_leaves_the_hue_alone(string hex)
    {
        var color = HsvColor.FromHex(hex);

        Assert.Equal(0, color.Saturation, 3);
        Assert.Equal(0, color.Hue, 3);
    }

    [Fact]
    public void Value_is_the_brightest_channel()
    {
        Assert.Equal(1, HsvColor.FromHex("#ff8080").Value, 3);
        Assert.Equal(0.5, HsvColor.FromHex("#804020").Value, 2);
        Assert.Equal(0, HsvColor.FromHex("#000000").Value, 3);
    }

    #endregion *************************************************************************



    #region The channels beside the picker *********************************************

    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#ff0000", 255, 0, 0)]
    [InlineData("#123456", 18, 52, 86)]
    [InlineData("#b30000", 179, 0, 0)]
    public void The_channels_are_the_color_as_three_numbers(string hex, int red, int green, int blue)
    {
        Assert.Equal((red, green, blue), HsvColor.FromHex(hex).ToRgb());
    }

    ///<summary>
    ///Typing a channel and reading it back has to give the same number, or a box would fight whoever is
    ///typing in it - nudge the red up by one, and the color it lands on reports a different red.
    ///</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(179, 0, 0)]
    [InlineData(18, 52, 86)]
    [InlineData(1, 2, 3)]
    [InlineData(254, 128, 7)]
    public void A_typed_channel_survives_being_read_back(int red, int green, int blue)
    {
        Assert.Equal((red, green, blue), HsvColor.FromRgb(red, green, blue).ToRgb());
    }

    ///<summary>A box can be typed past its ends, or emptied into a negative, so the value is brought back.</summary>
    [Fact]
    public void A_channel_past_its_ends_is_brought_back()
    {
        Assert.Equal((0, 0, 0), HsvColor.FromRgb(-5, -1, -900).ToRgb());
        Assert.Equal((255, 255, 255), HsvColor.FromRgb(256, 300, 99999).ToRgb());
    }

    #endregion *************************************************************************



    #region Refusing to fall over ******************************************************

    ///<summary>
    ///Anything unreadable comes back black rather than throwing. This parses whatever is in storage or on
    ///an element, and a picker that will not open because one color is malformed is worse than one that
    ///opens on the wrong color.
    ///</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#f00")]
    [InlineData("#gggggg")]
    [InlineData("#1234567")]
    public void Something_that_is_not_a_color_reads_as_black(string? hex)
    {
        Assert.Equal("#000000", HsvColor.FromHex(hex).ToHex());
    }

    ///<summary>A drag runs past both ends, so the constructor has to bring it back rather than trust it.</summary>
    [Fact]
    public void Values_past_the_ends_are_brought_back()
    {
        Assert.Equal("#ffffff", new HsvColor(0, -1, 5).ToHex());
        Assert.Equal("#000000", new HsvColor(0, 2, -3).ToHex());

        //Hue wraps rather than clamping - 360 and 0 are the same place, and a drag can pass either.
        Assert.Equal(HsvColor.FromHex("#ff0000").ToHex(), new HsvColor(360, 1, 1).ToHex());
        Assert.Equal(HsvColor.FromHex("#ff0000").ToHex(), new HsvColor(-360, 1, 1).ToHex());
    }

    ///<summary>Written the same in any culture, like everything else this app stores.</summary>
    [Fact]
    public void A_color_is_written_the_same_in_any_culture()
    {
        string invariant = HsvColor.FromHex("#123456").ToHex();
        string hostile = GdsTestData.UnderHostileCulture(() => HsvColor.FromHex("#123456").ToHex());

        Assert.Equal(invariant, hostile);
    }

    #endregion *************************************************************************
}
