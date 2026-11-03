using System.Xml.Linq;
using GDSViewer.Models;
using GdsII;

namespace GDSViewer.Tests;

///
///The drawings the Generator's numbers are typed into - see MarkFigures (2026-11-01, asked for). What is held here is
///what would make one unusable rather than plain: a kind left without a drawing of its own, a number with no box or
///nothing drawn for it to light, a box off the drawing or over another, and markup the page cannot put down. How
///each one reads is looked at in the browser - marks.spec, and the generator screenshot.
///
public class MarkFiguresTests
{
    public static IEnumerable<object[]> EveryMark()
    {
        return Marks.All.Select(maker => new object[] { maker.Key });
    }

    [Theory]
    [MemberData(nameof(EveryMark))]
    public void Every_kind_has_a_drawing_of_its_own_with_a_box_and_a_dimension_for_every_number(string key)
    {
        var maker = Marks.Named(key)!;
        var figure = MarkFigures.For(maker);

        Assert.True(MarkFigures.Drawn(maker), $"The {maker.Name} has no drawing of its own.");
        Assert.Equal(maker.Settings.Select(setting => setting.Name).OrderBy(name => name), figure.Chips.Keys.OrderBy(name => name));

        var svg = XElement.Parse(figure.Svg);

        Assert.Equal(MarkFigure.Width.ToString(), svg.Attribute("width")?.Value);
        Assert.Equal(MarkFigure.Height.ToString(), svg.Attribute("height")?.Value);

        //The structure itself, and a group per number - numbered as the settings are, which is how the page lights one.
        Assert.Contains(svg.Elements(), group => group.Attribute("class")?.Value == "markEdges" && group.HasElements);

        for (int index = 0; index < maker.Settings.Count; index++)
        {
            string number = $"markDim markDim{index}";

            Assert.Contains(svg.Elements(), group => group.Attribute("class")?.Value == number && group.HasElements);
        }

        Assert.DoesNotContain(svg.Elements(), group => group.Attribute("class")?.Value == $"markDim markDim{maker.Settings.Count}");
    }

    ///<summary>Every box on the drawing and clear of every other, at the size the page draws one.</summary>
    [Theory]
    [MemberData(nameof(EveryMark))]
    public void Every_box_is_on_the_drawing_and_clear_of_every_other(string key)
    {
        var maker = Marks.Named(key)!;
        var boxes = MarkFigures.For(maker).Chips.Select(chip => (Name: chip.Key, Box: boxAround(chip.Value))).ToList();

        foreach (var (name, box) in boxes)
        {
            Assert.True(box.Left >= 0 && box.Top >= 0, $"The {maker.Name}'s {name} box runs off the drawing's top or left.");
            Assert.True(box.Right <= MarkFigure.Width && box.Bottom <= MarkFigure.Height, $"The {maker.Name}'s {name} box runs off the drawing's bottom or right.");
        }

        for (int first = 0; first < boxes.Count; first++)
        {
            for (int second = first + 1; second < boxes.Count; second++)
            {
                var a = boxes[first].Box;
                var b = boxes[second].Box;
                bool apart = a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top;

                Assert.True(apart, $"The {maker.Name}'s {boxes[first].Name} and {boxes[second].Name} boxes overlap.");
            }
        }
    }

    [Fact]
    public void A_drawing_is_written_the_same_whatever_the_browsers_language()
    {
        var maker = Marks.Named("ratrace")!;
        string hostile = GdsTestData.UnderHostileCulture(() => MarkFigures.Draw(maker).Svg);

        Assert.Equal(MarkFigures.Draw(maker).Svg, hostile);
        Assert.DoesNotContain("!", hostile);
    }

    [Fact]
    public void A_kind_is_drawn_once_and_kept()
    {
        var maker = Marks.Named("spiral")!;

        Assert.Same(MarkFigures.For(maker), MarkFigures.For(maker));
    }

    private static (double Left, double Top, double Right, double Bottom) boxAround((int X, int Y) middle)
    {
        double halfWide = MarkFigure.ChipWidth / 2.0;
        double halfHigh = MarkFigure.ChipHeight / 2.0;

        return (middle.X - halfWide, middle.Y - halfHigh, middle.X + halfWide, middle.Y + halfHigh);
    }
}
