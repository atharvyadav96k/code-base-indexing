namespace CodeIndexer.Storage;

public enum IndexReadStatus
{
    Success,
    NotFound,
    VersionMismatch,
    Corrupted,
}
