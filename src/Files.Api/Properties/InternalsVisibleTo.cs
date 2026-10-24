using System.Runtime.CompilerServices;

// The generated `Program` class from top-level statements is internal; make it visible to the
// test projects so they can boot the real host via WebApplicationFactory<Program>.
[assembly: InternalsVisibleTo("Files.IntegrationTests")]
[assembly: InternalsVisibleTo("Files.ContractTests")]
