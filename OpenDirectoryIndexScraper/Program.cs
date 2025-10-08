using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OpenDirectoryIndexScraper
{


    public struct FoundFile
    {
        public string localPath;
        public string absPath;
        public DateTime parsedDateTime;
    }
    class Program
    {

        static char[] badChars = (new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "!").ToCharArray(); // Linux doesn't like !

        public static string cleanupPath(string path, char replacement = '_')
        {
            return string.Join(replacement, path.Split(badChars));
        }

        static bool getFileSizes = false;

        enum Mode { 
            Normal,
            Monitor,
            Restore
        }

        class FileToRestore {
            public string filename;
            public string trueFilename;
            public string originalPath;
            public DateTime date;

            static int prefixLength = "yyyy-MM-dd_HH-mm-ss_".Length;

            //yyyy-MM-dd_HH-mm-ss
            static Regex dateParse = new Regex(@"^(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})_(?<h>\d{2})-(?<min>\d{2})-(?<s>\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
            public FileToRestore(string path, StringBuilder sb)
            {
                trueFilename = filename = Path.GetFileName(path);
                date = File.GetLastWriteTime(path);
                originalPath = path;

                Match dateMatch = dateParse.Match(filename);
                if (dateMatch.Success)
                {
                    DateTime tmpDate = new DateTime(int.Parse(dateMatch.Groups["y"].Value), int.Parse(dateMatch.Groups["m"].Value), int.Parse(dateMatch.Groups["d"].Value), int.Parse(dateMatch.Groups["h"].Value), int.Parse(dateMatch.Groups["min"].Value), int.Parse(dateMatch.Groups["s"].Value), DateTimeKind.Local);
                    if(tmpDate != date)
                    {
                        Console.WriteLine($"Warning: Date mismatch between last written and parsed date on {trueFilename}: {date} (last written) vs {tmpDate} (parsed)");
                        sb.AppendLine($"# Warning: Date mismatch between last written and parsed date on {trueFilename}: {date} (last written) vs {tmpDate} (parsed)");
                        date = tmpDate;
                    }
                    //if(tmpDate < date)
                    //{
                    //    date = tmpDate;
                    //}
                    if(filename.Length > prefixLength)
                    {
                        filename = filename.Substring(prefixLength);
                    }
                }
            }
        }


        static void GetFileDate(string path, string filename)
        {

        }

        static void Restore(string rootIn, string rootOut, string inFolder, string outFolder, StringBuilder sb)
        {
            string relOut = Path.GetRelativePath(rootOut, outFolder);
            if(relOut != ".")
            {
                sb.AppendLine($"mkdir -p \"{Path.Combine(rootOut,relOut)}\"");
            }

            //inFolder = Path.GetFullPath(inFolder);
            //outFolder = Path.GetFullPath(outFolder);

            string[] filePaths = Directory.GetFiles(inFolder);
            List<FileToRestore> files = new List<FileToRestore>();
            foreach(string file in filePaths)
            {
                files.Add(new FileToRestore(file,sb));
            }

            foreach(FileToRestore file in files)
            {
                bool haveNewerVersion = false;
                bool unresolvable = false;
                FileToRestore newestVersion = null;
                int alts = 0;
                List<FileToRestore> unresolvables = new List<FileToRestore>();
                foreach (FileToRestore file2 in files)
                {
                    if(file2.filename == file.filename)
                    {
                        if (file == file2) continue;
                        alts++;
                        if (file2.date > file.date)
                        {
                            haveNewerVersion = true;
                            newestVersion = file2;
                        } else if (file2.date == file.date)
                        {
                            unresolvable = true;
                            unresolvables.Add(file2);
                        }
                    }
                }
                if (unresolvable)
                {
                    Console.WriteLine($"Ignoring {file.trueFilename}, unresolvable date conflict (identical date) with the following files:");
                    sb.AppendLine($"# Ignoring {file.trueFilename}, unresolvable date conflict (identical date) with the following files:");
                    foreach (FileToRestore f in unresolvables)
                    {
                        Console.WriteLine(f.trueFilename);
                        sb.AppendLine($"# {f.trueFilename}");
                    }
                    continue;
                }
                if (haveNewerVersion)
                {
                    Console.WriteLine($"Ignoring {file.trueFilename}, {newestVersion.trueFilename} is newer ({alts+1} variants total).");
                    continue;
                }
                Console.WriteLine($"Copying {file.trueFilename} ({alts} older dupes skipped).");
                //string relIn = Path.GetRelativePath(rootIn, file.filename);
                string outname = Path.Combine(rootOut,relOut, file.filename);
                if(alts> 0)
                {
                    sb.AppendLine($"# {alts} dupes skipped");
                }
                sb.AppendLine($"cp -p \"{file.originalPath}\" \"{outname}\" ");
            }

            string[] folders = Directory.GetDirectories(inFolder);
            foreach(string folder in folders)
            {
                string newOut = Path.Combine(outFolder, Path.GetRelativePath(inFolder, folder));
                Restore(rootIn,rootOut, folder, newOut,sb);
            }
        }

        static void Main(string[] args)
        {
            Mode mode = Mode.Normal;
            mode = args.Length > 1 && args[0] == "-monitor" ? Mode.Monitor : mode;
            mode = args.Length > 1 && args[0] == "-restore" ? Mode.Restore : mode;
            int monitorRecrawlDelay = 60000;
            if (mode == Mode.Monitor) monitorRecrawlDelay = int.Parse(args[1]);

            List<Task> tasks = new List<Task>();

            if(mode == Mode.Restore)
            {
                if(args.Length < 3)
                {
                    Console.WriteLine( "need in and out folder");
                    return;
                }
                string inFolder = args[1];
                string outFolder = args[2];
                if (!Directory.Exists(inFolder))
                {
                    Console.WriteLine("in folder not found");
                    return;
                }
                StringBuilder sb = new StringBuilder();
                Restore(inFolder, outFolder, inFolder, outFolder,sb);
                File.WriteAllText(GetUnusedFilename("restore.sh"),sb.ToString());
                return;
            }

            int index = 0;
            foreach(string arg in args)
            {
                if (mode == Mode.Monitor && index++ > 1)
                {
                    tasks.Add(monitor(arg, monitorRecrawlDelay));
                } else if(mode == Mode.Normal)
                {
                    tasks.Add(scrape(arg));
                }
            }
#if DEBUG
            //scrape("https://caad.ifreviews.org/");
            //scrape("https://pdsimage2.wr.usgs.gov/archive/LO_1001/");
            //scrape("https://www.crawl-forever.com/downloads/");
            //scrape("http://89.179.240.237/");
            //tasks.Add(scrape("http://74.91.123.99/races/"));

#endif
            Task.WaitAll(tasks.ToArray());
            lock (consoleReadMutex)
            {
                Console.WriteLine("\n\n\n\nDone! Press any key to exit...");
                Console.ReadKey();
            }
        }

        //private static Regex linkGetter = new Regex(@"<a\s+href=""(?<link>[^/\?""]+)(?<linkDirSep>/?)"">(?<linkText>[^/\?""<]+)(?<linkTextDirSep>/?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex linkGetter = new Regex(@"<a\s+href=""(?<link>[^/\?""]+)(?<linkDirSep>/?)"">\s*(<img[^>]*?>)?\s*(?<linkText>[^/\?""<]+)(?<linkTextDirSep>/?)\s*</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex linkGetterDate = new Regex(@"<a\s+href=""(?<link>[^\/\?""]+)(?<linkDirSep>\/?)"">\s*(<img[^>]*?>)?\s*(?<linkText>[^\/\?""<]+)(?<linkTextDirSep>\/?)\s*<\/a>[^\n\r]*?(?<dateTime>\d+-[^\n\r]+?)(?:  |<)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex linkGetterDateMongoose = new Regex(@"<a\s+href=""(?<link>[^\/\?""]+)(?<linkDirSep>\/?)"">\s*(<img[^>]*?>)?\s*(?<linkText>[^\/\?""<]+)(?<linkTextDirSep>\/?)\s*<\/a>[^\n\r]*?<\/td>\s*<td\s+name=\s*(?<dateTime>\d+)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex mongooseDetect = new Regex(@"<address>Mongoose v.(?<version>[^<\n\r]*)<\/address><\/body><\/html>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

       
        private struct PathPair
        {
            public string url;
            public string localPath;
        }

        static void EvaluateKeys()
        {
            lock (consoleReadMutex)
            {
                while (Console.KeyAvailable)
                {
                    var readKey = Console.ReadKey(false);
                    if (readKey.Key == ConsoleKey.Escape)
                    {
                        executionEndRequested = true;
                    }
                    if (readKey.Key == ConsoleKey.DownArrow)
                    {
                        executionContinueRequested = true;
                    }
                }
            }
        }

        static bool executionEndRequested = false;
        static bool executionContinueRequested = false;
        
        static Mutex consoleReadMutex = new Mutex();
        static bool ShouldEnd()
        {
            EvaluateKeys();
            if (executionEndRequested) return true;
            return false;
        }
        static bool ShouldContinue()
        {
            EvaluateKeys();
            if (executionContinueRequested)
            {
                executionContinueRequested = false;
                return true;
            }
            return false;
        }



        static async Task monitor(string baseURL,int reCrawlDelay)
        {

            JsonSerializerOptions opts = new JsonSerializerOptions();
            opts.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;
            opts.WriteIndented = true;

            string stateJSONFile = "ODIS_state_"+MakeValidFileName(baseURL) + ".json";

            //Dictionary<string, DateTime> urlTimes = new Dictionary<string, DateTime>();
            //List<string> directoriesWithSubFolders = new List<string>();

            MonitorState ms = new MonitorState();

            if (File.Exists(stateJSONFile))
            {
                string lines = File.ReadAllText(stateJSONFile); 
                MonitorState deSerialized = JsonSerializer.Deserialize<MonitorState>(lines, opts);
                if(ms != null)
                {
                    ms = deSerialized;
                }
                /*if(deSerialized.urlTimes != null)
                {
                    urlTimes = deSerialized.urlTimes;
                }
                if(deSerialized.directoriesWithSubFolders != null)
                {
                    directoriesWithSubFolders = deSerialized.directoriesWithSubFolders;
                }*/
            }


            //bool serverGivesDateModified = true;
            const int dateTimeModifiedOffsetsRequired = 5;
            //List<Int64> dateModifiedOffsets = new List<Int64>();
            //TimeSpan? dateModifiedDeterminedOffset = null;

            //List<FoundFile> foundFiles = new List<FoundFile>();

            //bool alreadyRun = false;
            while (true) {

                ms.foundFiles.Clear();
                Console.WriteLine($"Press ESC to stop monitoring, or arrow-down to do next crawl now (current default delay: {reCrawlDelay}ms)");
                int doneDelay = 0;
                if (ms.alreadyRun)
                {
                    while(doneDelay < reCrawlDelay)
                    {
                        System.Threading.Thread.Sleep(1000);
                        doneDelay += 1000;
                        if (ShouldEnd() || ShouldContinue())
                        {
                            break;
                        }
                    }
                }
                if (ShouldEnd())
                {
                    break;
                }

                Uri myUri = new Uri(baseURL);
                string basePath = myUri.LocalPath;
                baseURL = myUri.AbsoluteUri;
                if(basePath[basePath.Length-1] != '/')
                {
                    basePath += "/"; 
                    baseURL += "/"; 
                }
                while(basePath.Length > 0 && (basePath[0] == '/' || basePath[0] == '\\'))
                {
                    basePath = basePath.Substring(1);
                }
                //basePath = "." + basePath;

                Queue<PathPair> urlsToScrape = new Queue<PathPair>();
                //Dictionary<string, long?> foundFiles = new Dictionary<string, long?>();
                urlsToScrape.Enqueue(new PathPair() { url= baseURL ,localPath=basePath});

                int unchangedFoldersSkipped = 0;
                int filesFound = 0;
                int hrefNonMatch = 0;

                while(urlsToScrape.Count > 0)
                {
                    PathPair currentUrl = urlsToScrape.Peek();
                    PatientRequester.Response response = await PatientRequester.get(currentUrl.url);
                    if(response.success == true)
                    {
                        MatchCollection matches;
                        bool isMongoose = false;
                        if (mongooseDetect.Match(response.responseAsString).Success)
                        {
                            isMongoose = true;
                            matches = linkGetterDateMongoose.Matches(response.responseAsString);
                        }
                        else
                        {
                            matches = linkGetterDate.Matches(response.responseAsString);
                        }


                        if(matches != null)
                        {

                            foreach (Match match in matches)
                            {
                                if(match.Success && match.Groups != null)
                                {
                                    string link = match.Groups["link"].Value;
                                    string linkDirSep = match.Groups["linkDirSep"].Value;
                                    string linkText = match.Groups["linkText"].Value;
                                    string linkTextDirSep = match.Groups["linkTextDirSep"].Value;
                                    string dateTimeText = match.Groups["dateTime"].Value;

                                    DateTime parsedDateTime;
                                    if (isMongoose)
                                    {
                                        long time = 0;
                                        if(long.TryParse(dateTimeText,out time))
                                        {
                                            parsedDateTime = DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime.ToLocalTime();
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Mongoose timestamp '{dateTimeText}' parse error.");
                                            continue;
                                        }
                                    }
                                    else
                                    {

                                        try {

                                            parsedDateTime = DateTime.Parse(dateTimeText);
                                            if(parsedDateTime.Kind == DateTimeKind.Unspecified)
                                            {
                                                parsedDateTime = DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Local);
                                            } else
                                            {
                                                parsedDateTime = parsedDateTime.ToLocalTime();
                                            }
                                        } catch(Exception e)
                                        {
                                            Console.WriteLine($"Datetime '{dateTimeText}' parse error: {e.ToString()}");
                                            continue;
                                        }
                                    }

                                    if(link + linkDirSep != linkText + linkTextDirSep)
                                    {
                                        //Console.WriteLine("strange... href does not match text");
                                        hrefNonMatch++;
                                    }

                                    string absPath = currentUrl.url + link + linkDirSep;
                                    string localPath = currentUrl.localPath + cleanupPath(HttpUtility.UrlDecode(link)) + linkDirSep;

                                    if (linkDirSep != "")
                                    {

                                        // It's a folder
                                        if (link != ".." && link != ".") // We only go deeper, not up.
                                        {
                                            if (!ms.directoriesWithSubFolders.Contains(currentUrl.url))
                                            {
                                                ms.directoriesWithSubFolders.Add(currentUrl.url);
                                            }

                                            //File.AppendAllText(shFile, "mkdir -p '"+localPath+"'" + "\n");
                                            if (ms.urlTimes.ContainsKey(absPath))
                                            {
                                                if (ms.directoriesWithSubFolders.Contains(absPath))
                                                {
                                                    Console.WriteLine("Directory contains subfolders, hence cannot determine if changed; enqueued: " + localPath + "," + absPath);
                                                    urlsToScrape.Enqueue(new PathPair() { url = absPath, localPath = localPath });
                                                }
                                                else if(ms.urlTimes[absPath] == parsedDateTime)
                                                {
                                                    //Console.WriteLine("Directory date time seemingly not changed, skipping: " + localPath + "," + absPath);
                                                    unchangedFoldersSkipped++;
                                                } else
                                                {
                                                    Console.WriteLine("Directory date time seemingly changed, enqueued: " + localPath + "," + absPath);
                                                    urlsToScrape.Enqueue(new PathPair() { url = absPath, localPath = localPath });
                                                }
                                            } else
                                            {
                                                Console.WriteLine("Directory not yet crawled this run, enqueued: " + localPath + "," + absPath);
                                                urlsToScrape.Enqueue(new PathPair() { url = absPath, localPath = localPath });
                                            }
                                            ms.urlTimes[absPath] = parsedDateTime;
                                        }
                                    } else
                                    {
                                        // It's a file.
                                        if (link != ".." && link != ".") // Shouldn't be necessary bc the directories add "/" behind folders, but just to be safe...
                                        {

                                            long? fileSize = null;

                                            int? statusCode = null;
                                            if (/*getFileSizes*/ms.serverGivesDateModified && ms.dateModifiedDeterminedOffset == null && ms.dateModifiedOffsets.Count < dateTimeModifiedOffsetsRequired)
                                            {
                                                // We are not doing this in order to get the actual datetime. We wanna parse the actual datetime from the listing.
                                                // But in order to get the time in the correct tiemzone, we need to calculate the offset from the server listing's displayed time
                                                // and our actual local time.
                                                PatientRequester.Response headResponse = await PatientRequester.head(absPath);

                                                statusCode = headResponse.statusCode;
                                                if (headResponse.success == true)
                                                {
                                                    //fileSize = headResponse.headerContentLength;
                                                    if (!headResponse.dateTime.HasValue)
                                                    {
                                                        ms.serverGivesDateModified = false;
                                                        Console.WriteLine("Server does not send Date-Modified.: " + localPath + "," + absPath);
                                                    } else
                                                    {
                                                        Int64 dateModifiedOffset = (Int64)(Math.Floor((headResponse.dateTime.Value - parsedDateTime).TotalSeconds/60)+0.5f);
                                                        ms.dateModifiedOffsets.Add(dateModifiedOffset);
                                                        Console.WriteLine($"Date modified offset {ms.dateModifiedOffsets.Count}/{dateTimeModifiedOffsetsRequired} logged: {headResponse.dateTime.Value}-{parsedDateTime}={dateModifiedOffset}  :"+ localPath + "," + absPath);
                                                    }
                                                }
                                                else
                                                {
                                                    //File.AppendAllText(shFile, "# Error " + headResponse.statusCode + " during HEAD to \"" + absPath + "\", possibly can't be downloaded" + "\n");
                                                }

                                            } else
                                            {
                                                fileSize = -1;
                                                statusCode = -1;
                                            }
                                            string line =  absPath+";" + localPath + ";"+currentUrl.localPath+";"+ statusCode;
                                            //File.AppendAllText(csvFile, line + "\n");
                                            //File.AppendAllText(shFile, "wget -c --retry-connrefused --tries=0 --timeout=500 -O '" + localPath + "' '" + absPath + "'"+" # Filesize: "+fileSize + "\n");
                                            //Console.WriteLine("File found: "+line);
                                            filesFound++;
                                            ms.foundFiles.Add(new FoundFile() { absPath=absPath,localPath=localPath,parsedDateTime=parsedDateTime });
                                            //foundFiles.Add(absPath,fileSize);
                                        }
                                    }
                                }
                            }
                        }


                        urlsToScrape.Dequeue();
                    }
                    else
                    {
                        // Skip this item if error code is 403 or 404 (aka if no matter how often we retry, we won't get a proper response)
                        if(response.statusCode == 403 || response.statusCode == 404)
                        {
                            //File.AppendAllText(shFile, @"# Failed to scrape "+currentUrl.url+", error "+response.statusCode + "\n");
                            urlsToScrape.Dequeue();
                        }
                        else
                        {
                            urlsToScrape.Dequeue(); // eh we have to do thiis anyway? to avoid endless loop?
                        }
                    }

                    ((Action)(() => { }))(); // Just so i have a breakpoint.
                }


                Console.WriteLine($"{filesFound} files found, {unchangedFoldersSkipped} folders with unchanged datetime skipped.");
                if(hrefNonMatch > 0)
                {

                    Console.WriteLine($"NOTE: {hrefNonMatch}x href does not match text.");
                }

                // Do we have proper offset info?
                // Calculate it if we can.
                if(!ms.dateModifiedDeterminedOffset.HasValue && ms.dateModifiedOffsets.Count >= dateTimeModifiedOffsetsRequired)
                {
                    Console.WriteLine("Trying to calculate date modified offset median.");
                    // Get median
                    ms.dateModifiedOffsets.Sort();
                    Int64 median = ms.dateModifiedOffsets[ms.dateModifiedOffsets.Count/2]; // For example count is 5. divide by 2 = 2.5 (but since integer, it's 2). And 2 is perfect middle index.
                    // Now check if the median is 
                    Console.WriteLine($"Median is {median}. Now checking consistency.");

                    int countConsistent = 0;
                    foreach(Int64 doffset in ms.dateModifiedOffsets)
                    {
                        if(doffset == median)
                        {
                            countConsistent++;
                        }
                        else
                        {
                            Console.WriteLine($"Value doffset {doffset} inconsistent with {median}.");
                        }
                    }

                    if (countConsistent >= dateTimeModifiedOffsetsRequired - 1) // Allow 1 to be inconsistent
                    {
                        Console.WriteLine($"Median is consistent with {countConsistent} of {ms.dateModifiedOffsets.Count} measured offsets. Ok!");
                        //ms.dateModifiedDeterminedOffset = new TimeSpan(0, (int)median,0);
                        ms.dateModifiedDeterminedOffset = median;
                    } else
                    {
                        Console.WriteLine($"Median is consistent with {countConsistent} of {ms.dateModifiedOffsets.Count} measured offsets. Not ok, purging offsets.");
                        ms.dateModifiedOffsets.Clear();
                    }

                } else if (!ms.serverGivesDateModified)
                {
                    Console.WriteLine("Server does not send Date-Modified header. Setting offset to 0.");
                    //ms.dateModifiedDeterminedOffset = new TimeSpan(0);
                    ms.dateModifiedDeterminedOffset = 0;
                }

                // Ok do actual processing of files maybe.
                if(ms.dateModifiedDeterminedOffset.HasValue)
                {
                    Console.WriteLine($"Processing found files...");
                    int countAlreadyExisting = 0;
                    foreach(FoundFile foundFile in ms.foundFiles)
                    {
                        //DateTime fixedLocalDateTime = foundFile.parsedDateTime + ms.dateModifiedDeterminedOffset.Value;
                        DateTime fixedLocalDateTime = foundFile.parsedDateTime + new TimeSpan(0, (int)ms.dateModifiedDeterminedOffset.Value, 0);
                        string fixedLocalDateTimeString = fixedLocalDateTime.ToString("yyyy-MM-dd_HH-mm-ss");
                        string fullLocalPath = Path.Combine(Path.GetDirectoryName(foundFile.localPath),$"{fixedLocalDateTimeString}_{Path.GetFileNameWithoutExtension(foundFile.localPath)}{Path.GetExtension(foundFile.localPath)}");
                        if (File.Exists(fullLocalPath))
                        {
                            countAlreadyExisting++;
                        }
                        else
                        {
                            Console.WriteLine($"Downloading {fixedLocalDateTimeString} version of {foundFile.absPath}");
                            PatientRequester.Response response = await PatientRequester.get(foundFile.absPath);
                            if (response.success)
                            {
                                Console.WriteLine("Download succeeded. Saving.");
                                Directory.CreateDirectory(Path.GetDirectoryName(foundFile.localPath));
                                File.WriteAllBytes(fullLocalPath,response.rawData);
                                if (response.dateTime.HasValue) // More precise date modified if available :) Don't use for filename tho, else we can't tell what we already have.
                                {
                                    File.SetLastWriteTime(fullLocalPath, response.dateTime.Value);
                                }
                                else
                                {
                                    File.SetLastWriteTime(fullLocalPath, fixedLocalDateTime);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Download failed.");
                            }
                        }
                    }
                    Console.WriteLine($"Skipped {countAlreadyExisting} existing files.");
                } else
                {
                    Console.WriteLine($"Cannot process found files. Date modified offset not determined yet.");
                    ms.urlTimes.Clear(); // Otherwise it won't redownload. Bad.
                }


                ms.alreadyRun = true;

            }


            string saveFileDataJson = JsonSerializer.Serialize(ms, opts);

            if(saveFileDataJson != null)
            {
                File.WriteAllText(stateJSONFile, saveFileDataJson);
            }

            //File.AppendAllText(shFile, @"read -n1 -r" + "\n");
        }
        
        static async Task scrape(string baseURL)
        {

            string csvFile = GetUnusedFilename(MakeValidFileName(baseURL)+".csv");
            string shFile = GetUnusedFilename(MakeValidFileName(baseURL)+".sh");

            File.AppendAllText(shFile,@"#!/bin/bash"+"\n");
            File.AppendAllText(csvFile, @"URL;Size;LocalPath;LocalFolder;StatusCode"+"\n");
            

            Uri myUri = new Uri(baseURL);
            string basePath = myUri.LocalPath;
            baseURL = myUri.AbsoluteUri;
            if(basePath[basePath.Length-1] != '/')
            {
                basePath += "/"; 
                baseURL += "/"; 
            }
            basePath = "." + basePath;

            Queue<PathPair> urlsToScrape = new Queue<PathPair>();
            Dictionary<string, long?> foundFiles = new Dictionary<string, long?>();
            urlsToScrape.Enqueue(new PathPair() { url= baseURL ,localPath=basePath});

            while(urlsToScrape.Count > 0)
            {
                PathPair currentUrl = urlsToScrape.Peek();
                PatientRequester.Response response = await PatientRequester.get(currentUrl.url);
                if(response.success == true)
                {
                    MatchCollection matches = linkGetter.Matches(response.responseAsString);

                    if(matches != null)
                    {

                        foreach (Match match in matches)
                        {
                            if(match.Success && match.Groups != null)
                            {
                                string link = match.Groups["link"].Value;
                                string linkDirSep = match.Groups["linkDirSep"].Value;
                                string linkText = match.Groups["linkText"].Value;
                                string linkTextDirSep = match.Groups["linkTextDirSep"].Value;

                                if(link + linkDirSep != linkText + linkTextDirSep)
                                {
                                    Console.WriteLine("strange... href does not match text");
                                }

                                string absPath = currentUrl.url + link + linkDirSep;
                                string localPath = currentUrl.localPath + cleanupPath(link) + linkDirSep;

                                if (linkDirSep != "")
                                {
                                    // It's a folder
                                    if(link != ".." && link != ".") // We only go deeper, not up.
                                    {
                                        File.AppendAllText(shFile, "mkdir -p '"+localPath+"'" + "\n");
                                        Console.WriteLine("Enqueued: "+localPath+","+absPath);
                                        urlsToScrape.Enqueue(new PathPair() { url= absPath ,localPath=localPath});
                                    }
                                } else
                                {
                                    // It's a file.
                                    if (link != ".." && link != ".") // Shouldn't be necessary bc the directories add "/" behind folders, but just to be safe...
                                    {

                                        long? fileSize = null;

                                        int? statusCode = null;
                                        if (getFileSizes)
                                        {
                                            PatientRequester.Response headResponse = await PatientRequester.head(absPath);

                                            statusCode = headResponse.statusCode;
                                            if (headResponse.success == true)
                                            {
                                                fileSize = headResponse.headerContentLength;
                                            }
                                            else
                                            {
                                                File.AppendAllText(shFile, "# Error " + headResponse.statusCode + " during HEAD to \"" + absPath + "\", possibly can't be downloaded" + "\n");
                                            }

                                        } else
                                        {
                                            fileSize = -1;
                                            statusCode = -1;
                                        }
                                        string line =  absPath+";" + fileSize + ";" + localPath + ";"+currentUrl.localPath+";"+ statusCode;
                                        File.AppendAllText(csvFile, line + "\n");
                                        File.AppendAllText(shFile, "wget -c --retry-connrefused --tries=0 --timeout=500 -O '" + localPath + "' '" + absPath + "'"+" # Filesize: "+fileSize + "\n");
                                        Console.WriteLine("File found: "+line);
                                        //foundFiles.Add(absPath,fileSize);
                                    }
                                }
                            }
                        }
                    }


                    urlsToScrape.Dequeue();
                }
                else
                {
                    // Skip this item if error code is 403 or 404 (aka if no matter how often we retry, we won't get a proper response)
                    if(response.statusCode == 403 || response.statusCode == 404)
                    {
                        File.AppendAllText(shFile, @"# Failed to scrape "+currentUrl.url+", error "+response.statusCode + "\n");
                        urlsToScrape.Dequeue();
                    }
                }

                ((Action)(() => { }))(); // Just so i have a breakpoint.
            }

            File.AppendAllText(shFile, @"read -n1 -r" + "\n");
        }

        public static string GetUnusedFilename(string baseFilename)
        {
            if (!File.Exists(baseFilename))
            {
                return baseFilename;
            }
            string extension = Path.GetExtension(baseFilename);

            int index = 1;
            while (File.Exists(Path.ChangeExtension(baseFilename, "." + (++index) + extension))) ;

            return Path.ChangeExtension(baseFilename, "." + (index) + extension);
        }

        // from: https://stackoverflow.com/a/847251
        private static string MakeValidFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }
    }
}
