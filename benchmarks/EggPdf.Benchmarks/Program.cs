using BenchmarkDotNet.Running;
using EggPdf.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RenderBenchmarks).Assembly).Run(args);
