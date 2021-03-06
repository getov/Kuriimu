using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cetera.Compression;
using Kuriimu.Contract;
using Kuriimu.IO;
using Cetera.Hash;

namespace archive_xpck
{
    public sealed class XPCK
    {
        public List<XPCKFileInfo> Files = new List<XPCKFileInfo>();

        public XPCK(String filename)
        {
            Stream input;

            using (BinaryReaderX xpckBr = new BinaryReaderX(File.OpenRead(filename)))
            {
                if (xpckBr.ReadString(4) == "XPCK")
                {
                    xpckBr.BaseStream.Position = 0;
                    input = new MemoryStream(xpckBr.ReadBytes((int)xpckBr.BaseStream.Length));
                }
                else
                {
                    xpckBr.BaseStream.Position = 0;
                    byte[] decomp = CriWare.GetDecompressedBytes(xpckBr.BaseStream);
                    input = new MemoryStream(decomp);
                }
            }

            using (BinaryReaderX xpckBr = new BinaryReaderX(input))
            {
                //Header
                var header = xpckBr.ReadStruct<Header>();
                int fileCount = header.fileInfoSize / 0xc;

                //fileInfo
                var entries = new List<Entry>();
                entries.AddRange(xpckBr.ReadMultiple<Entry>(fileCount).OrderBy(e => e.fileOffset));

                //nameList
                var nameList = new List<String>();
                byte[] uncompressedNameList = CriWare.GetDecompressedBytes(new MemoryStream(xpckBr.ReadBytes(header.filenameTableSize)));
                using (BinaryReaderX nlBr = new BinaryReaderX(new MemoryStream(uncompressedNameList)))
                    for (int i = 0; i < fileCount; i++)
                        nameList.Add(nlBr.ReadCStringA());

                for (int i = 0; i < fileCount; i++)
                {
                    xpckBr.BaseStream.Position = header.dataOffset + entries[i].fileOffset;
                    Files.Add(new XPCKFileInfo()
                    {
                        Entry = entries[i],
                        FileName = nameList[i],
                        FileData = new MemoryStream(xpckBr.ReadBytes(entries[i].fileSize)),
                        State = ArchiveFileState.Archived
                    });
                }
            }
        }

        public void Save(Stream xpck)
        {
            using (BinaryWriterX xpckBw = new BinaryWriterX(xpck))
            {
                int fileOffset = 0;
                int nameListLength = 0;

                //Header
                xpckBw.WriteASCII("XPCK");
                xpckBw.Write((byte)Files.Count());
                xpckBw.Write((byte)0); //unknown
                xpckBw.Write((short)0x5);
                xpckBw.Write((short)((Files.Count() * 0xc + 0x14) / 4));
                xpckBw.Write((short)0);
                xpckBw.Write((short)(Files.Count() * 0xc / 4));
                xpckBw.Write((short)0);
                xpckBw.Write(0);

                //nameList
                xpckBw.BaseStream.Position = 0x14 + Files.Count * 0xc + 4;
                for (int i = 0; i < Files.Count(); i++)
                {
                    xpckBw.WriteASCII(Files[i].FileName); xpckBw.Write((byte)0);
                    nameListLength += Files[i].FileName.Length + 1;
                }
                while (nameListLength % 4 != 0) { xpckBw.Write((byte)0); nameListLength++; }
                xpckBw.BaseStream.Position = 0x14 + Files.Count * 0xc;
                xpckBw.Write(nameListLength << 3);

                //entryList
                xpckBw.BaseStream.Position = 0x14;
                for (int i = 0; i < Files.Count(); i++)
                {
                    xpckBw.Write(Files[i].Entry.crc32);
                    xpckBw.Write(Files[i].Entry.ID);
                    xpckBw.Write((short)(fileOffset / 4));

                    long bk = xpckBw.BaseStream.Position;
                    xpckBw.BaseStream.Position = nameListLength + 0x14 + Files.Count * 0xc + 4 + fileOffset;
                    xpckBw.Write(new BinaryReaderX(Files[i].FileData).ReadBytes(Files[i].Entry.fileSize));
                    xpckBw.BaseStream.Position = bk;

                    fileOffset += (int)Files[i].FileData.Length;
                    while (fileOffset % 4 != 0) fileOffset++;
                    xpckBw.Write(Files[i].Entry.fileSize);
                }
                int dataLength = fileOffset;

                //Header - write missing information
                xpckBw.BaseStream.Position = 0xa;
                xpckBw.Write((short)((nameListLength + 0x14 + Files.Count * 0xc + 4) / 4));
                xpckBw.BaseStream.Position = 0xe;
                xpckBw.Write((short)((nameListLength + 4) / 4));
                xpckBw.BaseStream.Position = 0x10;
                xpckBw.Write(dataLength);
            }
        }
    }
}