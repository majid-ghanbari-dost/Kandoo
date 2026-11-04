namespace Kandoo.HolooDiscovery;

public sealed record InstalledProgram(string Name, string InstallFolder, string Version);

public sealed record CandidateDatabase(
    string Path,
    long SizeBytes,
    DateTimeOffset LastWriteUtc);

public sealed record DiscoveryResult(
    string Mode,
    IReadOnlyList<InstalledProgram> Programs,
    IReadOnlyList<string> InstallFolders,
    IReadOnlyList<CandidateDatabase> CandidateDatabases,
    IReadOnlyList<string> SqlInstanceNames);

public sealed record BackupInspection(
    string Path,
    long SizeBytes,
    string MagicHex,
    string CompressionGuess,
    string EncryptionGuess);
