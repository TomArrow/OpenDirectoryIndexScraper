using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OpenDirectoryIndexScraper
{
    class Program
    {

        static char[] badChars = (new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "!").ToCharArray(); // Linux doesn't like !

        public static string cleanupPath(string path, char replacement = '_')
        {
            return string.Join(replacement, path.Split(badChars));
        }

        static bool getFileSizes = false;

        static void Main(string[] args)
        {
            bool isMonitor = args.Length > 1 && args[0] == "-monitor";
            int monitorRecrawlDelay = 60000;
            if (isMonitor) monitorRecrawlDelay = int.Parse(args[1]);

            List<Task> tasks = new List<Task>();

            int index = 0;
            foreach(string arg in args)
            {
                if (isMonitor && index++ > 1)
                {
                    tasks.Add(monitor(arg, monitorRecrawlDelay));
                } else if(!isMonitor)
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

       
        private struct PathPair
        {
            public string url;
            public string localPath;
        }

        static bool executionEndRequested = false;
        
        static Mutex consoleReadMutex = new Mutex();
        static bool ShouldEnd()
        {
            lock (consoleReadMutex)
            {
                while (Console.KeyAvailable)
                {
                    if (Console.ReadKey(false).Key == ConsoleKey.Escape)
                    {
                        executionEndRequested = true;
                        return true;
                    }
                }
                if (executionEndRequested) return true;
                return false;
            }
        }


        struct FoundFile
        {
            public string localPath;
            public string absPath;
            public DateTime parsedDateTime;
        }

        static async Task monitor(string baseURL,int reCrawlDelay)
        {

            //string csvFile = GetUnusedFilename(MakeValidFileName(baseURL)+".csv");
            //string shFile = GetUnusedFilename(MakeValidFileName(baseURL)+".sh");

            //File.AppendAllText(shFile,@"#!/bin/bash"+"\n");
            //File.AppendAllText(csvFile, @"URL;Size;LocalPath;LocalFolder;StatusCode"+"\n");

            Dictionary<string, DateTime> urlTimes = new Dictionary<string, DateTime>();

            bool serverGivesDateModified = true;
            const int dateTimeModifiedOffsetsRequired = 5;
            List<Int64> dateModifiedOffsets = new List<Int64>();
            TimeSpan? dateModifiedDeterminedOffset = null;

            List<FoundFile> foundFiles = new List<FoundFile>();

            bool alreadyRun = false;
            while (true) {

                foundFiles.Clear();
                Console.WriteLine("Press ESC to stop monitoring. (Will exit at next crawl interval)");
                if (alreadyRun) System.Threading.Thread.Sleep(reCrawlDelay);
                if (ShouldEnd())
                {
                    return;
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

                while(urlsToScrape.Count > 0)
                {
                    PathPair currentUrl = urlsToScrape.Peek();
                    PatientRequester.Response response = await PatientRequester.get(currentUrl.url);
                    if(response.success == true)
                    {
                        MatchCollection matches = linkGetterDate.Matches(response.responseAsString);

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

                                    if(link + linkDirSep != linkText + linkTextDirSep)
                                    {
                                        Console.WriteLine("strange... href does not match text");
                                    }

                                    string absPath = currentUrl.url + link + linkDirSep;
                                    string localPath = currentUrl.localPath + cleanupPath(HttpUtility.UrlDecode(link)) + linkDirSep;

                                    if (linkDirSep != "")
                                    {
                                        // It's a folder
                                        if(link != ".." && link != ".") // We only go deeper, not up.
                                        {
                                            //File.AppendAllText(shFile, "mkdir -p '"+localPath+"'" + "\n");
                                            if (urlTimes.ContainsKey(absPath))
                                            {
                                                if(urlTimes[absPath] == parsedDateTime)
                                                {
                                                    Console.WriteLine("Directory date time seemingly not changed, skipping: " + localPath + "," + absPath);
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
                                            urlTimes[absPath] = parsedDateTime;
                                        }
                                    } else
                                    {
                                        // It's a file.
                                        if (link != ".." && link != ".") // Shouldn't be necessary bc the directories add "/" behind folders, but just to be safe...
                                        {

                                            long? fileSize = null;

                                            int? statusCode = null;
                                            if (/*getFileSizes*/serverGivesDateModified && dateModifiedDeterminedOffset == null && dateModifiedOffsets.Count < dateTimeModifiedOffsetsRequired)
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
                                                        serverGivesDateModified = false;
                                                        Console.WriteLine("Server does not send Date-Modified.: " + localPath + "," + absPath);
                                                    } else
                                                    {
                                                        Int64 dateModifiedOffset = (Int64)(Math.Floor((headResponse.dateTime.Value - parsedDateTime).TotalSeconds/60)+0.5f);
                                                        dateModifiedOffsets.Add(dateModifiedOffset);
                                                        Console.WriteLine($"Date modified offset {dateModifiedOffsets.Count}/{dateTimeModifiedOffsetsRequired} logged: {headResponse.dateTime.Value}-{parsedDateTime}={dateModifiedOffset}  :"+ localPath + "," + absPath);
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
                                            Console.WriteLine("File found: "+line);
                                            foundFiles.Add(new FoundFile() { absPath=absPath,localPath=localPath,parsedDateTime=parsedDateTime });
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
                    }

                    ((Action)(() => { }))(); // Just so i have a breakpoint.
                }


                // Do we have proper offset info?
                // Calculate it if we can.
                if(!dateModifiedDeterminedOffset.HasValue && dateModifiedOffsets.Count >= dateTimeModifiedOffsetsRequired)
                {
                    Console.WriteLine("Trying to calculate date modified offset median.");
                    // Get median
                    dateModifiedOffsets.Sort();
                    Int64 median = dateModifiedOffsets[dateModifiedOffsets.Count/2]; // For example count is 5. divide by 2 = 2.5 (but since integer, it's 2). And 2 is perfect middle index.
                    // Now check if the median is 
                    Console.WriteLine($"Median is {median}. Now checking consistency.");

                    int countConsistent = 0;
                    foreach(Int64 doffset in dateModifiedOffsets)
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
                        Console.WriteLine($"Median is consistent with {countConsistent} of {dateModifiedOffsets.Count} measured offsets. Ok!");
                        dateModifiedDeterminedOffset = new TimeSpan(0, (int)median,0);
                    } else
                    {
                        Console.WriteLine($"Median is consistent with {countConsistent} of {dateModifiedOffsets.Count} measured offsets. Not ok, purging offsets.");
                        dateModifiedOffsets.Clear();
                    }

                } else if (!serverGivesDateModified)
                {
                    Console.WriteLine("Server does not send Date-Modified header. Setting offset to 0.");
                    dateModifiedDeterminedOffset = new TimeSpan(0);
                }

                // Ok do actual processing of files maybe.
                if(dateModifiedDeterminedOffset.HasValue)
                {
                    Console.WriteLine($"Processing found files...");
                    int countAlreadyExisting = 0;
                    foreach(FoundFile foundFile in foundFiles)
                    {
                        DateTime fixedLocalDateTime = foundFile.parsedDateTime + dateModifiedDeterminedOffset.Value;
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
                                File.SetLastWriteTime(fullLocalPath,fixedLocalDateTime);
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
                    urlTimes.Clear(); // Otherwise it won't redownload. Bad.
                }


                alreadyRun = true;

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
