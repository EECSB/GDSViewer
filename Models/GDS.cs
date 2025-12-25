using Microsoft.VisualBasic;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using System.Xml.Linq;
using System.Linq;
using static GDSViewer.Models.GDS;
using static GDSViewer.Models.GDS.Record;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
                        case short[] ia:
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
            public StreamFormatModel(Record header, Record bgnlib, Record libname, Record units, Record endlib, List<StructureModel> structures = null /*todo: add other optional params*/)
            {
                HEADER = header;
                BGNLIB = bgnlib;
                LIBNAME = libname;
                UNITS = units;
                ENDLIB = endlib;

                Structures = structures;
            }

            public StreamFormatModel(ref int i, List<Record> records)
            {
                HEADER = records[i];
                i++;

                BGNLIB = records[i];
                i++;
                
                LIBNAME = records[i];
                i++;

                if (records[i].Type == RecordType.REFLIBS)
                {
                    REFLIBS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.FONTS)
                {
                    FONTS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.ATTRTABLE)
                {
                    ATTRTABLE = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.GENERATIONS)
                {
                    GENERATIONS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.FORMAT)
                {
                    FormatType = new FormatTypeModel(ref i, records);
                }

                UNITS = records[i];
                i++;

                while (records[i].Type == RecordType.BGNSTR)
                {
                    Structures.Add(new StructureModel(ref i, records));
                }

                ENDLIB = records[i];
                i++;
            }

            public Record HEADER { get; set; }
            public Record BGNLIB { get; set; }
            public Record LIBNAME { get; set; }
            public Record REFLIBS { get; set; }
            public Record FONTS { get; set; }
            public Record ATTRTABLE { get; set; }
            public Record GENERATIONS { get; set; }
            public FormatTypeModel FormatType { get; set; }
            public Record UNITS { get; set; }
            public List<StructureModel> Structures { get; set; } = new List<StructureModel>();
            public Record ENDLIB { get; set; }
        }

        public class FormatTypeModel
        {
            public FormatTypeModel(ref int i, List<Record> records)
            {
                FORMAT = records[i];
                i++;

                if (records[i].Type == RecordType.MASK)
                {
                    while (records[i].Type != RecordType.ENDMASKS)
                    {
                        MASKS.Add(records[i]);
                        i++;
                    }
                }

                if (records[i].Type == RecordType.ENDMASKS)
                {
                    ENDMASKS = records[i];
                    i++;
                }
            }

            public Record FORMAT { get; set; }
            public List<Record> MASKS { get; set; } = new List<Record>();
            public Record ENDMASKS { get; set; }
        }

        public class StructureModel
        {
            public StructureModel(Record bgnstr, Record strname, Record strclass = null /*todo: add other optional params*/)
            {
                BGNSTR = bgnstr;
                STRNAME = strname;
                STRCLASS = strclass;

                //ENDSTR = new Record(0, 0, new byte[0]);
            }

            public StructureModel(ref int i, List<Record> records)
            {
                BGNSTR = records[i];
                i++;

                STRNAME = records[i];
                i++;

                if (records[i].Type == RecordType.STRCLASS)
                {
                    STRCLASS = records[i];
                    i++;
                }


                while (ElementModel.IsElementRecord(records[i].Type)) 
                {
                    Elements.Add(new ElementModel(ref i, records));

                    /*if (records.Count <= i) //temp. for debug and testing
                    {
                        i = records.Count-2;
                        break;
                    }*/
                }

                ENDSTR = records[i];
                i++;
            }

            public Record BGNSTR { get; set; }
            public Record STRNAME { get; set; }
            public Record STRCLASS { get; set; }
            public List<ElementModel> Elements { get; set; } = new List<ElementModel>();
            public Record ENDSTR { get; set; }
        }

        public class ElementModel
        {
            public ElementModel()
            {
                
            }

            public ElementModel(ref int i, List<Record> records)
            {
                switch (records[i].Type)
                {
                    case RecordType.BOUNDARY:
                        Element = new BoundaryModel(ref i, records);
                        break;
                    case RecordType.PATH:
                        Element = new PathModel(ref i, records);
                        break;
                    case RecordType.SREF:
                        Element = new SrefModel(ref i, records);
                        break;
                    case RecordType.AREF:
                        Element = new ArefModel(ref i, records);
                        break;
                    case RecordType.TEXT:
                        Element = new TextModel(ref i, records);
                        break;
                    case RecordType.NODE:
                        Element = new NodeModel(ref i, records);
                        break;                   
                    case RecordType.BOX:
                        Element = new BoxModel(ref i, records);
                        break;
                    
                    default: throw new Exception("Error"); //TODO
                }

                while (records[i].Type == RecordType.PROPATTR)
                {
                    Properties.Add(new PropertyModel(ref i, records));
                }

                ENDEL = records[i];
                i++;
            }


            public static bool IsElementRecord(RecordType type)
            {
                switch (type)
                {
                    case RecordType.BOUNDARY:
                    case RecordType.PATH:
                    case RecordType.SREF:
                    case RecordType.AREF:
                    case RecordType.TEXT:
                    case RecordType.NODE:
                    case RecordType.BOX:
                        return true;

                    default:
                        return false;
                }
            }


            public List<PropertyModel> Properties { get; set; } = new List<PropertyModel>();
            public ElementType Element { get; set; } = null!;
            public Record ENDEL { get; set; }
        }

        public interface IHasLayer
        {
            public Record LAYER { get; set; }
        }

        public class ElementType 
        {
            public ElementType()
            {
                    
            }

            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public virtual Record XY { get; set; }
        }

        public class BoundaryModel : ElementType, IHasLayer
        {
            public BoundaryModel(Record boundary, Record layer, Record dataType, Record xy, Record elFlags = null, Record plex = null)
            {
                BOUNDARY = boundary;
                ELFLAGS = elFlags;
                PLEX = plex;
                LAYER = layer;
                DATATYPE = dataType;
                XY = xy;
            }

            public BoundaryModel(ref int i, List<Record> records)
            {
                BOUNDARY = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                LAYER = records[i];
                i++;

                DATATYPE = records[i];
                i++;

                XY = records[i];
                i++;
            }

            public Record BOUNDARY { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public Record DATATYPE { get; set; }
        }

        public class PathModel : ElementType, IHasLayer
        {
            public PathModel(ref int i, List<Record> records)
            {
                PATH = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                LAYER = records[i];
                i++;

                DATATYPE = records[i];
                i++;

                if (records[i].Type == RecordType.PATHTYPE)
                {
                    PATHTYPE = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.WIDTH)
                {
                    WIDTH = records[i];
                    i++;
                }

                XY = records[i];
                i++;
            }

            public Record PATH { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public Record DATATYPE { get; set; }
            public Record PATHTYPE { get; set; }
            public Record WIDTH { get; set; }
        }

        public class SrefModel : ElementType
        {
            public SrefModel(ref int i, List<Record> records)
            {
                SREF = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                SNAME = records[i];
                i++;

                if (records[i].Type == RecordType.STRANS)
                {
                    Strans = new StransModel(ref i, records);
                }

                XY = records[i];
                i++;
            }

            public Record SREF { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public StransModel Strans { get; set; }
            public Record SNAME { get; set; }
        }

        public class ArefModel : ElementType
        {
            public ArefModel(ref int i, List<Record> records)
            {
                AREF = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                SNAME = records[i];
                i++;

                if (records[i].Type == RecordType.STRANS)
                {
                    Strans = new StransModel(ref i, records);
                }

                COLROW = records[i];
                i++;

                XY = records[i];
                i++;
            }

            public Record AREF { get; set; }

            public Record SNAME { get; set; }
            public StransModel Strans { get; set; }
            public Record COLROW { get; set; }
        }

        public class TextModel : ElementType, IHasLayer
        {
            public TextModel(ref int i, List<Record> records)
            {
                TEXT = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                LAYER = records[i];
                i++;

                TextBody = new TextBodyModel(ref i, records);
            }

            public Record TEXT { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public TextBodyModel TextBody { get; set; }

            private Record xy;
            public override Record XY
            {
                get { return TextBody.XY; }
                set { TextBody.XY = value; }
            }
        }

        public class NodeModel : ElementType, IHasLayer
        {
            public NodeModel(ref int i, List<Record> records)
            {
                NODE = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                LAYER = records[i];
                i++;

                NODETYPE = records[i];
                i++;

                XY = records[i];
                i++;
            }

            public Record NODE { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public Record NODETYPE { get; set; }
        }

        public class BoxModel : ElementType, IHasLayer
        {
            public BoxModel(ref int i, List<Record> records)
            {
                BOX = records[i];
                i++;

                if (records[i].Type == RecordType.ELFLAGS)
                {
                    ELFLAGS = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PLEX)
                {
                    PLEX = records[i];
                    i++;
                }

                LAYER = records[i];
                i++;

                BOXTYPE = records[i];
                i++;

                XY = records[i];
                i++;
            }

            public Record BOX { get; set; }
            public Record ELFLAGS { get; set; }
            public Record PLEX { get; set; }
            public Record LAYER { get; set; }
            public Record BOXTYPE { get; set; }
        }

        public class TextBodyModel
        {
            public TextBodyModel(ref int i, List<Record> records)
            {
                TEXTYPE = records[i];
                i++;

                if (records[i].Type == RecordType.PRESENTATION)
                {
                    PRESENTATION = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.PATHTYPE)
                {
                    PATHTYPE = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.WIDTH)
                {
                    WIDTH = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.STRANS)
                {
                    Strans = new StransModel(ref i, records);
                }

                XY = records[i];
                i++;

                STRING = records[i];
                i++;
            }

            public Record TEXTYPE { get; set; }
            public Record PRESENTATION { get; set; }
            public Record PATHTYPE { get; set; }
            public Record WIDTH { get; set; }
            public StransModel Strans { get; set; }
            public Record XY { get; set; }
            public Record STRING { get; set; }
        }

        public class StransModel
        {
            public StransModel(ref int i, List<Record> records)
            {
                STRANS = records[i];
                i++;

                if (records[i].Type == RecordType.MAG)
                {
                    MAG = records[i];
                    i++;
                }

                if (records[i].Type == RecordType.ANGLE)
                {
                    ANGLE = records[i];
                    i++;
                }
            }

            public Record STRANS { get; set; }
            public Record MAG { get; set; }
            public Record ANGLE { get; set; }
        }

        public class PropertyModel
        {
            public PropertyModel(ref int i, List<Record> records)
            {
                PROPATTR = records[i];
                i++;

                PROPVALUE = records[i];
                i++;
            }

            public Record PROPATTR { get; set; }

            public Record PROPVALUE { get; set; }
        }



        public class Record
        {
            #region Constructor *****************************************************************

            public Record(Span<byte> allRecordBytes)
            {
                Span<byte> recordLengthSpan = allRecordBytes[0..2];
                Span<byte> recordTypeSpan = allRecordBytes[2..3];
                Span<byte> recordDataTypeSpan = allRecordBytes[3..4];

                short recordLength = BinaryPrimitives.ReadInt16BigEndian(recordLengthSpan);
                short recordType = BinaryPrimitives.ReadInt16BigEndian(recordTypeSpan);
                short recordDataType = BinaryPrimitives.ReadInt16BigEndian(recordDataTypeSpan);

                Span<byte> recordDataSpan = allRecordBytes[4..(recordLength - 4)];

                Type = (RecordType)recordType;
                DataType = (RecordDataType)recordDataType;
                Data = convertData(recordDataSpan.ToArray());
            }

            public Record(short length, short type, byte[] data)
            {
                Type = (RecordType)type;

                setData(data);
            }

            #endregion **************************************************************************



            #region Properties ******************************************************************

            public dynamic? Data { get; set; }

            public dynamic? DotNetDataType { get; set; } //TODO: see if this can be done better.

            public RecordType Type { get; set; }

            public RecordDataType DataType { get; set; }

            public Dictionary<RecordDataType, Record> ChildRecords { get; set; } = new Dictionary<RecordDataType, Record>(); //TODO: ???

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
                        if (data.Length > 2)
                        {
                            int numberOfElements = data.Length / 2; //Each int is 2 bytes so divide by 2 to get the number of elements;
                            short[] shorts = new short[numberOfElements]; //Create a temp INT2 array.
                            for (int i = 0; i < numberOfElements; i++)
                            {
                                int index = i * 2; //Calculate index of current INT2 element.
                                byte[] currentVal = data[index..(index + 2)]; //Get the 2 bytes for current INT2 element.
                                Array.Reverse(currentVal);
                                shorts[i] = BitConverter.ToInt16(currentVal, 0); //Convert the 2 bytes to INT2 and save it into the INT2 array.
                            }

                            convertedData = shorts;
                        }
                        else 
                        {
                            Array.Reverse(data);
                            short int2 = BitConverter.ToInt16(data, 0);
                            convertedData = int2;
                        }
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
                            double double8 = ToDoubleHelper(data);
                            convertedData = double8;
                        break;
                    case RecordDataType.ASCII:
                            Span<byte> dataSpan = data;

                            int length = dataSpan.Length;
                            if (dataSpan[length-1] == 0) //If is null terminated string.
                                    length--; //Don't include null termination into conversion.

                            string asciiString = Encoding.ASCII.GetString(dataSpan[0..length]);
                            convertedData = asciiString;
                        break;
                    default:
                        break;
                }

                return convertedData;
            }

            private static double ToDoubleHelper(byte[] data)
            {
                //Section 3.0 Data Type Description of GDSII defintion doc for more info on the REAL8 data type definition.

                //This will right shift the first bit(the sign bit) in the first byte to the beginning and fill the other places with zeros.
                //Then we can simply check if the the byte has a value of 1. If so it means we have a sign and the number is negative. 
                bool sign = (data[0] >> 7) == 1;

                //To get the exponent from the byte we first have to get rid(set it to 0) of the sign bit.
                //We can do this by AND-ing the first byte with 128 or 01111111 in binary. The first bit will be 0 and all the others will be copied over.
                int exponent = data[0] & 0b01111111; 
                exponent = exponent - 64; //Subtract from 64 to get the exaponent as specified in section 3 - 4 of GDSII defintion doc.

                //Make a byte array for the mantissa bytes.
                //Set all bits in the first byte to 0 bacause data[0] contains the sign and exponent and we only want the 7-byte mantissa.
                byte[] mantissaBytes = new byte[] 
                {
                    //This array must contain 8 bytes as BitConverter will not convert only 7 bytes to ulong.
                    //      0, data[1], data[2], data[3],
                    //data[4], data[5], data[6], data[7]

                    //Reverse due to different endianness.
                    data[7], data[6], data[5], data[4],
                    data[3], data[2], data[1], 0
                };

                //Convert byte[] to ulong.
                ulong mantissa = BitConverter.ToUInt64(mantissaBytes, 0);


                //If mantissa is 0 return 0 now and save some compute time.
                if (mantissa == 0) 
                    return 0;


                //Get the max value for a 7 byte ulong. We need to ignore the first byte as it for the sign and exponent. 
                //Set first byte to 0 and all the others to the max value. We'll do it in HEX as it more compact. 
                ulong maxValueFor7ByteUlong = 0x00FFFFFFFFFFFFFFUL;

                //Turn the mantissa into a fraction/decimal between 0 and 1 by dividing it by the max possible value.
                double fraction = (double)mantissa / maxValueFor7ByteUlong;

                //Calcaulte the actual value from the exponent and mantissa then save it into a double.
                double double8 = fraction * Math.Pow(16, exponent);
                
                //Make the number negative if the sign bit is set to 1.
                if (sign)
                    double8 = double8 * -1;


                return double8;
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

                        //Convert Data to DateTime type. (year, month, day, hour, minute, second)
                        DateTime BGNLIB_lastModificationTime = new DateTime(Data[0], Data[1], Data[2], Data[3], Data[4], Data[5]);
                        DateTime BGNLIB_lastAccessTime = new DateTime(Data[6], Data[7], Data[8], Data[9], Data[10], Data[11]);

                        //Save it as DateTime,DateTime tuple. This will make it easier to work with than an INT2 array.
                        DotNetDataType = (BGNLIB_lastModificationTime, BGNLIB_lastAccessTime);
                        break;
                    case RecordType.LIBNAME:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.UNITS:
                        DataType = RecordDataType.REAL8;

                        double[] units = new double[2];
                        units[0] = convertData(data[0..8]);
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

                        //Convert Data to DateTime type. (year, month, day, hour, minute, second)
                        DateTime BGNSTR_lastModificationTime = new DateTime(Data[0], Data[1], Data[2], Data[3], Data[4], Data[5]);
                        DateTime BGNSTR_lastAccessTime = new DateTime(Data[6], Data[7], Data[8], Data[9], Data[10], Data[11]);

                        //Save it as DateTime,DateTime tuple. This will make it easier to work with than an INT2 array.
                        DotNetDataType = (BGNSTR_lastModificationTime, BGNSTR_lastAccessTime);
                        break;
                    case RecordType.STRNAME: //TODO: add character validation logic
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
                        DataType = RecordDataType.NODATA;
                        break;
                    case RecordType.SREF:
                        DataType = RecordDataType.NODATA;
                        break;
                    case RecordType.AREF:
                        DataType = RecordDataType.NODATA;
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
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.XY:
                        DataType = RecordDataType.INT4; //TODO: revisit this

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
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.COLROW:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.TEXTNODE:
                        break;
                    case RecordType.NODE:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.TEXTTYPE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.PRESENTATION: //TODO: data model
                        DataType = RecordDataType.BITARRAY;
                        Data = convertData(data);
                        break;
                    case RecordType.STRING:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.STRANS: //TODO: data model
                        DataType = RecordDataType.BITARRAY;
                        Data = convertData(data);
                        break;
                    case RecordType.MAG:
                        DataType = RecordDataType.REAL8;
                        Data = convertData(data);
                        break;
                    case RecordType.ANGLE:
                        DataType = RecordDataType.REAL8;
                        Data = convertData(data);
                        break;
                    case RecordType.REFLIBS:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.FONTS:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.PATHTYPE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.GENERATIONS:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.ATTRTABLE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;

                    //case RecordType.STYPTABLE: //Unreleased feature.
                    //    DataType = RecordDataType.INT2;
                    //    Data = convertData(data);
                    //    break;
                    //case RecordType.STRTYPE: //Unreleased feature.
                    //    DataType = RecordDataType.INT2;
                    //    Data = convertData(data);
                    //    break;

                    case RecordType.ELFLAGS: //TODO: data model
                        DataType = RecordDataType.BITARRAY;
                        Data = convertData(data);
                        break;

                    //case RecordType.ELKEY: //Unreleased feature.
                    //    DataType = RecordDataType.INT2;
                    //    Data = convertData(data);
                    //    break;
                    //case RecordType.LINKTYPE: //Unreleased feature.
                    //    DataType = RecordDataType.INT2;
                    //    Data = convertData(data);
                    //    break;
                    //case RecordType.LINKKEYS: //Unreleased feature.
                    //    DataType = RecordDataType.INT2;
                    //    Data = convertData(data);
                    //    break;

                    case RecordType.NODETYPE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.PROPATTR:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.PROPVALUE:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.BOX:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
                        break;
                    case RecordType.BOXTYPE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.PLEX:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.BGNEXTN:
                        break;
                    case RecordType.ENDEXTN:
                        break;
                    case RecordType.TAPENUM:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.TAPECODE:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.STRCLASS: //Not used
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.RESERVED: //This record type was used for NUMTYPES but was not required
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.FORMAT:
                        DataType = RecordDataType.INT2;
                        Data = convertData(data);
                        break;
                    case RecordType.MASK:
                        DataType = RecordDataType.ASCII;
                        Data = convertData(data);
                        break;
                    case RecordType.ENDMASKS:
                        DataType = RecordDataType.NODATA;
                        Data = convertData(data);
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
                REAL4 = 4, //Not used
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
                // STYPTABLE = 0x2406, //Unreleased feature
                // STRTYPE = 0x2502, //Unreleased feature
                ELFLAGS = 0x2601,
                // ELKEY = 0x2703, //Unreleased feature
                // LINKTYPE = 0x28, //Unreleased feature
                // LINKKEYS = 0x29, //Unreleased feature
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
                RESERVED = 0x3503, 
                FORMAT = 0x3602,
                MASK = 0x3706,
                ENDMASKS = 0x3800,
                LIBDIRSIZE = 0x3902,
                SRFNAME = 0x3A06,
                LIBSECUR = 0x3B02,

                //Types used only with Custom Plus
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
