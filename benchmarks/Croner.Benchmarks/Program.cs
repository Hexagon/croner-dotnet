using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/Croner.Benchmarks -- --filter '*'
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
