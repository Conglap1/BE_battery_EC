using Xunit;

// Disable parallel test execution across classes to ensure isolation for tests using shared PostgreSQL state
[assembly: CollectionBehavior(DisableTestParallelization = true)]
