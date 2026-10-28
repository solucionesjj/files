using System.Runtime.CompilerServices;

// Expose the internal ClamAV protocol/scanner seams to the test projects so the INSTREAM framing
// and verdict parsing can be unit-tested against a fake TCP server or in-memory stream.
[assembly: InternalsVisibleTo("Files.UnitTests")]
[assembly: InternalsVisibleTo("Files.IntegrationTests")]
