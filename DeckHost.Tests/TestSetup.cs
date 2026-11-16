// NUnit settings for the whole test assembly.
//
// One test class instance per test, as the tests are written: constructors set up fresh state and
// Dispose cleans up after each test (NUnit calls Dispose per test with this life cycle).
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

// Test classes run in parallel (as they did under xUnit); the tests in one class run one at a time.
[assembly: Parallelizable(ParallelScope.Fixtures)]
