using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            List<Task> tasks = new List<Task>();

            foreach(string arg in args)
            {
                tasks.Add(scrape(arg));
            }
#if DEBUG
            //scrape("https://caad.ifreviews.org/");
            //scrape("https://pdsimage2.wr.usgs.gov/archive/LO_1001/");
            //scrape("https://www.crawl-forever.com/downloads/");
            //scrape("http://89.179.240.237/");
            tasks.Add(scrape("http://74.91.123.99/races/"));

#endif
            Task.WaitAll(tasks.ToArray());
            Console.WriteLine("\n\n\n\nDone! Press any key to exit...");
            Console.ReadKey();
        }

        //private static Regex linkGetter = new Regex(@"<a\s+href=""(?<link>[^/\?""]+)(?<linkDirSep>/?)"">(?<linkText>[^/\?""<]+)(?<linkTextDirSep>/?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex linkGetter = new Regex(@"<a\s+href=""(?<link>[^/\?""]+)(?<linkDirSep>/?)"">\s*(<img[^>]*?>)?\s*(?<linkText>[^/\?""<]+)(?<linkTextDirSep>/?)\s*</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

       
        private struct PathPair
        {
            public string url;
            public string localPath;
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
