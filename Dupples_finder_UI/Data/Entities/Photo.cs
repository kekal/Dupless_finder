using System;

namespace Dupples_finder_UI.Data.Entities;

public class Photo
{
    public int DescriptorCols { get; set; }
    public int DescriptorRows { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public string Fingerprint => $"{FileSize}_{LastModifiedUtc.Ticks}";
    public DateTime HashDate { get; set; }
    public int Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public byte[] SiftDescriptors { get; set; }
    public byte[] Thumbnail { get; set; }
}