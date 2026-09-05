using TedToolkit.Orchestration.StateMachine;

Console.WriteLine("StateMachine playground");
Console.WriteLine();

var empty = new OrderMachine { ItemCount = 0 };
var canSubmit = await empty.CanSubmitAsync(orderId: 1000);
var rejected = await empty.TrySubmitAsync(orderId: 1000);
Console.WriteLine(
    $"Empty order: CanSubmit={canSubmit}, Succeeded={rejected.Succeeded}, Rejection={rejected.Rejection}, State={empty.State}");
Console.WriteLine();

await RunOrderAsync("Approved route", orderId: 1001, score: 90);
await RunOrderAsync("Rejected route", orderId: 1002, score: 20);
await RunOrderAsync("Otherwise route", orderId: 1003, score: 60);

static async Task RunOrderAsync(string label, int orderId, int score)
{
    var timeline = new List<string>();
    var machine = new OrderMachine { ItemCount = 2, Observe = timeline.Add };
    var reentrantAttempt = default(ValueTask<TriggerResult<OrderState>>);
    machine.Transitioned += (_, transition) =>
    {
        timeline.Add($"Transitioned {transition.Source} -> {transition.Destination}, State={machine.State}");
        if (transition.Destination == OrderState.Reviewing)
            reentrantAttempt = machine.TryReviewAsync(score);
    };
    machine.TransitionCompleted += (_, transition) =>
        timeline.Add($"TransitionCompleted {transition.Source} -> {transition.Destination}, State={machine.State}");
    var submitted = await machine.TrySubmitAsync(orderId);
    try
    {
        await reentrantAttempt;
    }
    catch (ReentrantTriggerException exception)
    {
        timeline.Add($"Reentry blocked: {exception.Trigger}, State={exception.State}");
    }
    var reviewed = await machine.TryReviewAsync(score);

    Console.WriteLine($"{label}: {submitted.Source} -> {submitted.Destination} -> {reviewed.Destination}");
    foreach (var item in timeline)
        Console.WriteLine($"  {item}");
    Console.WriteLine();
}

public enum OrderState
{
    Draft,
    Reviewing,
    Approved,
    Rejected,
    ManualReview,
}

[StateMachine<OrderState>(OrderState.Draft)]
public sealed partial class OrderMachine
{
    public int ItemCount { get; init; }

    public Action<string>? Observe { get; init; }

    [TransitionTo(OrderState.Reviewing, OrderState.Draft)]
    public partial ValueTask SubmitAsync(int orderId);

    private bool CanSubmit(int orderId) => ItemCount > 0 && orderId > 0;

    [TransitionTo(OrderState.Approved, OrderState.Reviewing, Guard = nameof(CanApprove))]
    [TransitionTo(OrderState.Rejected, OrderState.Reviewing, Guard = nameof(CanReject))]
    [TransitionOtherwiseTo(OrderState.ManualReview, OrderState.Reviewing)]
    public partial ValueTask ReviewAsync(int score);

    private Task<bool> CanApprove(int score) => Task.FromResult(score >= 80);

    private ValueTask<bool> CanReject(int score) => ValueTask.FromResult(score < 40);

    [OnExit(OrderState.Draft)]
    private void LeaveDraft()
    {
        Observe?.Invoke($"OnExit Draft [void], State={State}");
    }

    [OnEntry(OrderState.Reviewing)]
    private async Task EnterReviewingAsync()
    {
        await Task.Yield();
        Observe?.Invoke($"OnEntry Reviewing [Task], State={State}");
    }

    [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
    private async ValueTask EnterReviewingFromSubmitAsync(int orderId)
    {
        await Task.Yield();
        Observe?.Invoke($"OnEntryFrom SubmitAsync [ValueTask], OrderId={orderId}, State={State}");
    }

    [OnExit(OrderState.Reviewing)]
    private void LeaveReviewing()
    {
        Observe?.Invoke($"OnExit Reviewing [void], State={State}");
    }

    [OnEntry(OrderState.Approved, OrderState.Rejected, OrderState.ManualReview)]
    private void EnterOutcome()
    {
        Observe?.Invoke($"OnEntry outcome [void], State={State}");
    }
}
