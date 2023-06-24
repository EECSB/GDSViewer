using System.Drawing;
using static GDSViewer.Models.GDS;
using static GDSViewer.Models.GDS.Record;

namespace GDSViewer.Models
{
    public class Element
    {
        public Element() { }

        public Element(Layer layer, List<Point> points)
        {
            Layer = layer;
            Points = points;
        }


        public Layer Layer { get; set; }
        public List<Point> Points { get; set; } = new List<Point>();


        public struct Point
        {
            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; set; }
            public int Y { get; set; }
        }
    }

    public class AdditionalGDSInformation
    {
        public AdditionalGDSInformation(GDS gds)
        {
            GetLayers(gds.StreamFormat.Structure.Elements);
        }

        public Dictionary<short, Layer> Layers { get; set; } = new Dictionary<short, Layer>();

        public void GetLayers(List<ElementModel> elements)
        {
            foreach (var element in elements)
            {
                var boundry = element.Boundry.First();

                if (boundry.Layer.Type != RecordType.LAYER)
                    continue;

                short layerNumber = boundry.Layer.Data;

                string layerColor = "";
                var newLayer = new Layer(boundry.Layer.Data, layerColor);

                if (!Layers.ContainsKey(layerNumber))
                    Layers.Add(layerNumber, newLayer);
            }

            var orderedLayers = Layers.OrderBy(x => x.Value.Number);

            int absoluteOffset = 0;
            int setLayerOffset = 0; //Keep fixed at 0 for now.
            foreach (var layer in orderedLayers) 
            {
                layer.Value.Offset = absoluteOffset;
                absoluteOffset += layer.Value.Depth + setLayerOffset;
            }

            int colorStep = 255 / Layers.Count;
            int i = 0;
            foreach (var layer in orderedLayers)
            {
                layer.Value.Color = layerColors[i];
                i += colorStep;
            }
        }

        #region Data ************************************************************************

        //Seed values used to create the 255 color palette. One color for each layer. 
        //https://vis4.net/labs/multihue/#colors=#b30000%20#7c1158%20#4421af%20#1a53ff%20#0d88e6%20#00b7c7%20#5ad45a%20#8be04e%20#ebdc78|steps=255|bez=0|coL=0
        //["#b30000", "#7c1158", "#4421af", "#1a53ff", "#0d88e6", "#00b7c7", "#5ad45a", "#8be04e", "#ebdc78"]
        private static string[] layerColors = new string[]
        {
            "#b30000", "#b20004", "#b00109", "#af010d", "#ad0211", "#ac0214", "#aa0318", "#a8031b", "#a7041e", "#a50420",
            "#a40523", "#a20526", "#a10628", "#9f062b", "#9d072d", "#9c0730", "#9a0832", "#980835", "#970937", "#950a3a",
            "#930a3c", "#910b3e", "#8f0b41", "#8e0c43", "#8c0d46", "#8a0d48", "#880e4a", "#860e4d", "#840f4f", "#820f51",
            "#801054", "#7e1156", "#7c1159", "#7b115b", "#7a125e", "#7a1261", "#791263", "#781366", "#771368", "#76146b",
            "#75146e", "#741470", "#731573", "#721576", "#701679", "#6f167b", "#6e177e", "#6c1781", "#6b1883", "#691886",
            "#671989", "#66198c", "#641a8f", "#621b91", "#601b94", "#5e1c97", "#5b1c9a", "#591d9d", "#561e9f", "#531ea2",
            "#501fa5", "#4d1fa8", "#4a20ab", "#4621ae", "#4422b0", "#4324b3", "#4325b5", "#4327b8", "#4229ba", "#422abc",
            "#412cbf", "#412ec1", "#402fc4", "#3f31c6", "#3f33c9", "#3e34cb", "#3d36ce", "#3c37d0", "#3b39d3", "#3a3ad5",
            "#393cd8", "#383dda", "#373fdd", "#3641df", "#3442e2", "#3344e5", "#3145e7", "#3047ea", "#2e48ec", "#2c4aef",
            "#2a4bf1", "#274df4", "#254ef7", "#2250f9", "#1f51fc", "#1b53fe", "#1b54fe", "#1c56fe", "#1d58fd", "#1e5afc",
            "#1f5cfb", "#1f5dfb", "#205ffa", "#2061f9", "#2163f8", "#2164f7", "#2166f7", "#2168f6", "#216af5", "#216bf4",
            "#216df4", "#216ff3", "#2170f2", "#2072f1", "#2073f0", "#1f75f0", "#1f77ef", "#1e78ee", "#1d7aed", "#1c7bec",
            "#1b7dec", "#1a7feb", "#1880ea", "#1782e9", "#1583e8", "#1385e8", "#1086e7", "#0d88e6", "#1089e5", "#128be4",
            "#148ce3", "#158ee2", "#168fe1", "#1891e0", "#1992df", "#1994de", "#1a95dd", "#1b97dc", "#1b98dc", "#1b9adb",
            "#1c9bda", "#1c9dd9", "#1c9ed8", "#1ca0d7", "#1ba1d6", "#1ba3d5", "#1aa4d4", "#1aa6d3", "#19a7d2", "#18a9d1",
            "#17aad0", "#16accf", "#14adce", "#13aecd", "#11b0cc", "#0eb1cb", "#0bb3ca", "#07b4c9", "#03b6c8", "#05b7c6",
            "#16b8c3", "#1fb9c0", "#26babd", "#2cbbb9", "#31bcb6", "#35bcb3", "#39bdb0", "#3cbeac", "#3fbfa9", "#42c0a6",
            "#45c1a3", "#47c29f", "#49c39c", "#4bc499", "#4dc595", "#4ec592", "#50c68f", "#51c78b", "#52c888", "#53c985",
            "#54ca81", "#55cb7e", "#56cc7a", "#57cd76", "#58ce73", "#58cf6f", "#59d06c", "#59d168", "#59d264", "#5ad360",
            "#5ad45c", "#5bd45a", "#5dd559", "#5fd559", "#60d559", "#62d658", "#64d658", "#65d658", "#67d757", "#69d757",
            "#6ad857", "#6cd856", "#6ed856", "#6fd956", "#71d955", "#72da55", "#74da54", "#75da54", "#77db54", "#78db53",
            "#7adb53", "#7bdc53", "#7ddc52", "#7edd52", "#80dd51", "#81dd51", "#82de51", "#84de50", "#85de50", "#87df4f",
            "#88df4f", "#89e04f", "#8be04e", "#8ee04f", "#91e050", "#95e052", "#98e053", "#9be055", "#9fe056", "#a2e057",
            "#a5e059", "#a8e05a", "#ace05c", "#afdf5d", "#b2df5e", "#b5df60", "#b8df61", "#bbdf62", "#bedf64", "#c1df65",
            "#c4df66", "#c7df67", "#cade69", "#ccde6a", "#cfde6b", "#d2de6d", "#d5de6e", "#d8de6f", "#dbdd70", "#dddd72",
            "#e0dd73", "#e3dd74", "#e6dc75", "#e8dc77", "#ebdc78"
        };

        #endregion **************************************************************************

        ///////////////////////////////////////// To remove /////////////////////////////////////////////////
        [Obsolete]
        public enum LayerColors
        {
            Red,
            Green,
            Blue,
            Yellow,
            Orange,
            Tomato,
            DodgerBlue,
            MediumSeaGreen,
            Gray,
            SlateBlue,
            Violet,
            LightGray
        }
    }

    public class Layer
    {
        #region Constructors ****************************************************************
       
        public Layer(short layerNumber, string layerColor, int layerOffset = 10, int layerDepth = 50)
        {
            Offset = layerOffset;
            Number = layerNumber;
            Color = layerColor;
            Depth = layerDepth;
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        public short Number { get; set; }
        public int Offset { get; set; }
        public int Depth { get; set; }
        public string Color { get; set; }

        #endregion **************************************************************************



        #region Methods *********************************************************************





        #endregion **************************************************************************

    }
}
