using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Entry point marker so <see cref="BenchmarkSwitcher"/> can locate this assembly.</summary>
public partial class Program;
