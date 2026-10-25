namespace RequestsManagement.Tests.Infrastructure;

/// <summary>
/// Declares the "Database" xUnit collection.
///
/// All test classes decorated with [Collection("Database")] share the single
/// ApiFactory instance registered here and are forced to run serially —
/// xUnit never parallelises within a collection.
///
/// Do NOT also declare IClassFixture&lt;ApiFactory&gt; on those classes;
/// ICollectionFixture already provides the shared instance via constructor
/// injection, and combining both would create a second factory.
/// </summary>
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<ApiFactory> { }
