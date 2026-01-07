using System.ComponentModel;
using CDB;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Tools.ExternalDevServices.AI.MCP.CdbMcp;

[McpServerToolType]
public static class CdbTool
{
    private const string CdbPath = @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe"; //@"C:\Install\GFlags\x64\x64\cdb.exe";

    private static readonly Dictionary<int, CdbInstance> CdbInstances = new();

    [McpServerTool, Description("""
                                Loads a dump file into a new CDB instance and returns the dump file information.
                                The CDB instance is kept alive until explicitly unloaded.
                                The CDB instance can be accessed using the returned ID. 
                                """)]
    public static async Task<DumpFileInfo> LoadDumpFileAsync(string dumpFilePath, CancellationToken ct = default)
    {
        try
        {
            var cdbInstance = new CdbInstance();
            var dumpFileInfo = await cdbInstance.LoadDumpFileAsync(CdbPath, dumpFilePath, ct);
            CdbInstances.Add(cdbInstance.Id!.Value, cdbInstance);
            return dumpFileInfo;
        }
        catch (Exception ex)
        {
            throw new McpException($"{ex.Message}\r\n{ex.StackTrace}");
        }
    }

    [McpServerTool, Description("Unloads an active CDB instance based on the provided ID")]
    public static async Task UnloadCdbInstanceAsync(int id, CancellationToken ct = default)
    {
        try
        {
            if (!CdbInstances.TryGetValue(id, out var cdbInstance))
                throw new McpException($"CDB instance with ID {id} not found.");

            await cdbInstance.UnloadAsync(ct);
        }
        catch (Exception ex)
        {
            throw new McpException($"{ex.Message}\r\n{ex.StackTrace}");
        }
    }

    [McpServerTool, Description("Sends a CDB command to an active CDB instance based on the provided ID")]
    public static async Task<string> SendCommandAsync(int id, string command, CancellationToken ct = default)
    {
        try
        {
            if (!CdbInstances.TryGetValue(id, out var cdbInstance))
                throw new McpException($"CDB instance with ID {id} not found.");
            return await cdbInstance.SendCommandAsync(command, ct);
        }
        catch (Exception ex)
        {
            throw new McpException($"{ex.Message}\r\n{ex.StackTrace}");
        }
    }
}