using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OpenDirectoryIndexScraper
{
    class PatientRequester
    {
        private static readonly HttpClient client = new HttpClient();
        private const int RETRYCOUNT = 20;
        private const bool CANCELON404OR403 = true;

        public struct Response
        {
            public bool success;
            public byte[] rawData;
            public string responseAsString;
            public string headerContentType;
            //public string headerContentEncoding;
            public long? headerContentLength;
            public int? statusCode;
            public DateTime? dateTime;
            //public Dictionary<string, string> responseHeaders;
        }

        public async static Task<Response> post(string url, Dictionary<string, string> postData)
        {
            Response retVal = new Response();

            FormUrlEncodedContent content = new FormUrlEncodedContent(postData);

            bool success = false;
            int errorCount = 0;

            while (!success && errorCount < RETRYCOUNT)
            {
                try
                {

                    HttpResponseMessage response = await client.PostAsync(url, content);
                    retVal.statusCode = (int)response.StatusCode;
                    response.EnsureSuccessStatusCode();
                    retVal.rawData = await response.Content.ReadAsByteArrayAsync();
                    retVal.responseAsString = await response.Content.ReadAsStringAsync();
                    retVal.headerContentType = response.Content.Headers.ContentType != null ? response.Content.Headers.ContentType.ToString() : null;
                    retVal.headerContentLength = response.Content.Headers.ContentLength;
                    retVal.dateTime = response.Content.Headers.LastModified.HasValue ? response.Content.Headers.LastModified.Value.LocalDateTime : null;
                    success = true;
                }
                catch (Exception e)
                {
                    errorCount++;
                    if (CANCELON404OR403 && (retVal.statusCode == 403 || retVal.statusCode == 404))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(1000);
                }
            }

            retVal.success = success;


            return retVal;
        }

        public async static Task<Response> get(string url)
        {
            Response retVal = new Response();

            bool success = false;
            int errorCount = 0;

            while (!success && errorCount < RETRYCOUNT)
            {
                try
                {
                    
                    HttpResponseMessage response = await client.GetAsync(url);
                    retVal.statusCode = (int)response.StatusCode;
                    response.EnsureSuccessStatusCode();
                    retVal.rawData = await response.Content.ReadAsByteArrayAsync();
                    retVal.responseAsString = await response.Content.ReadAsStringAsync();
                    retVal.headerContentType = response.Content.Headers.ContentType != null ? response.Content.Headers.ContentType.ToString() : null; 
                    retVal.headerContentLength = response.Content.Headers.ContentLength;
                    retVal.dateTime = response.Content.Headers.LastModified.HasValue ? response.Content.Headers.LastModified.Value.LocalDateTime : null;
                    success = true;
                }
                catch (Exception e)
                {
                    errorCount++;
                    if (CANCELON404OR403 && (retVal.statusCode == 403 || retVal.statusCode == 404))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(1000);
                }
            }

            retVal.success = success;


            return retVal;
        }
        
        public async static Task<Response> head(string url)
        {
            Response retVal = new Response();

            bool success = false;
            int errorCount = 0;

            while (!success && errorCount < RETRYCOUNT)
            {
                try
                {

                    HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                    retVal.statusCode = (int)response.StatusCode;
                    response.EnsureSuccessStatusCode();
                    //retVal.rawData = await response.Content.ReadAsByteArrayAsync();
                    //retVal.responseAsString = await response.Content.ReadAsStringAsync();
                    retVal.headerContentType = response.Content.Headers.ContentType != null ? response.Content.Headers.ContentType.ToString() : null;
                    retVal.headerContentLength = response.Content.Headers.ContentLength;
                    retVal.dateTime = response.Content.Headers.LastModified.HasValue ? response.Content.Headers.LastModified.Value.LocalDateTime : null;
                    success = true;
                }
                catch (Exception e)
                {
                    errorCount++;
                    if(CANCELON404OR403 && (retVal.statusCode == 403 || retVal.statusCode == 404))
                    {
                        break;
                    }
                    System.Threading.Thread.Sleep(1000);
                }
            }

            retVal.success = success;


            return retVal;
        }

    }
}
