using System.Text.Json.Serialization;

namespace GDSViewer.Models
{
    ///<summary>
    ///One of the bundled designs, and whose it is.
    ///
    ///**The designs are somebody else's work**, where the cells beside them are one library under one license.
    ///So each carries its own credit - who made it, under what terms, and where it came from - and the app
    ///shows that with the design rather than leaving it to a file nobody opens. Read from
    ///designs.json, which is written by hand for exactly that reason: a glob can list files but cannot write
    ///a credit, and a test holds the list and the folder to each other.
    ///
    ///Plain lower-case names, since the file is written by hand and read by people.
    ///</summary>
    public class BundledDesign
    {
        ///<summary>The file in the designs' folder, extension included - what a link's file= names.</summary>
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        ///<summary>What the design is called, for people rather than for a URL.</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        ///<summary>Who made it.</summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = "";

        ///<summary>One sentence on what it is.</summary>
        [JsonPropertyName("about")]
        public string About { get; set; } = "";

        ///<summary>The design's own home - its repository.</summary>
        [JsonPropertyName("project")]
        public string Project { get; set; } = "";

        ///<summary>
        ///The exact file it was taken from, pinned to the commit it was taken at - so the link goes on
        ///pointing at what is bundled here however the project moves on.
        ///</summary>
        [JsonPropertyName("gds")]
        public string Gds { get; set; } = "";

        ///<summary>The license it is published under, as an SPDX identifier.</summary>
        [JsonPropertyName("license")]
        public string License { get; set; } = "";

        ///
        ///**Where the file is downloaded from when it is opened, for a design the app lists but does not ship** - the
        ///raw file at the commit the gds link names. GF180's and IHP's designs are listed this way (2026-11-02, asked
        ///for): a link somebody clicks to download the design then, rather than megabytes carried in the app for
        ///everybody. Empty for a design that ships in its folder.
        ///
        [JsonPropertyName("download")]
        public string Download { get; set; } = "";

        ///<summary>How big the download is, in bytes - said before anybody presses to fetch it. Zero for a design that ships.</summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        ///<summary>Whether this is fetched from its source when opened rather than shipped - see <see cref="Download"/>.</summary>
        [JsonIgnore]
        public bool Downloaded => Download.Length > 0;
    }
}
