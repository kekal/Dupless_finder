using System;

namespace Dupples_finder_UI.Data.Entities
{
    public class Photo
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public byte[] SiftDescriptors { get; set; }
        public int DescriptorRows { get; set; }
        public int DescriptorCols { get; set; }
        public long FileSize { get; set; }
        public DateTime HashDate { get; set; }

        /// <summary>
        /// The file's last-modified timestamp (UTC), used together with FileSize
        /// to form a lightweight identity fingerprint for cache lookups.
        /// </summary>
        public DateTime LastModifiedUtc { get; set; }

        /// <summary>
        /// JPEG-compressed thumbnail data for display in the UI.
        /// Null when the thumbnail has not yet been generated.
        /// </summary>
        public byte[] Thumbnail { get; set; }

        /// <summary>
        /// Computed fingerprint that combines FileSize and LastModifiedUtc.
        /// Two files with the same size and modification time are assumed
        /// to be the same image content, regardless of path.
        /// Not mapped to the database — the composite index on
        /// (FileSize, LastModifiedUtc) serves the same purpose.
        /// </summary>
        public string Fingerprint => $"{FileSize}_{LastModifiedUtc.Ticks}";
    }
}
