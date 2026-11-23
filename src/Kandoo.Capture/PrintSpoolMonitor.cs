using System.Security.Cryptography;

namespace Kandoo.Capture;

/// <summary>Polling/raw-copy portion of the probe, with baseline suppression and stable-file detection.</summary>
public sealed class PrintSpoolMonitor
{
    private readonly SpoolFileTracker _tracker;
    private readonly PrintCaptureOptions _options;

    public PrintSpoolMonitor(PrintCaptureOptions options, ICapturedSpoolSink sink, TimeProvider? timeProvider = null)
    {
        _options = options;
        _options.Validate();
        _tracker = new SpoolFileTracker(options, sink, timeProvider);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _tracker.IgnoreExisting();
        while (!cancellationToken.IsCancellationRequested)
        {
            await _tracker.ScanOnceAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(_options.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class SpoolFileTracker
{
    private readonly PrintCaptureOptions _options;
    private readonly ICapturedSpoolSink _sink;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, SeenFile> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public SpoolFileTracker(PrintCaptureOptions options, ICapturedSpoolSink sink, TimeProvider? timeProvider = null)
    {
        _options = options;
        _options.Validate();
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _time = timeProvider ?? TimeProvider.System;
    }

    public void IgnoreExisting()
    {
        _seen.Clear();
        foreach (var path in SpoolFiles())
        {
            var info = new FileInfo(path);
            _seen[path] = new SeenFile(info.Length, info.LastWriteTimeUtc.Ticks, _options.StableScans, true);
        }
    }

    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = SpoolFiles().ToArray();
            foreach (var path in current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists || info.Length == 0 || info.Length > _options.MaxCaptureBytes)
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }

                if (!_seen.TryGetValue(path, out var seen))
                {
                    _seen[path] = new SeenFile(info.Length, info.LastWriteTimeUtc.Ticks, 1, false);
                    continue;
                }

                if (seen.Length != info.Length || seen.LastWriteTicks != info.LastWriteTimeUtc.Ticks)
                {
                    seen.Length = info.Length;
                    seen.LastWriteTicks = info.LastWriteTimeUtc.Ticks;
                    seen.StableCount = 1;
                    seen.Captured = false;
                    continue;
                }

                if (seen.Captured)
                {
                    continue;
                }

                seen.StableCount++;
                if (seen.StableCount < _options.StableScans)
                {
                    continue;
                }

                try
                {
                    var artifact = await CaptureAsync(path, cancellationToken).ConfigureAwait(false);
                    var disposition = await _sink.AcceptAsync(artifact, cancellationToken).ConfigureAwait(false);
                    if (disposition != CaptureDisposition.Review)
                    {
                        throw new InvalidOperationException("CAPTURE_NOT_REVIEWED");
                    }

                    seen.Captured = true;
                }
                catch (InvalidDataException)
                {
                    // The spooler may still be writing/deleting the source. It
                    // remains uncaptured and is retried after a later stable scan.
                    seen.StableCount = 1;
                }
                catch (IOException)
                {
                    seen.StableCount = 1;
                }
            }

            var currentSet = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in _seen.Keys.Where(path => !currentSet.Contains(path)).ToArray())
            {
                _seen.Remove(missing);
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<CapturedSpoolArtifact> CaptureAsync(string sourcePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.CaptureDirectory);
        using var memory = new MemoryStream();
        using (var source = new FileStream(
                   sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        }

        if (memory.Length == 0 || memory.Length > _options.MaxCaptureBytes)
        {
            throw new InvalidDataException("The spool capture is empty or exceeds the configured limit.");
        }

        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var destination = Path.Combine(_options.CaptureDirectory, hash + extension);
        if (!File.Exists(destination))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return new CapturedSpoolArtifact(
            sourcePath, destination, extension, bytes.LongLength, hash, _time.GetUtcNow());
    }

    private IEnumerable<string> SpoolFiles()
    {
        if (!Directory.Exists(_options.SpoolDirectory))
        {
            throw new DirectoryNotFoundException($"Spooler folder not found: {_options.SpoolDirectory}");
        }

        return Directory.EnumerateFiles(_options.SpoolDirectory)
            .Where(path => Path.GetExtension(path).Equals(".SPL", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".SHD", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SeenFile(long length, long lastWriteTicks, int stableCount, bool captured)
    {
        public long Length { get; set; } = length;
        public long LastWriteTicks { get; set; } = lastWriteTicks;
        public int StableCount { get; set; } = stableCount;
        public bool Captured { get; set; } = captured;
    }
}
