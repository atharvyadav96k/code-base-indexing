namespace CodeIndexer.Storage;

/// <summary>Constants describing the on-disk binary index format.</summary>
public static class BinaryIndexFormat
{
    /// <summary>Magic bytes identifying a valid index file, written first.</summary>
    public const string MagicHeader = "CIDX";

    /// <summary>
    /// Format version. Bump this whenever the on-disk schema changes; a reader
    /// seeing a mismatched version must rebuild rather than attempt to misread.
    /// </summary>
    public const int CurrentVersion = 1;
}
