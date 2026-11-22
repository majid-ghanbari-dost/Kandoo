using System.Diagnostics;

namespace Kandoo.Hub;

/// <summary>Raw-byte process transport for the frozen extractor CLI boundary.</summary>
public sealed class ExtractorProcessRunner : IExtractorProcessRunner
{
    public async Task<ExtractorProcessRun> RunAsync(
        ExtractorProcessInvocation invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (timeout <= TimeSpan.Zero || timeout > ExtractionBridgeContract.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = invocation.WorkingDirectory
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in invocation.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("EXTRACTOR_PROCESS_START_FAILED");
        }

        // Drain both pipes concurrently so a full stderr/stdout pipe cannot
        // deadlock the child while the caller is waiting for exit or timeout.
        var standardOutput = ReadBytes(process.StandardOutput.BaseStream);
        var standardError = ReadBytes(process.StandardError.BaseStream);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await KillAndDrain(process, standardOutput, standardError).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new ExtractorProcessRun(
                null,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false),
                TimedOut: true);
        }

        return new ExtractorProcessRun(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static async Task KillAndDrain(
        Process process,
        Task<byte[]> standardOutput,
        Task<byte[]> standardError)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
