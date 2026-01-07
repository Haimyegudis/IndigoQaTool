using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CDB;

public partial class CdbInstance
{
    private static partial class Regexes
    {
        [GeneratedRegex(@"[a-fA-F0-9]+:[a-fA-F0-9]+> ", RegexOptions.None)]
        public static partial Regex OutputLineStartRegex();

        /*[GeneratedRegex("(?<Address>[a-fA-F0-9]+)", RegexOptions.None)]
        public static partial Regex InstanceAddressRegex();

        [GeneratedRegex(@"String:\s*(?<StringValue>.*?)\s*\n(?=Fields:)", RegexOptions.None)]
        public static partial Regex StringValueRegex();

        [GeneratedRegex(@"(?<MT>[a-fA-F0-9]+)\s+(?<Count>\d+)\s+(?<TotalSize>\d+)\s+(?<ClassName>.*)", RegexOptions.None)]
        public static partial Regex DumpHeapTypeStatsRegex();

        [GeneratedRegex(@"(?<Address>[a-fA-F0-9]+)\s+(?<MT>[a-fA-F0-9]+)\s+(?<Size>\d+)\s*", RegexOptions.None)]
        public static partial Regex MTOutputRegex();

        [GeneratedRegex(@"(?<MT>[a-fA-F0-9]+)\s+(?<Field>[a-fA-F0-9]+)\s+(?<Offset>[a-fA-F0-9]+)\s+(?<Type>.+)\s+(?<VT>\d+)\s+(?<ATTR>.+)\s+(?<Value>[a-fA-F0-9]+)\s+(?<Name>.*)\s*", RegexOptions.None)]
        public static partial Regex DumpObjectFieldRegex();*/
    }
    
    private const string CommandStarted = "#COMMAND_START#";
    private const string CommandFinished = "#COMMAND_END#";

    private readonly StringBuilder _output = new();
    private readonly object _lock = new();

    private DumpFileInfo? _dumpInfo;
    private CdbCache? _cdbCache;
    private readonly Dictionary<string, GetMethodTableAddressResponse> _methodTableCache = new();
    private Process? _cdbProcess;

    public int? Id => _cdbProcess is { HasExited: false } ? _cdbProcess.Id : null;

    public async Task<DumpFileInfo> LoadDumpFileAsync(string cdbFilePath, string dumpFilePath, CancellationToken ct = default)
    {
        if(_cdbProcess is not null)
            throw new InvalidOperationException($"Another dump file is already loaded in this instance (Id: {Id})");

        dumpFilePath = dumpFilePath.Replace("\"", "");
        if(!File.Exists(dumpFilePath))
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}");
        
        /*var mexDebuggerExtensionPath = GetDebuggerExtensionPath("mex");
        if(!File.Exists(mexDebuggerExtensionPath))
            throw new FileNotFoundException($"Debugger extension 'mex' not found: {mexDebuggerExtensionPath}");*/

        _cdbCache = new CdbCache(dumpFilePath);
        _methodTableCache.Clear();
        _dumpInfo = null;

        _cdbProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cdbFilePath,
                Arguments = $"-z \"{dumpFilePath}\" -c \".loadby sos coreclr\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Minimized
            }
        };
        _cdbProcess.Start();

        // Hook up the output and error streams
        _cdbProcess.OutputDataReceived += (_, eventArgs) =>
        {
            switch (eventArgs.Data)
            {
                case null:
                case not null when string.IsNullOrWhiteSpace(eventArgs.Data):
                    return;
                case not null when eventArgs.Data.Contains(CommandStarted):
                    _output.Clear();
                    return;
                case not null when eventArgs.Data.Contains(CommandFinished):
                    lock (_lock)
                    {
                        Monitor.Pulse(_lock);
                    }
                    return;
                default:
                    _output.AppendLine(eventArgs.Data.Trim());
                    return;
            }
        };
        _cdbProcess.BeginOutputReadLine();

        _cdbProcess.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null)
            {
                Console.WriteLine($"ERROR: {eventArgs.Data}");
            }
        };
        _cdbProcess.BeginErrorReadLine();

        _dumpInfo = await GetDumpInfoAsync();
        return _dumpInfo;
    }

    public async Task UnloadAsync(CancellationToken ct = default)
    {
        if (_cdbProcess is null) return;

        try
        {
            await WriteLineToCdbProcessAsync("q");
            await _cdbProcess.WaitForExitAsync(ct);
        }
        finally
        {
            _cdbProcess = null;
            _cdbCache = null;
            _methodTableCache.Clear();
        }
    }

    public async Task<string> SendCommandAsync(string dbgCommand, CancellationToken ct = default)
    {
        if (dbgCommand.StartsWith("#mt", StringComparison.OrdinalIgnoreCase))
        {
            var parts = dbgCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                Console.WriteLine("Invalid command format. Expected: #mt <type>");
                return string.Empty;
            }
            var mt = await GetMethodTableAddressAsync(parts[1]);
            return mt.Success ? mt.MethodTableAddress! : string.Empty;
        }

        if (dbgCommand.Contains(".cls", StringComparison.OrdinalIgnoreCase))
        {
            Console.Clear();
            if (dbgCommand.Equals(".cls", StringComparison.OrdinalIgnoreCase) || dbgCommand.Equals(".cls;", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            dbgCommand = dbgCommand
                .Replace(".cls", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(".cls;", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        if (_cdbCache!.TryGetCommandOutput(dbgCommand.ToLower().Trim(), out var output)) return output;

        await WriteLineToCdbProcessAsync($".echo {CommandStarted}");
        await WriteLineToCdbProcessAsync($"{dbgCommand}");
        await WriteLineToCdbProcessAsync($".echo {CommandFinished}");
        lock (_lock)
        {
            Monitor.Wait(_lock);
        }
        output = _output.ToString().Trim();
        await _cdbCache.SetCommandOutputAsync(dbgCommand.ToLower().Trim(), output);
        return output;
    }

    public async Task<DumpFileInfo> GetDumpInfoAsync()
    {
        if (_cdbProcess is null)
            throw new InvalidOperationException("CDB process is not running");

        if(_dumpInfo is not null) return _dumpInfo;

        var commandLine = await SendCommandAsync("!cl");
        var computerName = await SendCommandAsync("!cn");
        var dumpTime = await SendCommandAsync(".time");
        var exr = await SendCommandAsync(".exr -1");

        _dumpInfo = new DumpFileInfo
        {
            CdbInstanceId = Id!.Value,
            DumpFilePath = _cdbCache!.CacheFilePath,
            DumpFileTime = Regexes.OutputLineStartRegex().Replace(dumpTime, ""),
            DumpFileProcessCommandLine = Regexes.OutputLineStartRegex().Replace(commandLine, ""),
            ComputerName = Regexes.OutputLineStartRegex().Replace(computerName, ""),
            Exr = Regexes.OutputLineStartRegex().Replace(exr, "")
        };

        return _dumpInfo;
    }

    public async Task<GetMethodTableAddressResponse> GetMethodTableAddressAsync(string type)
    {
        if (_methodTableCache.TryGetValue(type, out var mtr)) return mtr;

        var name2EEOutput = await SendCommandAsync($"!name2ee *!{type}");
        var mt = name2EEOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("MethodTable:"))?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrEmpty(mt)) return new GetMethodTableAddressResponse { Type = type, Success = false };

        mtr = new GetMethodTableAddressResponse { Type = type, Success = true, MethodTableAddress = mt };
        _methodTableCache[type] = mtr;
        return mtr;
    }

    private async Task WriteLineToCdbProcessAsync(string line)
    {
        if (_cdbProcess is null)
            throw new InvalidOperationException("CDB process is not running");

        await _cdbProcess.StandardInput.WriteLineAsync(line);
    }

    private static string GetDebuggerExtensionPath(string extensionFileName) =>
        Path.Combine(Environment.CurrentDirectory, "DebuggerExtensions",
            $"{extensionFileName}.dll");
}