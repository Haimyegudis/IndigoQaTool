namespace CDB;

public class DumpFileInfo
{
    /// <summary>
    /// The CDB instance ID. 
    /// </summary>
    public int CdbInstanceId { get; set; }

    /// <summary>
    /// The path to the dump file.
    /// </summary>
    public string DumpFilePath { get; set; } = null!;

    /// <summary>
    /// The time that the dump file was created, as taken using the '.time' CDB command.
    /// </summary>
    public string DumpFileTime { get; set; } = null!;

    /// <summary>
    /// The command line that was used to start the process that the dump file was created from, as taken using the '!cl' CDB command.
    /// </summary>
    public string DumpFileProcessCommandLine { get; set; } = null!;

    /// <summary>
    /// The Computer Name of the machine that the dump file was created on, as taken using the '!cn' CDB command.
    /// </summary>
    public string ComputerName { get; set; } = null!;

    /// <summary>
    /// The output of the '.exr -1' CDB command for the dump file.
    /// </summary>
    public string Exr { get; set; } = null!;
}

public class GetMethodTableAddressResponse
{
    public string Type { get; set; } = null!;
    public bool Success { get; set; }
    public string? MethodTableAddress { get; set; }
}