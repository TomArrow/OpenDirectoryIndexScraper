using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenDirectoryIndexScraper
{
    class MonitorState
    {
        public Dictionary<string, DateTime> urlTimes { get; set; } = new Dictionary<string, DateTime>();
        public List<string> directoriesWithSubFolders { get; set; } = new List<string>();

        public bool serverGivesDateModified { get; set; } = true;

        public List<Int64> dateModifiedOffsets { get; set; } = new List<Int64>();
        //public TimeSpan? dateModifiedDeterminedOffset { get; set; } = null;
        public Int64? dateModifiedDeterminedOffset { get; set; } = null;

        public List<FoundFile> foundFiles { get; set; } = new List<FoundFile>();

        public bool alreadyRun { get; set; } = false;
    }
}
