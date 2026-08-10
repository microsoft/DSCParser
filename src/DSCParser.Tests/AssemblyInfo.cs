using Xunit;

// DSCParser mutates PowerShell's process-wide DSC state (the engine's internal DscClassCache and
// the DSCParser keyword/import registries are statics shared by every test). Running collections
// in parallel makes tests that assert on that shared state order-dependent and flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]