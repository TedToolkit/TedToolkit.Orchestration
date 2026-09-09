using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.Orchestration.StateMachine.Analyzer;

public sealed partial class StateMachineGenerator
{
    private const string TransitionAttributeName = "TedToolkit.Orchestration.StateMachine.TransitionToAttribute";
    private const string OtherwiseAttributeName = "TedToolkit.Orchestration.StateMachine.TransitionOtherwiseToAttribute";
    private const string OnEntryAttributeName = "TedToolkit.Orchestration.StateMachine.OnEntryAttribute";
    private const string OnEntryFromAttributeName = "TedToolkit.Orchestration.StateMachine.OnEntryFromAttribute";
    private const string OnExitAttributeName = "TedToolkit.Orchestration.StateMachine.OnExitAttribute";
    private const string CancellationTokenName = "System.Threading.CancellationToken";

    private static readonly string[] ReservedMemberNames =
    [
        "State",
        "Transitioned",
        "TransitionCompleted",
        "RaiseTransitioned",
        "RaiseTransitionCompleted",
        "IsConsumerCallbackActive",
        "EnterConsumerCallback",
    ];

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static bool TryCreateModel(INamedTypeSymbol machine, out MachineModel? model, out string error)
    {
        model = null;
        error = "";
        if (machine.TypeKind != TypeKind.Class || machine.IsStatic || machine.IsAbstract || machine.IsFileLocal ||
            machine.ContainingType is not null || machine.Arity != 0 || !machine.IsSealed)
            return Fail("use a sealed, non-generic, top-level partial class", out error);
        if (machine.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>().Any(declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return Fail("declare the state machine class partial", out error);
        if (machine.BaseType is not { SpecialType: SpecialType.System_Object })
            return Fail("remove the explicit base class; the generator supplies StateMachine<TState>", out error);
        var machineAttributes = machine.GetAttributes()
            .Where(attribute => attribute.AttributeClass is { MetadataName: "StateMachineAttribute`1" } attributeClass &&
                                attributeClass.ContainingNamespace.ToDisplayString() ==
                                "TedToolkit.Orchestration.StateMachine").ToArray();
        if (machineAttributes.Length != 1)
            return Fail("declare exactly one StateMachine<TState> attribute with an enum state type", out error);
        var machineAttributeType = machineAttributes[0].AttributeClass!;
        if (machineAttributeType.TypeArguments.Length != 1 ||
            machineAttributeType.TypeArguments[0] is not INamedTypeSymbol { TypeKind: TypeKind.Enum } stateType)
            return Fail("declare exactly one StateMachine<TState> attribute with an enum state type", out error);
        string? initialState = null;
        var machineAttribute = machineAttributes[0];
        if (machineAttribute.ConstructorArguments.Length == 1 &&
            !TryStateExpression(machineAttribute.ConstructorArguments[0], stateType, out initialState))
            return Fail($"the initial state must be a named {stateType.Name} member", out error);
        if (machine.InstanceConstructors.Any(constructor =>
                !constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, stateType)))
            return Fail("remove the constructor that only takes the initial state; the generator supplies it", out error);
        if (initialState is not null && machine.InstanceConstructors.Any(constructor =>
                !constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 0))
            return Fail("remove the parameterless constructor; the generator supplies it when the StateMachine attribute declares an initial state", out error);
        foreach (var reservedName in ReservedMemberNames)
            if (machine.GetMembers(reservedName).Length != 0)
                return Fail($"rename member '{reservedName}'; the generated state machine reserves that name", out error);

        var triggers = new List<TriggerModel>();
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in machine.GetMembers().OfType<IMethodSymbol>())
        {
            var attributes = method.GetAttributes().Where(attribute =>
                attribute.AttributeClass?.ToDisplayString() is TransitionAttributeName or OtherwiseAttributeName).ToArray();
            if (attributes.Length == 0) continue;
            if (!TryCreateTrigger(machine, stateType, method, attributes, out var trigger, out error)) return false;
            if (!stems.Add(trigger!.Stem))
                return Fail($"trigger names must be unique after removing the Async suffix ('{trigger.Stem}')", out error);
            if (machine.GetMembers(trigger.TryName).Length != 0 || machine.GetMembers(trigger.CanName).Length != 0)
                return Fail($"rename members that collide with generated '{trigger.TryName}' or '{trigger.CanName}'", out error);
            triggers.Add(trigger);
        }
        if (triggers.Count == 0)
            return Fail("declare at least one partial trigger with TransitionTo", out error);
        if (!TryApplyLifecycle(machine, stateType, triggers, out error)) return false;

        model = new MachineModel(machine, stateType, initialState, triggers);
        return true;
    }

    private static bool TryCreateTrigger(INamedTypeSymbol machine, INamedTypeSymbol stateType, IMethodSymbol method,
        AttributeData[] attributes, out TriggerModel? trigger, out string error)
    {
        trigger = null;
        error = "";
        if (method.DeclaredAccessibility != Accessibility.Public || method.IsStatic || method.IsAbstract ||
            method.IsVirtual || method.IsGenericMethod || !method.IsPartialDefinition || method.PartialImplementationPart is not null)
            return Fail($"trigger '{method.Name}' must be a public, non-static partial method declaration without a body", out error);
        if (!IsValueTask(method.ReturnType))
            return Fail($"trigger '{method.Name}' must return ValueTask", out error);
        if (method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer))
            return Fail($"trigger '{method.Name}' parameters must be supported by-value values", out error);
        var cancellation = method.Parameters.Where(parameter => IsCancellationToken(parameter.Type)).ToArray();
        if (cancellation.Length > 1 || cancellation.Length == 1 && !SymbolEqualityComparer.Default.Equals(cancellation[0], method.Parameters.Last()))
            return Fail($"trigger '{method.Name}' may have one CancellationToken as its final parameter", out error);
        if (method.Parameters.Any(parameter => parameter.HasExplicitDefaultValue && !IsCancellationToken(parameter.Type)))
            return Fail($"trigger '{method.Name}' only supports a default value on its final CancellationToken", out error);

        var routes = new List<RouteModel>();
        foreach (var attribute in attributes)
        {
            var fallback = attribute.AttributeClass?.ToDisplayString() == OtherwiseAttributeName;
            if (attribute.ConstructorArguments.Length != 2 ||
                !TryStateExpression(attribute.ConstructorArguments[0], stateType, out var target))
                return Fail($"trigger '{method.Name}' has a target that is not a named {stateType.Name} member", out error);
            var sources = attribute.ConstructorArguments[1];
            if (sources.Kind != TypedConstantKind.Array || sources.Values.IsDefaultOrEmpty)
                return Fail($"trigger '{method.Name}' must declare at least one source state", out error);
            string? guardName = null;
            if (!fallback)
                guardName = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Guard").Value.Value as string;
            foreach (var source in sources.Values)
            {
                if (!TryStateExpression(source, stateType, out var sourceExpression))
                    return Fail($"trigger '{method.Name}' has a source that is not a named {stateType.Name} member", out error);
                routes.Add(new RouteModel(sourceExpression, target, guardName, fallback));
            }
        }

        var stem = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name.Substring(0, method.Name.Length - 5) : method.Name;
        GuardModel? convention = null;
        var conventionName = "Can" + stem;
        if (machine.GetMembers(conventionName).OfType<IMethodSymbol>().Any() &&
            !TryResolveGuard(machine, method, conventionName, out convention, out error)) return false;

        foreach (var group in routes.GroupBy(route => route.Source, StringComparer.Ordinal))
        {
            var candidates = group.Where(route => !route.IsFallback).ToArray();
            var fallbacks = group.Where(route => route.IsFallback).ToArray();
            if (fallbacks.Length > 1)
                return Fail($"trigger '{method.Name}' has more than one otherwise transition from {group.Key}", out error);
            if (candidates.Length == 0)
                return Fail($"trigger '{method.Name}' has an otherwise transition without a guarded candidate from {group.Key}", out error);
            if (candidates.Length == 1 && candidates[0].GuardName is null && convention is not null)
                candidates[0].Guard = convention;
            if (candidates.Length > 1 && candidates.Any(route => route.GuardName is null))
                return Fail($"trigger '{method.Name}' has multiple targets from {group.Key}; every candidate needs an explicit Guard", out error);
            if (fallbacks.Length == 1 && candidates.Any(route => route.GuardName is null && route.Guard is null))
                return Fail($"trigger '{method.Name}' has an otherwise transition after an unconditional candidate from {group.Key}", out error);
        }

        foreach (var route in routes.Where(route => !route.IsFallback && route.Guard is null && route.GuardName is not null))
        {
            if (!TryResolveGuard(machine, method, route.GuardName!, out var guard, out error)) return false;
            route.Guard = guard;
        }

        var duplicate = routes.GroupBy(route => route.Source + "|" + route.Target + "|" + route.IsFallback + "|" + route.GuardName,
            StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) return Fail($"trigger '{method.Name}' declares the same transition more than once", out error);

        trigger = new TriggerModel(method, stem, routes);
        return true;
    }

    private static bool TryResolveGuard(INamedTypeSymbol machine, IMethodSymbol trigger, string name,
        out GuardModel? guard, out string error)
    {
        guard = null;
        error = "";
        var compatible = new List<GuardModel>();
        foreach (var method in machine.GetMembers(name).OfType<IMethodSymbol>())
        {
            if (method.IsGenericMethod || method.RefKind != RefKind.None) continue;
            var isAsync = IsValueTaskOfBoolean(method.ReturnType) || IsTaskOfBoolean(method.ReturnType);
            if (!isAsync && method.ReturnType.SpecialType != SpecialType.System_Boolean) continue;
            if (TryBindParameters(trigger, method, out var arguments))
                compatible.Add(new GuardModel(method, arguments, isAsync));
        }
        if (compatible.Count != 1)
            return Fail($"guard '{name}' for trigger '{trigger.Name}' must resolve to one bool, Task<bool>, or ValueTask<bool> method whose parameters bind by name and type", out error);
        guard = compatible[0];
        return true;
    }

    private static bool TryApplyLifecycle(INamedTypeSymbol machine, INamedTypeSymbol stateType,
        IReadOnlyList<TriggerModel> triggers, out string error)
    {
        var declarations = new List<LifecycleDeclaration>();
        foreach (var method in machine.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attribute in method.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name is not (OnEntryAttributeName or OnEntryFromAttributeName or OnExitAttributeName)) continue;
                if (!TryAddLifecycleDeclarations(method, attribute, stateType, triggers, declarations, out error))
                    return false;
            }
        }

        foreach (var trigger in triggers)
        {
            foreach (var route in trigger.Routes)
            {
                if (!TryResolveLifecycleAction(declarations, LifecycleKind.Exit, route.Source, null, trigger.Method,
                        out var exitAction, out error) ||
                    !TryResolveLifecycleAction(declarations, LifecycleKind.Entry, route.Target, null, trigger.Method,
                        out var entryAction, out error) ||
                    !TryResolveLifecycleAction(declarations, LifecycleKind.EntryFrom, route.Target,
                        trigger.Method.Name, trigger.Method, out var entryFromAction, out error))
                    return false;
                route.ExitAction = exitAction;
                route.EntryAction = entryAction;
                route.EntryFromAction = entryFromAction;
            }
        }

        var unused = declarations.FirstOrDefault(declaration => !declaration.Matched);
        if (unused is not null)
            return Fail($"lifecycle handler '{unused.Method.Name}' targets {unused.State}, but no transition uses that lifecycle declaration", out error);
        error = "";
        return true;
    }

    private static bool TryAddLifecycleDeclarations(IMethodSymbol method, AttributeData attribute,
        INamedTypeSymbol stateType, IReadOnlyList<TriggerModel> triggers, ICollection<LifecycleDeclaration> declarations,
        out string error)
    {
        var attributeName = attribute.AttributeClass?.ToDisplayString();
        if (attributeName == OnEntryFromAttributeName)
            return TryAddEntryFromDeclarations(method, attribute, stateType, triggers, declarations, out error);

        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Array } states ||
            states.Values.IsDefaultOrEmpty)
            return Fail($"lifecycle handler '{method.Name}' must declare at least one state", out error);
        var kind = attributeName == OnExitAttributeName ? LifecycleKind.Exit : LifecycleKind.Entry;
        foreach (var state in states.Values)
        {
            if (!TryStateExpression(state, stateType, out var stateExpression))
                return Fail($"lifecycle handler '{method.Name}' contains a state that is not a named {stateType.Name} member", out error);
            declarations.Add(new LifecycleDeclaration(method, kind, stateExpression, null));
        }
        error = "";
        return true;
    }

    private static bool TryAddEntryFromDeclarations(IMethodSymbol method, AttributeData attribute,
        INamedTypeSymbol stateType, IReadOnlyList<TriggerModel> triggers, ICollection<LifecycleDeclaration> declarations,
        out string error)
    {
        if (attribute.ConstructorArguments.Length != 2 ||
            !TryStateExpression(attribute.ConstructorArguments[0], stateType, out var state) ||
            attribute.ConstructorArguments[1] is not { Kind: TypedConstantKind.Array } triggerConstants ||
            triggerConstants.Values.IsDefaultOrEmpty)
            return Fail($"lifecycle handler '{method.Name}' must declare one target state and at least one trigger", out error);

        foreach (var triggerConstant in triggerConstants.Values)
        {
            if (triggerConstant.Value is not string triggerName || string.IsNullOrWhiteSpace(triggerName))
                return Fail($"lifecycle handler '{method.Name}' contains an invalid trigger name", out error);
            var trigger = triggers.SingleOrDefault(candidate => candidate.Method.Name == triggerName);
            if (trigger is null)
                return Fail($"lifecycle handler '{method.Name}' references '{triggerName}', which is not a declared trigger", out error);
            if (!trigger.Routes.Any(route => route.Target == state))
                return Fail($"trigger '{triggerName}' does not enter {state} for lifecycle handler '{method.Name}'", out error);
            declarations.Add(new LifecycleDeclaration(method, LifecycleKind.EntryFrom, state, triggerName));
        }
        error = "";
        return true;
    }

    private static bool TryResolveLifecycleAction(IReadOnlyList<LifecycleDeclaration> declarations,
        LifecycleKind kind, string state, string? triggerName, IMethodSymbol trigger, out ActionModel? action,
        out string error)
    {
        action = null;
        var matches = declarations.Where(declaration => declaration.Kind == kind && declaration.State == state &&
            declaration.TriggerName == triggerName).ToArray();
        if (matches.Length > 1)
            return Fail($"more than one {kind} handler applies to trigger '{trigger.Name}' and state {state}", out error);
        if (matches.Length == 0)
        {
            error = "";
            return true;
        }

        matches[0].Matched = true;
        return TryCreateLifecycleAction(matches[0].Method, trigger, out action, out error);
    }

    private static bool TryCreateLifecycleAction(IMethodSymbol method, IMethodSymbol trigger,
        out ActionModel? action, out string error)
    {
        action = null;
        error = "";
        if (method.IsStatic || method.IsAbstract || method.IsVirtual || method.IsGenericMethod ||
            method.IsPartialDefinition || method.PartialDefinitionPart is not null || method.RefKind != RefKind.None)
            return Fail($"lifecycle handler '{method.Name}' must be a non-static, non-generic method with a body", out error);
        if (method.IsAsync && method.ReturnsVoid)
            return Fail($"lifecycle handler '{method.Name}' cannot be async void", out error);
        var isAsync = IsValueTask(method.ReturnType) || IsTask(method.ReturnType);
        if (!isAsync && !method.ReturnsVoid)
            return Fail($"lifecycle handler '{method.Name}' must return void, Task, or ValueTask", out error);
        if (!TryBindParameters(trigger, method, out var arguments))
            return Fail($"lifecycle handler '{method.Name}' parameters must bind by name and type to trigger '{trigger.Name}'", out error);
        action = new ActionModel(method, arguments, isAsync);
        return true;
    }

    private static bool TryBindParameters(IMethodSymbol trigger, IMethodSymbol target,
        out IReadOnlyList<string> arguments)
    {
        var bound = new List<string>();
        foreach (var parameter in target.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                arguments = Array.Empty<string>();
                return false;
            }
            if (IsCancellationToken(parameter.Type))
            {
                bound.Add(trigger.Parameters.FirstOrDefault(item => IsCancellationToken(item.Type)) is { } token
                    ? Identifier(token.Name)
                    : "default");
                continue;
            }
            var source = trigger.Parameters.FirstOrDefault(item => item.Name == parameter.Name &&
                SymbolEqualityComparer.IncludeNullability.Equals(item.Type, parameter.Type));
            if (source is null)
            {
                arguments = Array.Empty<string>();
                return false;
            }
            bound.Add(Identifier(source.Name));
        }
        arguments = bound;
        return true;
    }
    private static bool TryStateExpression(TypedConstant constant, INamedTypeSymbol stateType, out string expression)
    {
        expression = "";
        if (!SymbolEqualityComparer.Default.Equals(constant.Type, stateType) || constant.Value is null) return false;
        var field = stateType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
            member.HasConstantValue && Equals(member.ConstantValue, constant.Value));
        if (field is null) return false;
        expression = stateType.ToDisplayString(TypeFormat) + "." + Identifier(field.Name);
        return true;
    }

    private static bool IsValueTask(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "ValueTask", Arity: 0 } named &&
        named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

    private static bool IsTask(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Task", Arity: 0 } named &&
        named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

    private static bool IsValueTaskOfBoolean(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "ValueTask", Arity: 1 } named &&
        named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
        named.TypeArguments[0].SpecialType == SpecialType.System_Boolean;

    private static bool IsTaskOfBoolean(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Task", Arity: 1 } named &&
        named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
        named.TypeArguments[0].SpecialType == SpecialType.System_Boolean;

    private static bool IsCancellationToken(ITypeSymbol type) => type.ToDisplayString() == CancellationTokenName;

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private sealed class MachineModel
    {
        internal MachineModel(INamedTypeSymbol machine, INamedTypeSymbol stateType, string? initialState,
            IReadOnlyList<TriggerModel> triggers)
        {
            Machine = machine;
            StateType = stateType;
            InitialState = initialState;
            Triggers = triggers;
        }

        internal INamedTypeSymbol Machine { get; }
        internal INamedTypeSymbol StateType { get; }
        internal string? InitialState { get; }
        internal IReadOnlyList<TriggerModel> Triggers { get; }
    }

    private sealed class TriggerModel
    {
        internal TriggerModel(IMethodSymbol method, string stem, IReadOnlyList<RouteModel> routes)
        {
            Method = method;
            Stem = stem;
            Routes = routes;
        }

        internal IMethodSymbol Method { get; }
        internal string Stem { get; }
        internal string TryName => "Try" + Stem + "Async";
        internal string CanName => "Can" + Stem + "Async";
        internal IReadOnlyList<RouteModel> Routes { get; }
    }

    private sealed class RouteModel
    {
        internal RouteModel(string source, string target, string? guardName, bool isFallback)
        {
            Source = source;
            Target = target;
            GuardName = guardName;
            IsFallback = isFallback;
        }

        internal string Source { get; }
        internal string Target { get; }
        internal string? GuardName { get; }
        internal bool IsFallback { get; }
        internal GuardModel? Guard { get; set; }
        internal ActionModel? ExitAction { get; set; }
        internal ActionModel? EntryAction { get; set; }
        internal ActionModel? EntryFromAction { get; set; }
    }

    private sealed class GuardModel
    {
        internal GuardModel(IMethodSymbol method, IReadOnlyList<string> arguments, bool isAsync)
        {
            Method = method;
            Arguments = arguments;
            IsAsync = isAsync;
        }

        internal IMethodSymbol Method { get; }
        internal IReadOnlyList<string> Arguments { get; }
        internal bool IsAsync { get; }
    }

    private enum LifecycleKind
    {
        Exit,
        Entry,
        EntryFrom,
    }

    private sealed class LifecycleDeclaration
    {
        internal LifecycleDeclaration(IMethodSymbol method, LifecycleKind kind, string state, string? triggerName)
        {
            Method = method;
            Kind = kind;
            State = state;
            TriggerName = triggerName;
        }

        internal IMethodSymbol Method { get; }
        internal LifecycleKind Kind { get; }
        internal string State { get; }
        internal string? TriggerName { get; }
        internal bool Matched { get; set; }
    }

    private sealed class ActionModel
    {
        internal ActionModel(IMethodSymbol method, IReadOnlyList<string> arguments, bool isAsync)
        {
            Method = method;
            Arguments = arguments;
            IsAsync = isAsync;
        }

        internal IMethodSymbol Method { get; }
        internal IReadOnlyList<string> Arguments { get; }
        internal bool IsAsync { get; }
    }
}