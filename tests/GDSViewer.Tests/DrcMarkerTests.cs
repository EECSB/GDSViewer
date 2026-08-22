using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The markup a violation becomes, which is what the 2D view lays over the drawing.
///
///Built rather than appended to the DOM afterwards, so it is a string this can read - and a string is worth
///reading rather than trusting, because a rule id comes out of somebody's text file and goes straight into
///markup a browser parses.
///</summary>
public class DrcMarkerTests
{
    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    private static DrcViolation Region(string rule, params Element.Point[] points)
    {
        return new DrcViolation
        {
            RuleId = rule,
            Description = "",
            Check = DrcCheck.Width,
            Limit = 140,
            Marker = points.ToList()
        };
    }

    private static DrcViolation Point(string rule, int x, int y)
    {
        return new DrcViolation
        {
            RuleId = rule,
            Description = "",
            Check = DrcCheck.OffGrid,
            Limit = 5,
            Marker = new List<Element.Point> { At(x, y) }
        };
    }

    #region What it draws **************************************************************

    [Fact]
    public void Nothing_found_draws_nothing_at_all()
    {
        Assert.Equal("", SvgWriter.Markers(new List<DrcViolation>()));
    }

    [Fact]
    public void A_region_becomes_a_polygon_of_its_corners()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("met1.1", At(0, 0), At(100, 0), At(100, 50), At(0, 50))
        });

        Assert.Contains("<polygon", markup);
        Assert.Contains("points=\"0,0 100,0 100,50 0,50\"", markup);
        Assert.Contains(SvgWriter.MarkerClass, markup);
    }

    ///<summary>
    ///An off-grid fault is a coordinate and has no extent, so it is a zero-length line - which a round cap
    ///draws as a dot exactly as wide as the stroke, and therefore the same size on screen at every zoom.
    ///</summary>
    [Fact]
    public void A_point_becomes_a_zero_length_line()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation> { Point("grid.1", 1975, -67) });

        Assert.Contains("<line", markup);
        Assert.Contains("x1=\"1975\" y1=\"-67\"", markup);
        Assert.Contains("x2=\"1975\" y2=\"-67\"", markup);
        Assert.Contains(SvgWriter.MarkerPointClass, markup);
    }

    [Fact]
    public void Every_marker_sits_in_one_named_group()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("met1.1", At(0, 0), At(10, 0), At(10, 10)),
            Point("grid.1", 5, 5)
        });

        Assert.StartsWith($"<g id=\"{SvgWriter.MarkersId}\">", markup);
        Assert.EndsWith("</g>", markup);
    }

    [Fact]
    public void A_marker_says_which_rule_it_broke()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("difftap.8", At(0, 0), At(10, 0), At(10, 10))
        });

        Assert.Contains($"{SvgWriter.RuleAttribute}=\"difftap.8\"", markup);
    }

    ///<summary>A marker with fewer than three corners encloses nothing, so it is drawn as the point it is.</summary>
    [Fact]
    public void A_two_point_marker_is_not_drawn_as_a_polygon()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("odd.1", At(0, 0), At(10, 10))
        });

        Assert.DoesNotContain("<polygon", markup);
        Assert.Contains("<line", markup);
    }

    [Fact]
    public void A_marker_with_no_corners_at_all_is_left_out()
    {
        Assert.Equal("", SvgWriter.Markers(new List<DrcViolation> { Region("empty.1") }));
    }

    #endregion **************************************************************************



    #region What a deck is allowed to put on the page **********************************

    ///<summary>
    ///A rule id is whatever somebody typed into a text file, and it is written straight into markup the
    ///browser parses - so it is escaped, the way any other writer of XML would.
    ///
    ///Not a hypothetical worth waving away: the deck is a file a user loads, the same route a layermap
    ///takes, and nothing between the two ever checks what an id contains.
    ///</summary>
    [Fact]
    public void A_rule_id_cannot_break_out_of_its_attribute()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("bad\"/><script>alert(1)</script>", At(0, 0), At(10, 0), At(10, 10))
        });

        Assert.DoesNotContain("<script>", markup);
        Assert.Contains("&quot;", markup);
        Assert.Contains("&lt;script&gt;", markup);
    }

    [Fact]
    public void An_ampersand_in_a_rule_id_is_escaped_once()
    {
        string markup = SvgWriter.Markers(new List<DrcViolation>
        {
            Region("a&b", At(0, 0), At(10, 0), At(10, 10))
        });

        Assert.Contains("a&amp;b", markup);
        Assert.DoesNotContain("&amp;amp;", markup);
    }

    #endregion **************************************************************************



    #region Coordinates ****************************************************************

    ///<summary>
    ///Invariant, like every other number this writes. A comma-decimal locale would put commas between the
    ///halves of a coordinate pair, where the pair separator is already a comma - and the polygon would be
    ///read as having twice as many points, all in the wrong places.
    ///</summary>
    [Fact]
    public void Coordinates_are_written_the_same_in_every_locale()
    {
        var was = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            string markup = SvgWriter.Markers(new List<DrcViolation>
            {
                Region("met1.1", At(-1500, 2000), At(-1400, 2000), At(-1400, 2100))
            });

            Assert.Contains("points=\"-1500,2000 -1400,2000 -1400,2100\"", markup);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = was;
        }
    }

    #endregion **************************************************************************
}
