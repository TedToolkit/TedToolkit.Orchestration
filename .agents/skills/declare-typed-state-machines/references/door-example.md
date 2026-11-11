# Complete door state machine example

```csharp
using TedToolkit.Orchestration.StateMachine;

var door = new DoorMachine();
door.TransitionCompleted += (_, transition) =>
    Console.WriteLine($"{transition.Source} -> {transition.Destination}");

await door.OpenAsync();

public enum DoorState
{
    Closed,
    Open,
}

[StateMachine<DoorState>(DoorState.Closed)]
public sealed partial class DoorMachine
{
    [TransitionTo(DoorState.Open, DoorState.Closed)]
    public partial ValueTask OpenAsync();

    [TransitionTo(DoorState.Closed, DoorState.Open)]
    public partial ValueTask CloseAsync();
}
```
