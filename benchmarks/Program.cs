using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Snail.MCP.FileSystem.Benchmarks.CallBenchmarks).Assembly).Run(args);
