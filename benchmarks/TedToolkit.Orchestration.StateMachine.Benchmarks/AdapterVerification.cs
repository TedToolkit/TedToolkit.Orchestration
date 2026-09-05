namespace TedToolkit.Orchestration.StateMachine.Benchmarks;

internal static class AdapterVerification
{
    internal static void Run()
    {
        VerifyBasic();
        VerifyGuardAndLifecycle();
        VerifyCanFire();
    }

    private static void VerifyBasic()
    {
        var handwritten = new HandwrittenMachine(BenchmarkState.A);
        var ted = new TedBasicMachine(BenchmarkState.A);
        var stateless = new StatelessBasicMachine();
        var appccelerate = AppccelerateMachines.CreateBasic();
        var appccelerateTransitions = 0;
        appccelerate.TransitionCompleted += (_, _) => appccelerateTransitions++;

        var handwrittenState = handwritten.Toggle();
        var tedState = ted.Toggle();
        var statelessState = stateless.Toggle();
        appccelerate.Fire(BenchmarkTrigger.Toggle);

        Require(handwrittenState == BenchmarkState.B, "handwritten basic state");
        Require(tedState == BenchmarkState.B, "TedToolkit basic state");
        Require(statelessState == BenchmarkState.B, "Stateless basic state");
        Require(appccelerateTransitions == 1, "Appccelerate basic transition");
    }

    private static void VerifyGuardAndLifecycle()
    {
        var handwritten = new HandwrittenMachine(BenchmarkState.A);
        var ted = new TedGuardedMachine(BenchmarkState.A);
        var stateless = new StatelessGuardedMachine();
        var appccelerate = AppccelerateMachines.CreateGuarded();

        handwritten.GuardedToggle();
        ted.Toggle();
        stateless.Toggle();
        appccelerate.Toggle();

        Require(handwritten.State == BenchmarkState.B && handwritten.ActionCount == 2, "handwritten guarded toggle");
        Require(ted.State == BenchmarkState.B && ted.ActionCount == 2, "TedToolkit guarded toggle");
        Require(stateless.Machine.State == BenchmarkState.B && stateless.ActionCount == 2, "Stateless guarded toggle");
        Require(appccelerate.ActionCount == 2, "Appccelerate guarded toggle");
    }

    private static void VerifyCanFire()
    {
        var handwritten = new HandwrittenMachine(BenchmarkState.A);
        var ted = new TedBasicMachine(BenchmarkState.A);
        var stateless = new StatelessBasicMachine();

        Require(handwritten.CanToggle(), "handwritten CanFire");
        Require(ted.CanToggleAsync().GetAwaiter().GetResult(), "TedToolkit CanFire");
        Require(stateless.Machine.CanFire(BenchmarkTrigger.Toggle), "Stateless CanFire");
    }

    private static void Require(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException($"Adapter verification failed: {scenario}.");
    }
}
