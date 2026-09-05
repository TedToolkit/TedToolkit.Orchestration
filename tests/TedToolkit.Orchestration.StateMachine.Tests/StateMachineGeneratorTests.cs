using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using TedToolkit.Orchestration.StateMachine;
using TedToolkit.Orchestration.StateMachine.Analyzer;

namespace TedToolkit.Orchestration.StateMachine.Tests;

public class StateMachineGeneratorTests
{
    private const string Imports = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using TedToolkit.Orchestration.StateMachine;

        """;

    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(StateMachine<>).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    private static CSharpParseOptions ParseOptions => new(LanguageVersion.Preview);

    [Test]
    public async Task GeneratesInitialStateConstructorAndMultiSourceTransition()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Unknown, Draft, Returned, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft, OrderState.Returned)]
                public partial ValueTask SubmitAsync();
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine(OrderState.Draft);
                    var initial = machine.State;
                    await machine.SubmitAsync();
                    var first = machine.State;
                    var restored = new OrderMachine(OrderState.Returned);
                    await restored.SubmitAsync();
                    return $"{initial}:{first}:{restored.State}:{machine is StateMachine<OrderState>}";
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Draft:Submitted:Submitted:True");
    }

    [Test]
    public async Task GeneratesParameterlessConstructorFromAttributedInitialState()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>(OrderState.Draft)]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine();
                    var initial = machine.State;
                    await machine.SubmitAsync();
                    return $"{initial}:{machine.State}";
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Draft:Submitted");
    }

    [Test]
    public async Task OmitsParameterlessConstructorWithoutAttributedInitialState()
    {
        var run = await Compile<Func<bool>>("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }

            public static class Scenario
            {
                public static bool Run() => typeof(OrderMachine).GetConstructor(Type.EmptyTypes) is null;
            }
            """);

        await Assert.That(run()).IsTrue();
    }

    [Test]
    public async Task ReportsInvalidAttributedInitialStateAndParameterlessCollision()
    {
        var invalidStateDiagnostics = Generate("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>((OrderState)42)]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }
            """).Diagnostics;
        var constructorDiagnostics = Generate("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>(OrderState.Draft)]
            public sealed partial class OrderMachine
            {
                public OrderMachine() : base(OrderState.Submitted) { }

                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }
            """).Diagnostics;

        await Assert.That(invalidStateDiagnostics.Single(diagnostic => diagnostic.Id == "TTSM001").GetMessage())
            .Contains("initial state");
        await Assert.That(constructorDiagnostics.Single(diagnostic => diagnostic.Id == "TTSM001").GetMessage())
            .Contains("parameterless constructor");
    }

    [Test]
    public async Task UsesConventionGuardAndGeneratesCanAndTryMethods()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                public OrderMachine(OrderState initialState, int items) : base(initialState) => Items = items;

                public int Items { get; set; }

                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                private bool CanSubmit() => Items > 0;
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine(OrderState.Draft, 0);
                    var before = await machine.CanSubmitAsync();
                    var rejected = await machine.TrySubmitAsync();
                    machine.Items = 1;
                    var permitted = await machine.CanSubmitAsync();
                    await machine.SubmitAsync();
                    return $"{before}:{rejected.Rejection}:{permitted}:{machine.State}";
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("False:GuardRejected:True:Submitted");
    }

    [Test]
    public async Task SelectsGuardedDestinationAndOtherwiseFallback()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Reviewing, Approved, Rejected, ManualReview }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Approved, OrderState.Reviewing, Guard = nameof(CanApprove))]
                [TransitionTo(OrderState.Rejected, OrderState.Reviewing, Guard = nameof(CanReject))]
                [TransitionOtherwiseTo(OrderState.ManualReview, OrderState.Reviewing)]
                public partial ValueTask ReviewAsync(int score);

                private Task<bool> CanApprove(int score) => Task.FromResult(score >= 80);
                private ValueTask<bool> CanReject(int score) => ValueTask.FromResult(score < 40);
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var approved = new OrderMachine(OrderState.Reviewing);
                    await approved.ReviewAsync(90);
                    var rejected = new OrderMachine(OrderState.Reviewing);
                    await rejected.ReviewAsync(10);
                    var manual = new OrderMachine(OrderState.Reviewing);
                    await manual.ReviewAsync(50);
                    return $"{approved.State}:{rejected.State}:{manual.State}";
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Approved:Rejected:ManualReview");
    }

    [Test]
    public async Task RoutesOneTriggerDifferentlyFromDifferentSourceStates()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Draft, Paid, Cancelled, Refunding }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Cancelled, OrderState.Draft)]
                [TransitionTo(OrderState.Refunding, OrderState.Paid)]
                public partial ValueTask CancelAsync(string reason);

            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var cancelled = new OrderMachine(OrderState.Draft);
                    await cancelled.CancelAsync("customer");
                    var refunding = new OrderMachine(OrderState.Paid);
                    await refunding.CancelAsync("fraud");
                    return $"{cancelled.State}:{refunding.State}";
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Cancelled:Refunding");
    }

    [Test]
    public async Task StrictTriggerThrowsExpectedRejection()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Draft, Submitted, Paid }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Paid, OrderState.Submitted)]
                public partial ValueTask PayAsync();
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    try
                    {
                        await new OrderMachine(OrderState.Draft).PayAsync();
                        return "missing";
                    }
                    catch (TriggerRejectedException exception)
                    {
                        return $"{exception.State}:{exception.Rejection}";
                    }
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Draft:NotPermitted");
    }

    [Test]
    public async Task RejectsAmbiguousGuardsAtRuntime()
    {
        var run = await Compile<Func<Task<string>>>("""
            public enum OrderState { Reviewing, Approved, Rejected }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Approved, OrderState.Reviewing, Guard = nameof(Always))]
                [TransitionTo(OrderState.Rejected, OrderState.Reviewing, Guard = nameof(AlsoAlways))]
                public partial ValueTask ReviewAsync();

                private bool Always() => true;
                private bool AlsoAlways() => true;
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    try
                    {
                        await new OrderMachine(OrderState.Reviewing).ReviewAsync();
                        return "missing";
                    }
                    catch (AmbiguousTransitionException exception)
                    {
                        return exception.State.ToString()!;
                    }
                }
            }
            """);

        await Assert.That(await run()).IsEqualTo("Reviewing");
    }

    [Test]
    public async Task ReportsMultipleUnguardedTargetsAtCompilation()
    {
        var diagnostics = Generate("""
            public enum OrderState { Reviewing, Approved, Rejected }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Approved, OrderState.Reviewing)]
                [TransitionTo(OrderState.Rejected, OrderState.Reviewing)]
                public partial ValueTask ReviewAsync();
            }
            """).Diagnostics;

        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "TTSM001")).IsTrue();
    }

    [Test]
    public async Task ReportsDirectStateAssignmentInUserCode()
    {
        var diagnostics = await AnalyzeAsync("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                public void ForceSubmit() => State = OrderState.Submitted;
            }
            """);

        var errors = diagnostics.Where(diagnostic => diagnostic.Id == "TTSM002").ToArray();
        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].GetMessage()).Contains("generated triggers");
    }

    [Test]
    public async Task DoesNotReportGeneratedStateAssignment()
    {
        var diagnostics = await AnalyzeAsync("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }
            """);

        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "TTSM002")).IsFalse();
    }

    [Test]
    public async Task ReportsMembersReservedByTheGeneratedMachine()
    {
        var reservedMembers = new[]
        {
            ("State", "public OrderState State { get; set; }"),
            ("Transitioned", "public event Action? Transitioned;"),
            ("TransitionCompleted", "public event Action? TransitionCompleted;"),
            ("RaiseTransitioned", "private void RaiseTransitioned(OrderState source, OrderState destination) { }"),
            ("RaiseTransitionCompleted", "private void RaiseTransitionCompleted(OrderState source, OrderState destination) { }"),
            ("IsConsumerCallbackActive", "private bool IsConsumerCallbackActive => false;"),
            ("EnterConsumerCallback", "private void EnterConsumerCallback() { }"),
        };

        foreach (var (name, declaration) in reservedMembers)
        {
            var diagnostics = Generate("""
                public enum OrderState { Draft, Submitted }

                [StateMachine<OrderState>]
                public sealed partial class OrderMachine
                {
                    MEMBER

                    [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                    public partial ValueTask SubmitAsync();
                }
                """.Replace("MEMBER", declaration, StringComparison.Ordinal)).Diagnostics;

            var errors = diagnostics.Where(diagnostic => diagnostic.Id == "TTSM001").ToArray();
            await Assert.That(errors).HasSingleItem();
            await Assert.That(errors[0].GetMessage()).Contains(name);
        }
    }

    [Test]
    public async Task ReportsGeneratedCompanionAndConstructorCollisions()
    {
        var companionDiagnostics = Generate("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                public ValueTask TrySubmitAsync() => ValueTask.CompletedTask;
            }
            """).Diagnostics;
        var constructorDiagnostics = Generate("""
            public enum OrderState { Draft, Submitted }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                public OrderMachine(OrderState initialState) { }

                [TransitionTo(OrderState.Submitted, OrderState.Draft)]
                public partial ValueTask SubmitAsync();
            }
            """).Diagnostics;

        await Assert.That(companionDiagnostics.Single(diagnostic => diagnostic.Id == "TTSM001").GetMessage())
            .Contains("TrySubmitAsync");
        await Assert.That(constructorDiagnostics.Single(diagnostic => diagnostic.Id == "TTSM001").GetMessage())
            .Contains("constructor");
    }

    [Test]
    public async Task ExecutesAttributedLifecycleInOrderWithGeneratedParameterBinding()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                private readonly System.Collections.Generic.List<string> _events = [];

                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync(int orderId, CancellationToken cancellationToken = default);

                [OnExit(OrderState.Draft)]
                private void LeaveDraft(int orderId) => _events.Add($"exit:{orderId}:{State}");

                [OnEntry(OrderState.Reviewing)]
                private async Task EnterReviewingAsync()
                {
                    await Task.Yield();
                    _events.Add($"entry:{State}");
                }

                [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
                private async ValueTask EnterReviewingFromSubmitAsync(int orderId, CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    _events.Add($"entry-from:{orderId}:{State}");
                }

                public string Events => string.Join("|", _events);
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine(OrderState.Draft);
                    var can = await machine.CanSubmitAsync(42);
                    var before = machine.Events;
                    var result = await machine.TrySubmitAsync(42);
                    return $"{can}:{before}:{result.Succeeded}:{machine.Events}:{machine.State}";
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo(
            "True::True:exit:42:Draft|entry:Reviewing|entry-from:42:Reviewing:Reviewing");
    }

    [Test]
    public async Task PublishesTransitionEventsWithStatelessOrdering()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                public System.Action<string>? Observe { get; set; }

                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnExit(OrderState.Draft)]
                private void LeaveDraft() => Observe!("exit:" + State);

                [OnEntry(OrderState.Reviewing)]
                private void EnterReviewing() => Observe!("entry:" + State);

                [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
                private void EnterFromSubmit() => Observe!("entry-from:" + State);
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var events = new System.Collections.Generic.List<string>();
                    var machine = new OrderMachine(OrderState.Draft) { Observe = events.Add };
                    machine.Transitioned += (_, transition) =>
                        events.Add($"transitioned:{transition.Source}>{transition.Destination}:{machine.State}");
                    machine.TransitionCompleted += (_, transition) =>
                        events.Add($"completed:{transition.Source}>{transition.Destination}:{machine.State}");

                    await machine.SubmitAsync();
                    return string.Join("|", events);
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo(
            "exit:Draft|transitioned:Draft>Reviewing:Reviewing|entry:Reviewing|entry-from:Reviewing|completed:Draft>Reviewing:Reviewing");
    }

    [Test]
    public async Task RejectsReentrantTriggerAndClearsProtectionAfterFailure()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing, Completed }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                public bool CanCompleteDuringEntry { get; private set; }

                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [TransitionTo(OrderState.Completed, OrderState.Reviewing, Guard = nameof(CanComplete))]
                public partial ValueTask CompleteAsync();

                private bool CanComplete() => true;

                [OnEntry(OrderState.Reviewing)]
                private async ValueTask EnterReviewingAsync()
                {
                    CanCompleteDuringEntry = await CanCompleteAsync();
                    await CompleteAsync();
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine(OrderState.Draft);
                    string rejected;
                    try
                    {
                        await machine.SubmitAsync();
                        rejected = "none";
                    }
                    catch (ReentrantTriggerException exception)
                    {
                        rejected = $"{exception.Trigger}:{exception.State}:{machine.State}";
                    }

                    var completed = await machine.TryCompleteAsync();
                    return $"{rejected}:{machine.CanCompleteDuringEntry}:{completed.Succeeded}:{machine.State}";
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo("CompleteAsync:Reviewing:Reviewing:True:True:Completed");
    }

    [Test]
    public async Task RejectsReentrantTriggerFromTransitionEvent()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing, Completed }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [TransitionTo(OrderState.Completed, OrderState.Reviewing)]
                public partial ValueTask CompleteAsync();
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var machine = new OrderMachine(OrderState.Draft);
                    var nested = default(ValueTask<TriggerResult<OrderState>>);
                    machine.Transitioned += (_, transition) =>
                    {
                        if (transition.Destination == OrderState.Reviewing)
                            nested = machine.TryCompleteAsync();
                    };

                    await machine.SubmitAsync();
                    try
                    {
                        await nested;
                        return "none";
                    }
                    catch (ReentrantTriggerException exception)
                    {
                        return $"{exception.Trigger}:{exception.State}:{machine.State}";
                    }
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo("CompleteAsync:Reviewing:Reviewing");
    }

    [Test]
    public async Task EmitsConsumerCallbacksWithUsingScope()
    {
        var generated = Generate("""
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                private bool CanSubmit() => true;

                [OnEntry(OrderState.Reviewing)]
                private async ValueTask EnterReviewingAsync() => await ValueTask.CompletedTask;
            }
            """);
        var generatedSource = generated.Sources.Single();

        await Assert.That(generatedSource).Contains("using (EnterConsumerCallback())");
        await Assert.That(generatedSource.Contains("finally", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task PublishesOnlyCommittedEventWhenEntryFailsAndNoEventsWhenRejected()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnEntry(OrderState.Reviewing)]
                private void EnterReviewing() => throw new InvalidOperationException("entry");
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var failed = new OrderMachine(OrderState.Draft);
                    var failedTransitioned = 0;
                    var failedCompleted = 0;
                    failed.Transitioned += (_, _) => failedTransitioned++;
                    failed.TransitionCompleted += (_, _) => failedCompleted++;
                    try { await failed.TrySubmitAsync(); } catch (InvalidOperationException) { }

                    var rejected = new OrderMachine(OrderState.Reviewing);
                    var rejectedTransitioned = 0;
                    var rejectedCompleted = 0;
                    rejected.Transitioned += (_, _) => rejectedTransitioned++;
                    rejected.TransitionCompleted += (_, _) => rejectedCompleted++;
                    var result = await rejected.TrySubmitAsync();

                    return $"{failed.State}:{failedTransitioned}:{failedCompleted}:" +
                        $"{result.Succeeded}:{rejectedTransitioned}:{rejectedCompleted}";
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo("Reviewing:1:0:False:0:0");
    }

    [Test]
    public async Task PreservesSourceBeforeCommitAndTargetAfterEntryFailure()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class ExitFailureMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnExit(OrderState.Draft)]
                private void LeaveDraft() => throw new InvalidOperationException("exit");
            }

            [StateMachine<OrderState>]
            public sealed partial class EntryFailureMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
                private void EnterFromSubmit() => throw new InvalidOperationException("entry");
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var exit = new ExitFailureMachine(OrderState.Draft);
                    var entry = new EntryFailureMachine(OrderState.Draft);
                    try { await exit.TrySubmitAsync(); } catch (InvalidOperationException) { }
                    try { await entry.TrySubmitAsync(); } catch (InvalidOperationException) { }
                    return $"{exit.State}:{entry.State}";
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo("Draft:Reviewing");
    }

    [Test]
    public async Task RunsOnlyTheLifecycleForTheSelectedGuardedOrFallbackTarget()
    {
        const string source = """
            public enum OrderState { Reviewing, Approved, ManualReview }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                private readonly System.Collections.Generic.List<string> _events = [];

                [TransitionTo(OrderState.Approved, OrderState.Reviewing, Guard = nameof(CanApprove))]
                [TransitionOtherwiseTo(OrderState.ManualReview, OrderState.Reviewing)]
                public partial ValueTask ReviewAsync(int score);

                private bool CanApprove(int score) => score >= 80;
                [OnExit(OrderState.Reviewing)]
                private void LeaveReviewing() => _events.Add("exit");

                [OnEntryFrom(OrderState.Approved, nameof(ReviewAsync))]
                private void EnterApprovedFromReview() => _events.Add("approved");

                [OnEntry(OrderState.ManualReview)]
                private void EnterManualReview() => _events.Add("manual");

                public string Events => string.Join("|", _events);
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var approved = new OrderMachine(OrderState.Reviewing);
                    var manual = new OrderMachine(OrderState.Reviewing);
                    await approved.TryReviewAsync(90);
                    await manual.TryReviewAsync(50);
                    return $"{approved.Events}:{manual.Events}";
                }
            }
            """;

        var run = await Compile<Func<Task<string>>>(source);

        await Assert.That(await run()).IsEqualTo("exit|approved:exit|manual");
    }

    [Test]
    public async Task ReportsDiagnosticForIncompatibleAttributedLifecycleHandler()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync(int orderId);

                [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
                private Task RecordSubmissionAsync(string orderId) => Task.CompletedTask;
            }
            """;

        var generated = Generate(source);
        var errors = generated.Diagnostics.Where(diagnostic => diagnostic.Id == "TTSM001").ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].GetMessage()).Contains("lifecycle handler 'RecordSubmissionAsync'");
    }

    [Test]
    public async Task ReportsAsyncVoidLifecycleHandler()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnEntry(OrderState.Reviewing)]
                private async void EnterReviewingAsync()
                {
                    await Task.Yield();
                }
            }
            """;

        var generated = Generate(source);
        var errors = generated.Diagnostics.Where(diagnostic => diagnostic.Id == "TTSM001").ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].GetMessage()).Contains("async void");
    }

    [Test]
    public async Task ReportsEntryFromTriggerThatDoesNotEnterTheState()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing, Cancelled }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [TransitionTo(OrderState.Cancelled, OrderState.Draft)]
                public partial ValueTask CancelAsync();

                [OnEntryFrom(OrderState.Reviewing, nameof(CancelAsync))]
                private void EnterReviewingFromCancel() { }
            }
            """;

        var generated = Generate(source);
        var errors = generated.Diagnostics.Where(diagnostic => diagnostic.Id == "TTSM001").ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].GetMessage()).Contains("does not enter");
    }

    [Test]
    public async Task ReportsMultipleGeneralEntryHandlersForTheSameState()
    {
        const string source = """
            public enum OrderState { Draft, Reviewing }

            [StateMachine<OrderState>]
            public sealed partial class OrderMachine
            {
                [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
                public partial ValueTask SubmitAsync();

                [OnEntry(OrderState.Reviewing)]
                private void StartReview() { }

                [OnEntry(OrderState.Reviewing)]
                private void NotifyReview() { }
            }
            """;

        var generated = Generate(source);
        var errors = generated.Diagnostics.Where(diagnostic => diagnostic.Id == "TTSM001").ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].GetMessage()).Contains("more than one Entry handler");
    }

    private static async Task<TDelegate> Compile<TDelegate>(string source) where TDelegate : Delegate
    {
        var generatedResult = Generate(source);
        var generated = generatedResult.Compilation;
        var errors = generatedResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        await Assert.That(string.Join(Environment.NewLine, errors)).IsEqualTo("");
        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream);
        await Assert.That(string.Join(Environment.NewLine,
            emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))).IsEqualTo("");
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<TDelegate>();
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = Generate(source).Compilation;
        var analyzerType = typeof(StateMachineGenerator).Assembly.GetType(
            "TedToolkit.Orchestration.StateMachine.Analyzer.StateMachineAnalyzer");
        if (analyzerType is null) return ImmutableArray<Diagnostic>.Empty;
        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType)!;
        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
    }

    private static GeneratedCompilation Generate(string source)
    {
        var compilation = CSharpCompilation.Create("StateMachineScenario_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(Imports + source, ParseOptions, "Scenario.cs")], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new StateMachineGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);
        var sources = driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString()).ToImmutableArray();
        return new GeneratedCompilation(generated, generatorDiagnostics.AddRange(generated.GetDiagnostics()), sources);
    }

    private sealed record GeneratedCompilation(
        Compilation Compilation,
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<string> Sources);
}
