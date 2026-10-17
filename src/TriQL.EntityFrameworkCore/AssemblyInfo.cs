using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Design;

[assembly: InternalsVisibleTo("TriQL.EntityFrameworkCore.Tests")]
[assembly: InternalsVisibleTo("TriQL.EntityFrameworkCore.FunctionalTests")]

// How the EF Core tools (dotnet ef dbcontext scaffold) find the provider's design-time services.
[assembly: DesignTimeProviderServices("TriQL.EntityFrameworkCore.Design.Internal.TrinoDesignTimeServices")]
