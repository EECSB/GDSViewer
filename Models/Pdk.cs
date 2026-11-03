using System.Globalization;
using System.Text.Json.Serialization;

using GdsII;

namespace GDSViewer.Models
{
    ///
    ///A process design kit as the app keeps one: a name, its layers - a layermap, which names, colors, stacks and gives
    ///roles to the layers a file draws on - and the rules a layout in it is checked against. Kept in the PDK manager, and
    ///the one in use names and checks every file drawn in it. Asked for 2026-10-18.
    ///
    ///**sky130, gf180mcu and ihp-sg13g2 are built in**, each from the files beside the examples - see
    ///<see cref="BuiltIns"/> - and are edited like any other, with Reset to go back to them: a built-in's
    ///<see cref="Layers"/> and <see cref="Rules"/> are null until something is changed, and null means the shipped file's
    ///text. gf180mcu and ihp-sg13g2 since 2026-11-02, asked for.
    ///
    public sealed class Pdk
    {
        ///<summary>The first built-in's id, which a list always has and falls back on. See <see cref="PdkList.Default"/>.</summary>
        public const string Sky130 = "sky130";

        ///<summary>GlobalFoundries' 180 nm PDK, built in beside sky130 - its gf180mcuD stack.</summary>
        public const string Gf180mcu = "gf180mcu";

        ///<summary>IHP's 130 nm SiGe BiCMOS PDK, built in after gf180mcu.</summary>
        public const string IhpSg13g2 = "ihp-sg13g2";

        ///
        ///The PDKs that ship with the app, in the order a list shows them: each one's name, and the three files it is
        ///read from until it is changed - its layermap, its deck and its README, beside the examples drawn in it.
        ///
        public static readonly IReadOnlyList<ShippedPdk> BuiltIns = new[]
        {
            new ShippedPdk(Sky130, "sky130", "resources/GDS Files/sky130-roles.csv", "resources/GDS Files/sky130A.drc", "resources/GDS Files/sky130-README.md"),
            new ShippedPdk(Gf180mcu, "gf180mcu", "resources/GDS Files/gf180mcu-roles.csv", "resources/GDS Files/gf180mcuD.drc", "resources/GDS Files/gf180mcu-README.md"),
            new ShippedPdk(IhpSg13g2, "ihp-sg13g2", "resources/GDS Files/ihp-sg13g2-roles.csv", "resources/GDS Files/sg13g2.drc", "resources/GDS Files/ihp-sg13g2-README.md")
        };

        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        ///<summary>The layermap's text, in the shape LayerNames.Parse reads - or null, for a built-in's shipped one.</summary>
        [JsonPropertyName("layers")]
        public string? Layers { get; set; }

        ///<summary>The rule deck's text, in the shape DrcDeck.Parse reads - or null, for a built-in's shipped one.</summary>
        [JsonPropertyName("rules")]
        public string? Rules { get; set; }

        ///
        ///The layers a new layout starts with, as "layer/datatype" - or null for the usual ones: for a built-in, the
        ///layers its rules check with their pins and labels, and for any other PDK every layer it has. See
        ///<see cref="StartingLayers"/>.
        ///
        [JsonPropertyName("start")]
        public List<string>? Start { get; set; }

        ///
        ///What the PDK says about itself, shown when it is picked in the manager - the README its zip came with, or null
        ///for none. A built-in's is the README it ships with, sky130-README.md and the like, so null on it means that one.
        ///See <see cref="PdkReadme"/>.
        ///
        [JsonPropertyName("readme")]
        public PdkReadme? Readme { get; set; }

        ///<summary>What this PDK ships as, if it is one of the <see cref="BuiltIns"/> - or null for one made or brought in here.</summary>
        [JsonIgnore]
        public ShippedPdk? Shipped => BuiltIns.FirstOrDefault(shipped => shipped.Id == Id);

        [JsonIgnore]
        public bool BuiltIn => Shipped is not null;

        ///<summary>Whether this has been changed from what ships - for a built-in, what Reset puts back, its README among it.</summary>
        [JsonIgnore]
        public bool Changed => Layers is not null || Rules is not null || Start is not null || Readme is not null;

        ///
        ///The layers a new layout starts with here, given what this PDK's layers and rules say - which for a built-in
        ///are the shipped files' until changed.
        ///
        ///**A built-in's drawing layers and their pins and labels**: for sky130 the 21 its rules check, and met1.pin,
        ///met1.label and the rest of the 16 the layermap names on them, which is where a layout's ports and net names go.
        ///Asked for 2026-10-18. Not the markers and implants nobody starts a layout on, which are named all the same once
        ///a file drawing on them is opened. gf180mcu starts the same way, with its labels on datatype 10, and ihp-sg13g2
        ///with its net names as text on datatype 25 - see isPinOrLabel.
        ///
        public List<LayerKey> StartingLayers(LayerNames layers, DrcDeck rules)
        {
            if (Start is not null)
                return Start.Select(PairOf).OfType<LayerKey>().Distinct().OrderBy(key => key).ToList();

            if (!BuiltIn)
                return layers.Pairs.ToList();

            var marks = layers.Names.Where(row => isPinOrLabel(row.Value)).Select(row => row.Key);

            return rules.Layers.Values.Concat(marks).Distinct().OrderBy(key => key).ToList();
        }

        ///<summary>A pair as it is written, "68/20" - or null for anything else.</summary>
        public static LayerKey? PairOf(string written)
        {
            string[] parts = written.Split('/');

            if (parts.Length != 2)
                return null;

            if (!short.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short number))
                return null;

            if (!short.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short dataType))
                return null;

            return new LayerKey(number, dataType);
        }

        ///
        ///A pin, or where a layout's net names go - a label, or text, which is IHP's word for it: its LVS reads a net's name
        ///from Metal1.text, 8/25, and its cells write them there.
        ///
        private static bool isPinOrLabel(string name)
        {
            return name.EndsWith(".pin", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".label", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".text", StringComparison.OrdinalIgnoreCase);
        }
    }

    ///<summary>The kinds of file a PDK's README can be, each shown its own way in the manager.</summary>
    public enum PdkReadmeKind
    {
        Markdown,
        Html,
        Pdf,
        Text
    }

    ///
    ///A PDK's README: general information about it - the process, its layers and devices, where its documents are - as
    ///the file it came in, Markdown, HTML, a PDF or plain text. Asked for 2026-11-01, shown in the manager when the PDK
    ///itself is picked, and carried in and out in its zip beside the layermap and the deck.
    ///
    ///**Kept as the file was**: its name, which says its kind, and its text - or a PDF's bytes as base64, since the list
    ///is kept as JSON.
    ///
    public sealed class PdkReadme
    {
        ///<summary>The file's own name, README.md or sky130 guide.pdf, which a PDK's zip writes it back under.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        ///<summary>The text of a Markdown, HTML or text file - null for a PDF.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        ///<summary>A PDF's bytes as base64 - null for anything else.</summary>
        [JsonPropertyName("pdf")]
        public string? Pdf { get; set; }

        ///
        ///The pictures a Markdown README shows, by the path it names each by - beside it, or below its folder - as base64:
        ///brought in its zip with it, and written back out beside it (asked for 2026-11-01). Null for none.
        ///
        [JsonPropertyName("pictures")]
        public Dictionary<string, string>? Pictures { get; set; }

        [JsonIgnore]
        public PdkReadmeKind Kind => KindOf(Name) ?? PdkReadmeKind.Text;

        ///<summary>The kind of picture a file of this name is, as a browser is told it - or null for one a README cannot show.</summary>
        public static string? PictureTypeOf(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();

            if (extension == ".png")
                return "image/png";

            if (extension == ".jpg" || extension == ".jpeg")
                return "image/jpeg";

            if (extension == ".gif")
                return "image/gif";

            if (extension == ".webp")
                return "image/webp";

            if (extension == ".svg")
                return "image/svg+xml";

            return null;
        }

        ///
        ///Whether a picture's path stays beside the README: a picture's name, or a path below the README's folder - never
        ///an address, a path from a root or a drive, or one that climbs out with "..". The same rule a layermap's texture
        ///column keeps, for the same reason: in a zip, anything else is a file outside it.
        ///
        public static bool IsPicturePath(string path)
        {
            if (path.Length == 0 || PictureTypeOf(path) is null)
                return false;

            if (path.Contains(':') || path.StartsWith('/') || path.StartsWith('\\'))
                return false;

            return !path.Replace('\\', '/').Split('/').Any(part => part == "..");
        }

        ///<summary>A picture the README names, as an address the page can show it from - or null for one it does not carry.</summary>
        public string? PictureUrl(string path)
        {
            if (Pictures is null || !Pictures.TryGetValue(path, out string? picture) || PictureTypeOf(path) is not string type)
                return null;

            return $"data:{type};base64,{picture}";
        }

        ///<summary>The kind a file of this name is read as, or null for a file that is no README.</summary>
        public static PdkReadmeKind? KindOf(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();

            if (extension == ".md" || extension == ".markdown")
                return PdkReadmeKind.Markdown;

            if (extension == ".html" || extension == ".htm")
                return PdkReadmeKind.Html;

            if (extension == ".pdf")
                return PdkReadmeKind.Pdf;

            if (extension == ".txt")
                return PdkReadmeKind.Text;

            return null;
        }

        ///<summary>Whether a file is named as a README - README.md, readme.txt, ReadMe.pdf - which a package takes ahead of any other document in it.</summary>
        public static bool IsNamedReadme(string name)
        {
            return Path.GetFileNameWithoutExtension(name).Equals("readme", StringComparison.OrdinalIgnoreCase);
        }

        ///<summary>A README from a file's name and bytes: its text, or a PDF's bytes - null for a file that is no README.</summary>
        public static PdkReadme? From(string name, byte[] bytes)
        {
            var kind = KindOf(name);

            if (kind is null)
                return null;

            if (kind == PdkReadmeKind.Pdf)
                return new PdkReadme { Name = name, Pdf = Convert.ToBase64String(bytes) };

            return new PdkReadme { Name = name, Text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF') };
        }

        ///<summary>The file's bytes, as a zip carries it.</summary>
        public byte[] Bytes()
        {
            if (Pdf is not null)
                return Convert.FromBase64String(Pdf);

            return System.Text.Encoding.UTF8.GetBytes(Text ?? "");
        }

        public PdkReadme Copy()
        {
            Dictionary<string, string>? pictures = null;

            if (Pictures is not null)
                pictures = new Dictionary<string, string>(Pictures, StringComparer.Ordinal);

            return new PdkReadme { Name = Name, Text = Text, Pdf = Pdf, Pictures = pictures };
        }
    }

    ///
    ///A PDK as it ships with the app: its id and name, and where its layermap, deck and README are served from. See
    ///<see cref="Pdk.BuiltIns"/>.
    ///
    public sealed record ShippedPdk(string Id, string Name, string LayersPath, string RulesPath, string ReadmePath);

    ///<summary>The PDKs kept, and which one is in use. See <see cref="PdkStore"/>.</summary>
    public sealed class PdkList
    {
        [JsonPropertyName("selected")]
        public string Selected { get; set; } = Pdk.Sky130;

        [JsonPropertyName("pdks")]
        public List<Pdk> Pdks { get; set; } = new List<Pdk>();

        ///<summary>The built-ins alone, sky130 in use: what a browser that never kept a list starts with.</summary>
        public static PdkList Fresh()
        {
            var list = new PdkList();

            list.EnsureBuiltIns();

            return list;
        }

        ///<summary>sky130, which every list has and falls back on - see <see cref="EnsureBuiltIns"/>.</summary>
        [JsonIgnore]
        public Pdk Default => Pdks.First(pdk => pdk.Id == Pdk.Sky130);

        ///<summary>The PDK in use, or sky130 when the one named has gone.</summary>
        [JsonIgnore]
        public Pdk InUse => Find(Selected) ?? Default;

        ///
        ///Puts each built-in a list lacks at the top of it, in the order they ship, so there is always one to fall back
        ///on - and one that shipped later, gf180mcu or ihp-sg13g2, into a list kept before it did, under the built-ins
        ///before it and over the PDKs made here.
        ///
        ///**A built-in's name is its own.** A PDK somebody brought in under it before - a gf180mcu of their own - is kept,
        ///and renamed the way a new one would be, gf180mcu 2, so the two are not one name.
        ///
        public void EnsureBuiltIns()
        {
            int at = 0;

            foreach (var shipped in Pdk.BuiltIns)
            {
                int found = Pdks.FindIndex(pdk => pdk.Id == shipped.Id);

                if (found >= 0)
                {
                    at = found + 1;

                    continue;
                }

                var builtIn = new Pdk { Id = shipped.Id, Name = shipped.Name };

                Pdks.Insert(at, builtIn);

                at++;

                foreach (var namesake in Pdks.Where(pdk => !pdk.BuiltIn && string.Equals(pdk.Name, shipped.Name, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    string was = namesake.Name;

                    namesake.Name = "";
                    namesake.Name = FreeName(was);
                }
            }
        }

        public Pdk? Find(string id)
        {
            return Pdks.FirstOrDefault(pdk => pdk.Id == id);
        }

        ///<summary>A new PDK at the end of the list, under a name no other has.</summary>
        public Pdk Add(string name, string layers, string rules)
        {
            var pdk = new Pdk
            {
                Id = "pdk-" + Guid.NewGuid().ToString("N")[..8],
                Name = FreeName(name),
                Layers = layers,
                Rules = rules
            };

            Pdks.Add(pdk);

            return pdk;
        }

        ///<summary><paramref name="wanted"/>, or it with the first number after it that no PDK is called by.</summary>
        public string FreeName(string wanted)
        {
            string name = wanted.Trim();

            if (name.Length == 0)
                name = "New PDK";

            if (!Pdks.Any(pdk => string.Equals(pdk.Name, name, StringComparison.OrdinalIgnoreCase)))
                return name;

            for (int number = 2; ; number++)
            {
                string numbered = $"{name} {number}";

                if (!Pdks.Any(pdk => string.Equals(pdk.Name, numbered, StringComparison.OrdinalIgnoreCase)))
                    return numbered;
            }
        }
    }
}
