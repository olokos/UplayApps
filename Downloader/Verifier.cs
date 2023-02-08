﻿using Newtonsoft.Json;
using System.Linq;
using System.Security.Cryptography;
using ZstdNet;
using UDFile = Uplay.Download.File;

namespace Downloader
{
    internal class Verifier
    {
        public static List<UDFile> Verify(List<UDFile> files, Saving.Root saving)
        {
            List<UDFile> fileschecked = new();
            List<UDFile> remover = new();
            Console.WriteLine("\t\tVerification Started!");
            foreach (var file in files)
            {
                if (fileschecked.Contains(file))
                {
                    continue;
                }

                var fullPath = Path.Combine(Downloader.Config.DownloadDirectory, file.Name);
                if (File.Exists(fullPath))
                {
                    var Verified = VerifyFile(file, fullPath, saving, out var failes);
                    string addinfo = "";
                    if (Verified)
                    {
                        addinfo = "Check successful!";
                        remover.Add(file);
                    }
                    else
                    {
                        addinfo = "Check failed!";
                        if (failes.Contains(-1))
                        {
                            addinfo += " (FileSize)";
                        }
                        else
                        {
                            addinfo += " (SHA Missmatch)";
                        }
                        File.AppendAllText("FailedFiles.txt", $"\n{file.Name} - {JsonConvert.SerializeObject(failes)}");
                    }
                    fileschecked.Add(file);
                    Console.WriteLine($"\t\tFile {file.Name} verified! {addinfo}");
                }

            }
            List<UDFile> returner = new();
            returner.AddRange(files);
            foreach (var rf in remover)
            {
                returner.Remove(rf);
            }
            Console.WriteLine("\t\tVerification Done!");
            return returner;
        }

        public static bool VerifyFile(UDFile file, string PathToFile, Saving.Root saving, out List<int> failinplace)
        {
            failinplace = new();
            var fileInfo = new FileInfo(PathToFile);
            if ((ulong)fileInfo.Length != file.Size)
            {
                failinplace.Add(-1);
            }

            var filebytes = File.ReadAllBytes(PathToFile);

            if (saving.Verify.Files.Count == 0)
                goto END;

            var sfile = saving.Verify.Files.Where(x => x.Name == file.Name).FirstOrDefault();
            if (sfile == null)
                goto END;
            var takenSize = 0;
            for (int sinfocount = 0; sinfocount < sfile.SliceInfo.Count; sinfocount++)
            {
                var sinfo = sfile.SliceInfo[sinfocount];
                var fslist = file.SliceList[sinfocount];
                var fibytes = filebytes.Skip(takenSize).Take(sinfo.DecompressedSize).ToArray();
                /*
                var compBytes = LzhamWrapper.Compress(fibytes,(ulong)sinfo.DownloadedSize);
                var compsha1 = GetSHA1Hash(compBytes);
                */
                takenSize += sinfo.DecompressedSize;
                var decsha = GetSHA1Hash(fibytes);
                
                if (sinfo.DecompressedSHA != decsha)
                {
                    Console.WriteLine($"{sinfo.DecompressedSHA} != {decsha}");
                    failinplace.Add(takenSize);
                }
                if (sinfo.DecompressedSize != fibytes.Length)
                {
                    Console.WriteLine($"{sinfo.DecompressedSize} != {fibytes.Length}");
                    failinplace.Add(fibytes.Length * (-1));
                }
            }

        END:
            if (failinplace.Count != 0)
            {
                return false;
            }
            return true;
        }

        public static string GetSHA1Hash(byte[] input)
        {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(input));
        }

    }
}
