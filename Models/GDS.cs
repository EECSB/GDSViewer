using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Emit;
using System.Text;
using static GDSViewer.Models.GDS;
using static GDSViewer.Models.GDS.Record;

namespace GDSViewer.Models
{
    public class GDS
    {
        #region Constructor *****************************************************************

        public GDS(byte[] gdsData)
        {
            Deserialize(gdsData);
        }

        private void parseRecords(byte[] gdsData) 
        {
            for (int i = 0; i < gdsData.Length; )
            {
                byte[] recordLength = new byte[2];
                
                recordLength[0] = gdsData[i];
                i++;
                recordLength[1] = gdsData[i];
                i++;

                Array.Reverse(recordLength);

                short recordLengthInt = BitConverter.ToInt16(recordLength, 0);




                byte[] recordType = new byte[2];

                recordType[0] = gdsData[i];
                i++;
                recordType[1] = gdsData[i];
                i++;

                Array.Reverse(recordType);

                short recordTypeInt = BitConverter.ToInt16(recordType, 0);




                int dataLength = recordLengthInt - 4;
                byte[] data = new byte[dataLength];

                for (int j = 0; j < dataLength; j++)
                {
                    data[j] = gdsData[i];
                    i++;
                }

                Record record = new Record(recordLengthInt, recordTypeInt, data);

                Records.Add(record); 
            }
        }

        private void constructGDS()
        {
            int i = 0;
            StreamFormat = new StreamFormatModel(ref i, Records);
        }

        #endregion **************************************************************************



        #region Other ***********************************************************************

        public byte[] Serialize() 
        {
            byte[] serializedGDS = new byte[0];

            //todo: serialize GDS to byte[]

            return serializedGDS;
        }

        public void Deserialize(byte[] gdsData)
        {
            Records = new List<Record>();

            parseRecords(gdsData);
            constructGDS();

            AdditionalInformation = new AdditionalGDSInformation(this);
        }

        public void Deserialize(string gdsAstext)
        {
            /*foreach (var recordView in recordsView)
                {
                if (!recordView.changed)
                continue;

                recordView.changed = false;

                record.Type = new GDS.Record.RecordType();
            }*/
        }

        public string AsText() 
        {
            string gdsAsText = "";

            foreach (var record in this.Records)
            {
                string data = "";
                if(record.Data is not null)
                {
                    switch (record.Data)
                    {
                        case double[] da:
                            foreach (var item in da)
                            {
                                data += item.ToString() + " ";
                            }
                            break;
                        case int[] ia:
                            foreach (var item in ia)
                            {
                                data += item.ToString() + " ";
                            }
                            break;
                        case string s:
                            data = s;
                            break;
                        default:
                            data = record.Data.ToString();
                            break;
                    }
                }

                //No need to use StringBuilder explicitly.
                //When the code gets lowered it should be optimized automatically.
                gdsAsText += $"{record.Type.ToString()}: {data} \n";
            }

            return gdsAsText;
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        //[NonSerialized]
        public AdditionalGDSInformation AdditionalInformation { get; set; }

        public List<Record> Records { get; set; }

        public StreamFormatModel StreamFormat { get; set; }
        public FormatTypeModel FormatType { get; set; }
        public StructureModel Structure { get; set; }
        public ElementModel Element { get; set; }
        public BoundaryModel Boundary { get; set; }
        public PathModel Path { get; set; }
        public SrefModel Sref { get; set; }
        public ArefModel Aref { get; set; }
        public TextModel Text { get; set; }
        public NodeModel Node { get; set; }
        public BoxModel Box { get; set; }
        public TextBodyModel Textbody { get; set; }
        public StransModel Strans { get; set; }
        public PropertyModel Property { get; set; }


        #endregion **************************************************************************



        #region Models **********************************************************************

        public class StreamFormatModel
        {
            public StreamFormatModel(Record header, Record bgnlib, Record libname, Record units, Record endlib, StructureModel structure = null /*todo: add other optional params*/)
            {
                HEADER = header;
                BGNLIB = bgnlib;
                LIBNAME = libname;
                UNITS = units;
                ENDLIB = endlib;

                Structure = structure;
            }

            public StreamFormatModel(ref int i, List<Record> records)
            {
                HEADER = records[i];
                i++;

                BGNLIB = records[i];
                i++;
                
                LIBNAME = records[i];
                i++;

                UNITS = records[i];
                i++;

                Structure = new StructureModel(ref i, records);

                ENDLIB = records[i];
            }

            public Record HEADER { get; set; }
            public Record BGNLIB { get; set; }
            public Record LIBNAME { get; set; }
            public Record REFLIB { get; set; }
            public Record FONTS { get; set; }
            public Record ATTRTABLE { get; set; }
            public Record GENERATIONS { get; set; }
            public FormatTypeModel FormatType { get; set; }
            public Record UNITS { get; set; }
            public StructureModel Structure { get; set; }
            public Record ENDLIB { get; set; }
        }

        public class FormatTypeModel
        {

        }

        public class StructureModel
        {
            public StructureModel(Record bgnstr, Record strname /*todo: add other optional params*/)
            {
                BGNSTR = bgnstr;
                STRNAME = strname;
            }

            public StructureModel(ref int i, List<Record> records)
            {
                BGNSTR = records[i];
                i++;

                STRNAME = records[i];
                i++;

                Elements = new List<ElementModel>();
                while (records[i].Type != RecordType.ENDSTR) 
                {
                    Elements.Add(new ElementModel(ref i, records));

                    if (records.Count <= i) //temp. for debug and testing
                    {
                        i = records.Count-2;
                        break;
                    }
                }

                ENDSTR = records[i];
                i++;
            }

            public Record BGNSTR { get; set; }
            public Record STRNAME { get; set; }
            public Record STRCLASS { get; set; }
            public List<ElementModel> Elements { get; set; }
            public Record ENDSTR { get; set; }
        }

        public class ElementModel
        {
            public ElementModel()
            {
                
            }

            public ElementModel(ref int i, List<Record> records)
            {
                Boundry = new List<BoundaryModel>();

                Boundry.Add(new BoundaryModel(ref i, records));

                ENDEL = records[i];
                i++;
            }

            //todo: add other types

            public List<BoundaryModel> Boundry { get; set; }
            public Record ENDEL { get; set; }
        }

        public class BoundaryModel
        {
            public BoundaryModel(Record boundary, Record layer, Record dataType, Record xy, Record elFlags = null, Record plex = null)
            {
                Boundary = boundary;
                ElFlags = elFlags;
                Plex = plex;
                Layer = layer;
                DataType = dataType;
                XY = xy;
            }

            public BoundaryModel(ref int i, List<Record> records)
            {
                Boundary = records[i];
                i++;

                //ElFlags = records[i];
                //i++;

                //Plex = records[i];
                //i++;

                Layer = records[i];
                i++;

                DataType = records[i];
                i++;

                XY = records[i];
                i++;
            }

            public Record Boundary { get; set; }
            public Record ElFlags { get; set; }
            public Record Plex { get; set; }
            public Record Layer { get; set; }
            public Record DataType { get; set; }
            public Record XY { get; set; }
        }

        public class PathModel
        {

        }

        public class SrefModel
        {

        }

        public class ArefModel
        {

        }

        public class TextModel
        {

        }

        public class NodeModel
        {

        }

        public class BoxModel
        {

        }

        public class TextBodyModel
        {

        }

        public class StransModel
        {

        }

        public class PropertyModel
        {

        }



        public class Record
        {
            #region Constructor *****************************************************************

            public Record(short length, short type, byte[] data)
            {
                Type = (RecordType)type;

                setData(data);
            }

            #endregion **************************************************************************



            #region Properties ******************************************************************

            public dynamic? Data { get; set; }

            public RecordType Type { get; set; }

            public RecordDataType DataType { get; set; }

            public Dictionary<RecordDataType, Record> ChildRecords { get; set; } = new Dictionary<RecordDataType, Record>();

            #endregion **************************************************************************



            #region Private Methods *************************************************************

            private dynamic convertData(byte[] data) 
            {
                dynamic convertedData = null;

                switch (DataType)
                {
                    case RecordDataType.NODATA:
                        convertedData = null;
                        break;
                    case RecordDataType.BITARRAY:
                            convertedData = data;
                        break;
                    case RecordDataType.INT2:
                            Array.Reverse(data);
                            short int2 = BitConverter.ToInt16(data, 0);
                            convertedData = int2;
                        break;
                    case RecordDataType.INT4:
                            Array.Reverse(data);
                            int int4 = BitConverter.ToInt32(data, 0);
                            convertedData = int4;
                        break;
                    case RecordDataType.REAL4:
                            Array.Reverse(data);
                            float float4 = BitConverter.ToSingle(data, 0);
                            convertedData = float4;
                        break;
                    case RecordDataType.REAL8:
                            Array.Reverse(data);
                            double double8 = BitConverter.ToDouble(data, 0);
                            convertedData = double8;
                        break;
                    case RecordDataType.ASCII:
                            string asciiString = Encoding.ASCII.GetString(data);
                            convertedData = asciiString;
                        break;
                    default:
                        break;
                }

                return convertedData;
            }

            private void setData(byte[] data)
            {
                switch (Type)
                {
                    case RecordType.HEADER:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.BGNLIB:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.LIBNAME:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.UNITS:
                        DataType = RecordDataType.REAL8;
                        double[] units = new double[2];
                        units[0] = convertData(data[0..8]); //todo: Seems like the wrong value gets returned
                        units[1] = convertData(data[8..16]);
                        Data = units;
                        break;
                    case RecordType.ENDLIB:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.BGNSTR:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.STRNAME:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.ENDSTR:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.BOUNDARY:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.PATH:
                        DataType = RecordDataType.NODATA; //
                        break;
                    case RecordType.SREF:
                        DataType = RecordDataType.NODATA; //
                        break;
                    case RecordType.AREF:
                        DataType = RecordDataType.NODATA; //
                        break;
                    case RecordType.TEXT:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.LAYER:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.DATATYPE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.WIDTH:
                        DataType = RecordDataType.INT4; //
                        break;
                    case RecordType.XY:
                        DataType = RecordDataType.INT4;

                        int[] points = new int[data.Length / 4];

                        byte[] dataSegment = new byte[4];
                        int j = 0;
                        for (int i = 0; i < data.Length; i = i + 4) //todo: refactor, use points and multiply index *4
                        {
                            dataSegment[0] = data[i];
                            dataSegment[1] = data[i + 1];
                            dataSegment[2] = data[i + 2];
                            dataSegment[3] = data[i + 3];

                            points[j] = convertData(dataSegment);
                            j++;
                        }

                        Data = points;
                        break;
                    case RecordType.ENDEL:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.SNAME:
                        break;
                    case RecordType.COLROW:
                        break;
                    case RecordType.TEXTNODE:
                        break;
                    case RecordType.NODE:
                        break;
                    case RecordType.TEXTTYPE:
                        break;
                    case RecordType.PRESENTATION:
                        break;
                    case RecordType.STRING:
                        DataType = RecordDataType.ASCII;
                        break;
                    case RecordType.STRANS:
                        break;
                    case RecordType.MAG:
                        break;
                    case RecordType.ANGLE:
                        break;
                    case RecordType.REFLIBS:
                        break;
                    case RecordType.FONTS:
                        break;
                    case RecordType.PATHTYPE:
                        break;
                    case RecordType.GENERATIONS:
                        break;
                    case RecordType.ATTRTABLE:
                        break;
                    case RecordType.STYPTABLE:
                        break;
                    case RecordType.STRTYPE:
                        break;
                    case RecordType.ELFLAGS:
                        break;
                    case RecordType.ELKEY:
                        break;
                    case RecordType.NODETYPE:
                        break;
                    case RecordType.PROPATTR:
                        break;
                    case RecordType.PROPVALUE:
                        break;
                    case RecordType.BOX:
                        break;
                    case RecordType.BOXTYPE:
                        break;
                    case RecordType.PLEX:
                        break;
                    case RecordType.BGNEXTN:
                        break;
                    case RecordType.ENDEXTN:
                        break;
                    case RecordType.TAPENUM:
                        break;
                    case RecordType.TAPECODE:
                        break;
                    case RecordType.STRCLASS:
                        break;
                    case RecordType.FORMAT:
                        break;
                    case RecordType.MASK:
                        break;
                    case RecordType.ENDMASKS:
                        break;
                    case RecordType.LIBDIRSIZE:
                        break;
                    case RecordType.SRFNAME:
                        break;
                    case RecordType.LIBSECUR:
                        break;
                    case RecordType.BORDER:
                        break;
                    case RecordType.SOFTFENCE:
                        break;
                    case RecordType.HARDFENCE:
                        break;
                    case RecordType.SOFTWIRE:
                        break;
                    case RecordType.HARDWIRE:
                        break;
                    case RecordType.PATHPORT:
                        break;
                    case RecordType.NODEPORT:
                        break;
                    case RecordType.USERCONSTRAINT:
                        break;
                    case RecordType.SPACERERROR:
                        break;
                    case RecordType.CONTACT:
                        break;
                    default:
                        break;
                }
            }

            #endregion **************************************************************************



            #region Models **********************************************************************

            public enum RecordDataType
            {
                NODATA = 0,
                BITARRAY = 1,
                INT2 = 2,
                INT4 = 3,
                REAL4 = 4, // not used
                REAL8 = 5,
                ASCII = 6
            }

            public enum RecordType
            {
                HEADER = 0x0002,
                BGNLIB = 0x0102,
                LIBNAME = 0x0206,
                UNITS = 0x0305,
                ENDLIB = 0x0400,
                BGNSTR = 0x0502,
                STRNAME = 0x0606,
                ENDSTR = 0x0700,
                BOUNDARY = 0x0800,
                PATH = 0x0900,
                SREF = 0x0A00,
                AREF = 0x0B00,
                TEXT = 0x0C00,
                LAYER = 0x0D02,
                DATATYPE = 0x0E02,
                WIDTH = 0x0F03,
                XY = 0x1003,
                ENDEL = 0x1100,
                SNAME = 0x1206,
                COLROW = 0x1302,
                TEXTNODE = 0x1400,
                NODE = 0x1500,
                TEXTTYPE = 0x1602,
                PRESENTATION = 0x1701,
                // SPACING = 0x18??
                STRING = 0x1906,
                STRANS = 0x1A01,
                MAG = 0x1B05,
                ANGLE = 0x1C05,
                // UINTEGER = 0x1D??
                // USTRING = 0x1E??
                REFLIBS = 0x1F06,
                FONTS = 0x2006,
                PATHTYPE = 0x2102,
                GENERATIONS = 0x2202,
                ATTRTABLE = 0x2306,
                STYPTABLE = 0x2406,
                STRTYPE = 0x2502,
                ELFLAGS = 0x2601,
                ELKEY = 0x2703,
                // LINKTYPE: 0x28??
                // LINKKEYS: 0x29??
                NODETYPE = 0x2A02,
                PROPATTR = 0x2B02,
                PROPVALUE = 0x2C06,
                BOX = 0x2D00,
                BOXTYPE = 0x2E02,
                PLEX = 0x2F03,
                BGNEXTN = 0x3003,
                ENDEXTN = 0x3103,
                TAPENUM = 0x3202,
                TAPECODE = 0x3302,
                STRCLASS = 0x3401,
                // RESERVED: 0x3503
                FORMAT = 0x3602,
                MASK = 0x3706,
                ENDMASKS = 0x3800,
                LIBDIRSIZE = 0x3902,
                SRFNAME = 0x3A06,
                LIBSECUR = 0x3B02,
                // Types used only with Custom Plus
                BORDER = 0x3C00,
                SOFTFENCE = 0x3D00,
                HARDFENCE = 0x3E00,
                SOFTWIRE = 0x3F00,
                HARDWIRE = 0x4000,
                PATHPORT = 0x4100,
                NODEPORT = 0x4200,
                USERCONSTRAINT = 0x4300,
                SPACERERROR = 0x4400,
                CONTACT = 0x4500
            }

            #endregion **************************************************************************
        }

        #endregion **************************************************************************
    }
}
