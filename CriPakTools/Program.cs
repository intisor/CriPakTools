using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CriPakTools
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("CriPakTools\n");
            Console.WriteLine("Based off Falo's code released on Xentax forums (see readme.txt), modded by Nanashi3 from FuwaNovels.\nInsertion code by EsperKnight\n\n");

            if (args.Length == 0)
            {
                DisplayUsage();
                return;
            }

            string cpk_name = args[0];

            try
            {
                CPK cpk = new CPK(new Tools());
                cpk.ReadCPK(cpk_name);

                
                using (BufferedStream oldFile = new BufferedStream(File.OpenRead(cpk_name)))
              
                {
                    if (args.Length == 1)
                    {
                        DisplayAllChunks(cpk);
                    }
                    else if (args.Length == 2)
                    {
                        ExtractFiles(args[1], cpk, oldFile);
                    }
                    else
                    {
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Usage for insertion CriPakTools IN_CPK REPLACE_THIS REPLACE_WITH [OUT_CPK]");
                            return;
                        }

                        string ins_name = args[1];
                        string replace_with = args[2];

                        InsertFile(cpk_name, cpk, oldFile, ins_name, replace_with, args.Length >= 4 ? args[3] : null);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }
        }

        static void DisplayUsage()
        {
            Console.WriteLine("CriPakTool Usage:\n");
            Console.WriteLine("CriPakTool.exe IN_FILE - Displays all contained chunks.\n");
            Console.WriteLine("CriPakTool.exe IN_FILE EXTRACT_ME - Extracts a file.\n");
            Console.WriteLine("CriPakTool.exe IN_FILE ALL - Extracts all files.\n");
            Console.WriteLine("CriPakTool.exe IN_FILE REPLACE_ME REPLACE_WITH [OUT_FILE] - Replaces REPLACE_ME with REPLACE_WITH. Optional output it as a new CPK file otherwise it's replaced.\n");
        }

        static void DisplayAllChunks(CPK cpk)
        {
            List<FileEntry> entries = cpk.FileTable.OrderBy(x => x.FileOffset).ToList();
            foreach (var entry in entries)
            {
                Console.WriteLine(((entry.DirName != null) ? entry.DirName + "/" : "") + entry.FileName);
            }
        }

        static void ExtractFiles(string extractMe, CPK cpk, BufferedStream oldFile)
        {
            List<FileEntry> entries = (extractMe.ToUpper() == "ALL") ? cpk.FileTable.Where(x => x.FileType == "FILE").ToList() : cpk.FileTable.Where(x => ((x.DirName != null) ? x.DirName + "/" : "") + x.FileName.ToLower() == extractMe.ToLower()).ToList();

            if (entries.Count == 0)
            {
                Console.WriteLine("Cannot find " + extractMe + ".");
                return;
            }

            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.DirName))
                {
                    Directory.CreateDirectory(entry.DirName);
                }

                oldFile.Seek((long)entry.FileOffset, SeekOrigin.Begin);
                string isComp = Encoding.ASCII.GetString(ReadBytes(oldFile, 8));
                oldFile.Seek((long)entry.FileOffset, SeekOrigin.Begin);

                byte[] chunk = ReadBytes(oldFile, int.Parse(entry.FileSize.ToString()));
                if (isComp == "CRILAYLA")
                {
                    int size = Int32.Parse((entries[i].ExtractSize ?? entries[i].FileSize).ToString());
                    chunk = cpk.DecompressCRILAYLA(chunk, size);
                }

                Console.WriteLine("Extracting: " + ((entry.DirName != null) ? entry.DirName + "/" : "") + entry.FileName);
                File.WriteAllBytes(((entry.DirName != null) ? entry.DirName + "/" : "") + entry.FileName, chunk);
            }
        }

        static byte[] ReadBytes(BufferedStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int bytesRead = stream.Read(buffer, 0, count);
            if (bytesRead != count)
            {
                throw new EndOfStreamException();
            }
            return buffer;
        }

        static void InsertFile(string cpk_name, CPK cpk, BufferedStream oldFile, string ins_name, string replace_with, string outputName)
        {
            FileInfo fi = new FileInfo(cpk_name);
            outputName = outputName ?? fi.FullName + ".tmp";

            using (BufferedStream newCPK = new BufferedStream(File.OpenWrite(outputName)))
            {
                List<FileEntry> entries = cpk.FileTable.OrderBy(x => x.FileOffset).ToList();

                foreach (var entry in entries)
                {
                    if (entry.FileType != "CONTENT")
                    {
                        if (entry.FileType == "FILE")
                        {
                            if ((ulong)newCPK.Position < cpk.ContentOffset)
                            {
                                ulong padLength = cpk.ContentOffset - (ulong)newCPK.Position;
                                newCPK.Write(new byte[padLength], 0, (int)padLength);
                            }
                        }

                        if (entry.FileSize == null || entry.FileOffset == null || entry.FileName == null)
                        {
                            throw new NullReferenceException("Critical properties of the file entry are not initialized.");
                        }

                        if (entry.FileName.ToString() != ins_name)
                        {
                            oldFile.Seek((long)entry.FileOffset, SeekOrigin.Begin);
                            entry.FileOffset = (ulong)newCPK.Position;
                            cpk.UpdateFileEntry(entry);

                            byte[] chunk = ReadBytes(oldFile, int.Parse(entry.FileSize.ToString()));
                            newCPK.Write(chunk, 0, chunk.Length);
                        }
                        else
                        {
                            byte[] newbie = File.ReadAllBytes(replace_with);
                            entry.FileOffset = (ulong)newCPK.Position;
                            entry.FileSize = Convert.ChangeType(newbie.Length, entry.FileSizeType);
                            entry.ExtractSize = Convert.ChangeType(newbie.Length, entry.FileSizeType);
                            cpk.UpdateFileEntry(entry);
                            newCPK.Write(newbie, 0, newbie.Length);
                        }

                        if ((newCPK.Position % 0x800) > 0)
                        {
                            int padding = (int)(0x800 - (newCPK.Position % 0x800));
                            newCPK.Write(new byte[padding], 0, padding);
                        }
                    }
                    else
                    {
                        cpk.UpdateFileEntry(entry);
                    }
                }

                cpk.WriteCPK(newCPK);
                cpk.WriteITOC(newCPK);
                cpk.WriteTOC(newCPK);
                cpk.WriteETOC(newCPK);
                cpk.WriteGTOC(newCPK);
            }

            oldFile.Close();

            if (outputName == fi.FullName + ".tmp")
            {
                File.Delete(cpk_name);
                File.Move(outputName, cpk_name);
                File.Delete(outputName);
            }
        }
    }
}
