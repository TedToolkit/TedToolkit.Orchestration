using BenchmarkDotNet.Running;

using TedToolkit.Orchestration.StateMachine.Benchmarks;

if (args.Contains("--verify", StringComparer.Ordinal))
{
    AdapterVerification.Run();
    Console.WriteLine("All benchmark adapters produced equivalent results.");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
