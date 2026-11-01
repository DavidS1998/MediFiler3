namespace MediFiler3.Domain.Files;

public sealed record FileEntry(string FullPath, string Name, long Size, DateTime LastModifiedUtc)
{
    public string Extension => System.IO.Path.GetExtension(Name);
}
