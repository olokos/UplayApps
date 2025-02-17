﻿using CoreLib;
using Newtonsoft.Json;
using RestSharp;
using UplayKit;
using UplayKit.Connection;

namespace Downloader
{
    public class Program
    {
        public static string OWToken = "";
        public static ulong Exp = 0;
        public static OwnershipConnection? ownershipConnection = null;
        public static DemuxSocket? socket = null;
        static void Main(string[] args)
        {
            if (ParameterLib.HasParameter(args, "-help") || ParameterLib.HasParameter(args, "-?") || ParameterLib.HasParameter(args, "?"))
            {
                PrintHelp();
            }
            #region Argument thingy
            bool haslocal = ParameterLib.HasParameter(args, "-local");
            Debug.isDebug = ParameterLib.HasParameter(args, "-debug");
            int WaitTime = ParameterLib.GetParameter(args, "-time", 5);
            Config.ProductId = ParameterLib.GetParameter<uint>(args, "-product", 0);
            Config.ManifestId = ParameterLib.GetParameter(args, "-manifest", "");
            string manifest_path = ParameterLib.GetParameter(args, "-manifestpath", "");
            bool hasAddons = ParameterLib.HasParameter(args, "-addons");
            string lang = ParameterLib.GetParameter(args, "-lang", "default");
            Config.DownloadDirectory = ParameterLib.GetParameter(args, "-dir", $"{Directory.GetCurrentDirectory()}\\{Config.ProductId}\\{Config.ManifestId}\\");
            Config.UsingFileList = ParameterLib.HasParameter(args, "-skip");
            Config.UsingOnlyFileList = ParameterLib.HasParameter(args, "-only");
            string skipping = ParameterLib.GetParameter(args, "-skip", "skip.txt");
            string onlygetting = ParameterLib.GetParameter(args, "-only", "only.txt");
            Config.Verify = ParameterLib.GetParameter(args, "-verify", true);
            bool hasVerifyPrint = ParameterLib.HasParameter(args, "-vp");
            Config.DownloadAsChunks = ParameterLib.HasParameter(args, "-onlychunk");

            if (Config.UsingFileList && Config.UsingOnlyFileList)
            {
                Console.WriteLine("-skip and -only cannot be used in same time!");
                Environment.Exit(1);
            }
            #endregion

            UbiServices.Urls.IsLocalTest = haslocal;
            #region Login
            var login = LoginLib.LoginArgs_CLI(args);

            if (login == null)
            {
                Console.WriteLine("Login was wrong :(!");
                Environment.Exit(1);
            }

            #endregion
            #region Starting Connections, Getting game
            socket = new();
            socket.WaitInTimeMS = WaitTime;
            Console.WriteLine("Is same Version? " + socket.VersionCheck());
            socket.PushVersion();
            bool IsAuthSuccess = socket.Authenticate(login.Ticket);
            Console.WriteLine("Is Auth Success? " + IsAuthSuccess);
            if (!IsAuthSuccess)
            {
                Console.WriteLine("Oops something is wrong!");
                Environment.Exit(1);
            }
            ownershipConnection = new(socket, login.Ticket, login.SessionId);
            DownloadConnection downloadConnection = new(socket);
            var owned = ownershipConnection.GetOwnedGames(false);
            if (owned == null || owned.Count == 0)
            {
                Console.WriteLine("No games owned?!");
                Environment.Exit(1);
            }
            #endregion
            #region Game printing & Argument Check
            Uplay.Download.Manifest parsedManifest = new();
            RestClient rc = new();

            if (Config.ProductId == 0 && Config.ManifestId == "")
            {
                File.WriteAllText("games_full.json", JsonConvert.SerializeObject(owned.Where(x=>x.ProductType == (uint)Uplay.Ownership.OwnedGame.Types.ProductType.Game), Formatting.Indented));

                owned = owned.Where(game => game.LatestManifest.Trim().Length > 0 
                && game.ProductType == (uint)Uplay.Ownership.OwnedGame.Types.ProductType.Game
                && game.Owned
                && game.State == (uint)Uplay.Ownership.OwnedGame.Types.State.Playable
                && !game.LockedBySubscription
                ).ToList();

                File.WriteAllText("games.json", JsonConvert.SerializeObject(owned, Formatting.Indented));

                Console.WriteLine("-1) Your Downloadable games:.");
                Console.WriteLine("----------------------");
                int gameIds = 0;
                foreach (var game in owned)
                {
                    Console.WriteLine($"\n\t{Appname.GetAppName(game.ProductId)}");
                    Console.WriteLine($"({gameIds}) ProductId ({game.ProductId}) Manifest {game.LatestManifest}");
                    gameIds++;
                }
                Console.WriteLine("Please select:");
                Console.ReadLine();

                int selection = int.Parse(Console.ReadLine()!);
                if (selection == -1)
                {
                    Console.WriteLine("> Input the 20-byte long manifest identifier:");
                    Config.ManifestId = Console.ReadLine()!.Trim();

                    Console.WriteLine("> Input the productId:");
                    Config.ProductId = uint.Parse(Console.ReadLine()!.Trim());
                }
                else if (selection <= gameIds)
                {
                    Config.ManifestId = owned[selection].LatestManifest;
                    Config.ProductId = owned[selection].ProductId;
                    Console.WriteLine(Config.ManifestId + " " + Config.ProductId);
                }

                Config.DownloadDirectory = ParameterLib.GetParameter(args, "-dir", $"{Directory.GetCurrentDirectory()}\\{Config.ProductId}\\{Config.ManifestId}\\");
                Config.ProductManifest = $"{Config.ProductId}_{Config.ManifestId}";

                if (!Directory.Exists(Config.DownloadDirectory))
                {
                    Directory.CreateDirectory(Config.DownloadDirectory);
                }

                // Getting ownership token
                var ownershipToken = ownershipConnection.GetOwnershipToken(Config.ProductId);
                Console.WriteLine((ownershipConnection.IsConnectionClosed == false | string.IsNullOrEmpty(ownershipToken.Item1)) + " " + ownershipConnection.IsConnectionClosed + " " + string.IsNullOrEmpty(ownershipToken.Item1) + " " + ownershipToken.Item1);
                if (ownershipConnection.IsConnectionClosed == true || string.IsNullOrEmpty(ownershipToken.Item1)) { throw new("Product not owned"); }
                OWToken = ownershipToken.Item1;
                Exp = ownershipToken.Item2;
                Console.WriteLine($"Expires in {GetTimeFromEpoc(Exp)}");
                downloadConnection.InitDownloadToken(OWToken);

                if (manifest_path != "")
                {
                    File.Copy(manifest_path, Config.DownloadDirectory + "/uplay_install.manifest", true);
                    parsedManifest = Parsers.ParseManifestFile(manifest_path);
                }
                else
                {
                    var manifestUrls = downloadConnection.GetUrl(Config.ManifestId, Config.ProductId);

                    foreach (var url in manifestUrls)
                    {
                        var manifestBytes = rc.DownloadData(new(url));
                        if (manifestBytes == null)
                            continue;
                        File.WriteAllBytes(Config.ProductManifest + ".manifest", manifestBytes);
                        parsedManifest = Parsers.ParseManifestFile(Config.ProductManifest + ".manifest");
                    }
                }
            }
            #endregion
            #region Game from Argument
            else
            {
                if (!Directory.Exists(Config.DownloadDirectory))
                {
                    Directory.CreateDirectory(Config.DownloadDirectory);
                }
                var ownershipToken = ownershipConnection.GetOwnershipToken(Config.ProductId);
                if (ownershipConnection.IsConnectionClosed == false || string.IsNullOrEmpty(ownershipToken.Item1)) { throw new("Product not owned"); }
                OWToken = ownershipToken.Item1;
                Exp = ownershipToken.Item2;
                Console.WriteLine($"Expires in {GetTimeFromEpoc(Exp)}");
                downloadConnection.InitDownloadToken(OWToken);
                if (manifest_path != "")
                {
                    File.Copy(manifest_path, Config.DownloadDirectory + "/uplay_install.manifest", true);
                    parsedManifest = Parsers.ParseManifestFile(manifest_path);
                }
                else
                {
                    var manifestUrls = downloadConnection.GetUrl(Config.ManifestId, Config.ProductId);
                    foreach (var url in manifestUrls)
                    {
                        var manifestBytes = rc.DownloadData(new(url));
                        if (manifestBytes == null)
                            continue;
                        File.WriteAllBytes(Config.ProductManifest + ".manifest", manifestBytes);
                        File.Copy(Config.ProductManifest + ".manifest", Config.DownloadDirectory + "/uplay_install.manifest", true);
                        parsedManifest = Parsers.ParseManifestFile(Config.ProductManifest + ".manifest");
                    }
                }
            }
            #endregion
            #region Addons check
            if (hasAddons)
            {
                var LicenseURLs = downloadConnection.GetUrl(Config.ManifestId, Config.ProductId, "license");
                foreach (var url in LicenseURLs)
                {
                    var bytes = rc.DownloadData(new(url));
                    if (bytes == null)
                        continue;
                    File.WriteAllBytes(Config.ProductManifest + ".license", bytes);
                }

                var MetadataURLs = downloadConnection.GetUrl(Config.ManifestId, Config.ProductId, "metadata");
                foreach (var url in LicenseURLs)
                {
                    var bytes = rc.DownloadData(new(url));
                    if (bytes == null)
                        continue;
                    File.WriteAllBytes(Config.ProductManifest + ".metadata", bytes);
                }
            }
            rc.Dispose();
            #endregion
            #region Compression Print
            Console.WriteLine($"\nDownloaded and parsed manifest successfully:");
            Console.WriteLine($"Compression Method: {parsedManifest.CompressionMethod} IsCompressed? {parsedManifest.IsCompressed} Version {parsedManifest.Version}");
            #endregion
            #region Lang Chunks
            List<Uplay.Download.File> files = new();

            if (parsedManifest.Languages.ToList().Count > 0)
            {
                if (lang == "default")
                {
                    Console.WriteLine("Languages to use (just press enter to choose nothing, and all for all chunks)");
                    parsedManifest.Languages.ToList().ForEach(x => Console.WriteLine(x.Code));

                    var langchoosed = Console.ReadLine();

                    if (!string.IsNullOrEmpty(langchoosed))
                    {
                        if (langchoosed == "all")
                        {
                            files = ChunkManager.AllFiles(parsedManifest);
                        }
                        else
                        {
                            files.AddRange(ChunkManager.RemoveNonEnglish(parsedManifest));
                            lang = langchoosed;
                            files.AddRange(ChunkManager.AddLanguage(parsedManifest, lang));
                        }
                    }
                    else
                    {
                        files.AddRange(ChunkManager.RemoveNonEnglish(parsedManifest));

                    }
                }
                else if (lang == "all")
                {
                    files = ChunkManager.AllFiles(parsedManifest);
                }
                else
                {
                    files.AddRange(ChunkManager.RemoveNonEnglish(parsedManifest));
                    files.AddRange(ChunkManager.AddLanguage(parsedManifest, lang));
                }
            }
            else
            {
                files = ChunkManager.AllFiles(parsedManifest);
            }
            #endregion
            #region Skipping files from chunk
            Config.FilesToDownload = DLFile.FileNormalizer(files);
            List<string> skip_files = new();
            if (Config.UsingFileList)
            {
                if (File.Exists(skipping))
                {
                    var lines = File.ReadAllLines(skipping);
                    skip_files.AddRange(lines);
                    Console.WriteLine("Skipping files Added");
                }
                ChunkManager.RemoveSkipFiles(skip_files);
            }
            if (Config.UsingOnlyFileList)
            {
                if (File.Exists(onlygetting))
                {
                    var lines = File.ReadAllLines(onlygetting);
                    skip_files.AddRange(lines);
                    Console.WriteLine("Download only Added");
                }
                Config.FilesToDownload = ChunkManager.AddDLOnlyFiles(skip_files);
            }
            Console.WriteLine("\tFiles Ready to work\n");
            #endregion
            #region Saving
            Saving.Root saving = new();
            Config.VerifyBinPath = Path.Combine(Config.DownloadDirectory, ".UD\\verify.bin");
            var verifybinPathDir = Path.GetDirectoryName(Config.VerifyBinPath);
            if (verifybinPathDir != null)
                Directory.CreateDirectory(verifybinPathDir);
            if (File.Exists(Path.Combine(Config.DownloadDirectory, ".UD\\verify.bin.json")))
            {
                Saving.Root? root = JsonConvert.DeserializeObject<Saving.Root>(File.ReadAllText(Path.Combine(Config.DownloadDirectory, ".UD\\verify.bin.json")));
                if (root == null)
                    root = new();
                saving = root;
            }
            else if (File.Exists(Config.VerifyBinPath))
            {
                var readedBin = Saving.Read();
                if (readedBin == null)
                {
                    saving = Saving.MakeNew(Config.ProductId, Config.ManifestId, parsedManifest);
                }
                else
                {
                    saving = readedBin;
                }
            }
            else
            {
                saving = Saving.MakeNew(Config.ProductId, Config.ManifestId, parsedManifest);
            }
            if (hasVerifyPrint)
            {
                File.WriteAllText(Config.VerifyBinPath + ".json", JsonConvert.SerializeObject(saving));
                Console.ReadLine();
            }
            Saving.Save(saving);
            #endregion
            #region Verify + Downloading
            if (Config.Verify && !Config.DownloadAsChunks)
            {
                Verifier.Verify();
            }
            /*
            var resRoot = AutoRes.MakeNew(Config.ProductId, Config.ManifestId, Config.DownloadDirectory, Config.VerifyBinPath, Path.Combine(Config.DownloadDirectory, "uplay_install.manifest"));
            AutoRes.Save(resRoot);
            */
            DLWorker.DownloadWorker(downloadConnection);
            #endregion
            #region Closing and GoodBye
            Console.WriteLine("Goodbye!");
            Console.ReadLine();
            downloadConnection.Close();
            ownershipConnection.Close();
            socket.Disconnect();
            #endregion
        }
        #region Other Functions

        static void PrintHelp()
        {
            CoreLib.HelpArgs.PrintHelp();
            Console.WriteLine("\n");
            Console.WriteLine("\t\tWelcome to Uplay Downloader CLI!");
            Console.WriteLine();
            Console.WriteLine("\t Arguments\t\t Arguments Description");
            Console.WriteLine();
            Console.WriteLine("\t -debug\t\t\t Debugging every request/response");
            Console.WriteLine("\t -time\t\t\t Using that as a wait time (5 is default [Low is better])");
            Console.WriteLine("\t -product\t\t Id of the Product");
            Console.WriteLine("\t -manifest\t\t Manifest of the Product");
            Console.WriteLine("\t -manifestpath\t\t Path to Manifest file");
            Console.WriteLine("\t -lang\t\t\t Download selected lang if available");
            Console.WriteLine("\t -skip\t\t\t Skip files from downloading");
            Console.WriteLine("\t -only\t\t\t Downloading only selected files from txt");
            Console.WriteLine("\t -dir\t\t\t A Path where to download the files");
            Console.WriteLine("\t -vp\t\t\t Make a json from verify.bin");
            Console.WriteLine("\t -verify\t\t Verifying files before downloading");
            Console.WriteLine("\t -onlychunk\t\t\t Downloading only the Uncompressed Chunks");
            Console.WriteLine();
            Environment.Exit(0);
        }

        static DateTime GetTimeFromEpoc(ulong epoc)
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return dateTime.AddSeconds(epoc);
        }

        static ulong GetEpocTime()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return (ulong)t.TotalSeconds;

        }

        public static void CheckOW(uint ProdId)
        {
            if (Exp <= GetEpocTime())
            {
                Console.WriteLine("Your token has no more valid, getting new!");
                if (ownershipConnection != null && !ownershipConnection.IsConnectionClosed)
                {
                    var token = ownershipConnection.GetOwnershipToken(ProdId);
                    Exp = token.Item2;
                    OWToken = token.Item1;
                    Console.WriteLine("Is Token get success? " + ownershipConnection.IsConnectionClosed + " " + (Exp != 0));

                }
            }
        }
        #endregion
    }
}
