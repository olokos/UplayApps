﻿using UplayKit;
using UplayKit.Connection;
using static Downloader.Saving;
using File = System.IO.File;
using UDFile = Uplay.Download.File;

namespace Downloader;

internal class DLWorker
{
    public static void DownloadWorker(DownloadConnection downloadConnection)
    {
        Console.WriteLine("\n\t\tDownloading Started!");
        int filecounter = 0;
        foreach (var file in Config.FilesToDownload)
        {
            if (file.Size == 0)
                continue;

            filecounter++;

            List<string> sliceListIds = new();
            List<string> sliceIds = new();

            foreach (var sl in file.SliceList)
                sliceListIds.Add(Convert.ToHexString(sl.DownloadSha1.ToArray()));

            foreach (var sl in file.Slices)
                sliceIds.Add(Convert.ToHexString(sl.ToArray()));

            var saving = Read();
            if (saving.Verify.Files.Exists(x => x.Name == file.Name))
                continue;

            if (CheckCurrentFile(file, downloadConnection).Result)
                continue;

            saving.Work.FileInfo = new()
            {
                Name = file.Name,
                IDs = new()
                {
                    SliceList = sliceListIds,
                    Slices = sliceIds
                }
            };
            Save(saving);
            Console.WriteLine($"\t\tFile {file.Name} started ({filecounter}/{Config.FilesToDownload.Count}) [{Formatters.FormatFileSize(file.Size)}]");
            DownloadFile(file, downloadConnection);
        }
        Console.WriteLine($"\t\tDownload for app {Config.ProductId} is done!");
    }

    public static async Task<bool> CheckCurrentFile(UDFile file, DownloadConnection downloadConnection)
    {
        if (string.IsNullOrEmpty(Config.VerifyBinPath))
            return false;

        var saving = Read();
        if (saving.Work.FileInfo.Name != file.Name)
            return false;

        var curId = saving.Work.CurrentId;
        var NextId = saving.Work.NextId;
        var verifile = saving.Verify.Files.Where(x => x.Name == file.Name).FirstOrDefault();
        if (verifile == null)
            return false;

        List<string> slicesToDownload = new();
        int index = 0;
        uint Size = 0;
        if (saving.Compression.HasSliceSHA)
        {
            index = saving.Work.FileInfo.IDs.SliceList.FindIndex(0, x => x == curId);
            index += 1;
            slicesToDownload = saving.Work.FileInfo.IDs.SliceList.Skip(index).ToList();

        }
        else
        {
            index = saving.Work.FileInfo.IDs.Slices.FindIndex(0, x => x == curId);
            index += 1;
            slicesToDownload = saving.Work.FileInfo.IDs.Slices.Skip(index).ToList();
        }

        if (slicesToDownload.Count == 0)
            return false;

        var sizelister = file.SliceList.Take(index).ToList();
        foreach (var sizer in sizelister)
        {
            Size += sizer.Size;
        }

        var fullPath = Path.Combine(Config.VerifyBinPath, file.Name);
        var fileInfo = new System.IO.FileInfo(fullPath);
        if (fileInfo.Length != Size)
        {
            Console.WriteLine("Something isnt right, +/- chunk?? Check Error_CheckCurrentFile.txt file!");
            File.WriteAllText("Error_CheckCurrentFile.txt", (uint)fileInfo.Length + " != " + Size + " " + sizelister.Count + " " + index + "\n" + curId + " " + NextId);
            // We try restore chunk after here
            //
            return false;
        }
        Console.WriteLine($"\t\tRedownloading File {file.Name}!");
        await RedownloadSlices(slicesToDownload, file, downloadConnection);
        return true;
    }

    public static async Task RedownloadSlices(List<string> slicesToDownload, UDFile file, DownloadConnection downloadConnection)
    {
        var fullPath = Path.Combine(Config.VerifyBinPath, file.Name);

        var prevBytes = await File.ReadAllBytesAsync(fullPath);

        var fs = File.OpenWrite(fullPath);
        fs.Position = prevBytes.LongLength;
        Console.WriteLine("\t Slices: " + slicesToDownload.Count);
        var splittedList = slicesToDownload.SplitList();
        for (int listcounter = 0; listcounter < splittedList.Count; listcounter++)
        {
            var spList = splittedList[listcounter];
            var dlbytes = await ByteDownloader.DownloadBytes(file, spList, downloadConnection);
            Parallel.ForEach(dlbytes, async barray =>
            {
                await fs.WriteAsync(barray);
                //fs.Flush(true);
            });
            //if (listcounter % 30 == 0)
            //{
            //    Console.WriteLine("%");
            //    await Task.Delay(10);
            //}
            // Thread.Sleep(10);
        }
        fs.Flush();
        fs.Close();
    }

    public static async void DownloadFile(UDFile file, DownloadConnection downloadConnection)
    {
        var saving = Read();
        var fullPath = Path.Combine(Config.DownloadDirectory, file.Name);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null)
            Directory.CreateDirectory(dir);
        var fs = File.OpenWrite(fullPath);
        if (saving.Compression.HasSliceSHA)
        {
            Console.WriteLine("\t Slices: " + file.SliceList.Count);
            var splittedList = file.SliceList.ToList().SplitList();
            for (int listcounter = 0; listcounter < splittedList.Count; listcounter++)
            {
                var spList = splittedList[listcounter];
                var dlbytes = await ByteDownloader.DownloadBytes(file, spList, downloadConnection);
                Parallel.For(0, spList.Count, async i =>
                {
                    var sp = spList[i];
                    var barray = dlbytes[i];
                    if (Config.DownloadAsChunks)
                    {
                        var sliceId = Convert.ToHexString(sp.DownloadSha1.ToArray());
                        var slicepath = SliceManager.GetSlicePath(sliceId, (uint)saving.Version);
                        var fpath = Path.Combine(Config.DownloadDirectory, slicepath);
                        var dir2 = Path.GetDirectoryName(fpath);
                        if (dir2 != null)
                            Directory.CreateDirectory(dir2);
                        if (!File.Exists(fpath))
                            await File.WriteAllBytesAsync(Path.Combine(Config.DownloadDirectory, slicepath), barray);
                    }
                    else
                    {
                        await fs.WriteAsync(barray);
                        //fs.Flush(true);
                    }
                });
                //if (listcounter / 30 == 0)
                //{
                //    Debug.PWDebug("%30 wait 10ms");
                //    await Task.Delay(10);
                //}
                // Thread.Sleep(10);
            }
        }
        else
        {
            Console.WriteLine("\t Slice: " + file.Slices.Count);
            var splittedList = file.Slices.ToList().SplitList();
            for (int listcounter = 0; listcounter < splittedList.Count; listcounter++)
            {
                var spList = splittedList[listcounter];
                var dlbytes = await ByteDownloader.DownloadBytes(file, spList.ToList(), downloadConnection);
                Parallel.For(0, spList.Count, async i =>
                {
                    var sp = spList.ToList()[i];
                    var barray = dlbytes[i];
                    if (Config.DownloadAsChunks)
                    {
                        var sliceId = Convert.ToHexString(sp.ToArray());
                        var slicepath = SliceManager.GetSlicePath(sliceId, (uint)saving.Version);
                        var fpath = Path.Combine(Config.DownloadDirectory, slicepath);
                        var dir2 = Path.GetDirectoryName(fpath);
                        if (dir2 != null)
                            Directory.CreateDirectory(dir2);
                        if (!File.Exists(fpath))
                            await File.WriteAllBytesAsync(Path.Combine(Config.DownloadDirectory, slicepath), barray);
                    }
                    else
                    {
                        await fs.WriteAsync(barray);
                        //fs.Flush(true);
                    }
                  
                });

                //if (listcounter / 30 == 0)
                //{
                //    Debug.PWDebug("%30 wait 10ms");
                //    await Task.Delay(10);
                //}
                // Thread.Sleep(10);
            }
        }
        fs.Flush();
        fs.Close(); 
        Console.WriteLine($"\t\tFile {file.Name} finished");
        if (Config.DownloadAsChunks)
        {
            //we delete the file because we arent even writing to it :)
            File.Delete(fullPath);
        }
        
    }
}
