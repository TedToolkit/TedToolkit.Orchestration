using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TedToolkit.Orchestration.StateMachine.Analyzer;

/// <summary>Generates state storage inheritance and direct implementations for partial trigger methods.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class StateMachineGenerator : IIncrementalGenerator
{
    private const string MachineAttributeName = "TedToolkit.Orchestration.StateMachine.StateMachineAttribute`1";
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

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var machines = context.SyntaxProvider.ForAttributeWithMetadataName(
            MachineAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (syntax, _) => (INamedTypeSymbol)syntax.TargetSymbol);
        context.RegisterSourceOutput(machines, static (output, machine) => Generate(output, machine));
    }

    private static void Generate(SourceProductionContext context, INamedTypeSymbol machine)
    {
        var location = machine.Locations.FirstOrDefault(item => item.IsInSource) ?? Location.None;
        if (!TryCreateModel(machine, out var model, out var error))
        {
            context.ReportDiagnostic(Diagnostic.Create(StateMachineDiagnostics.InvalidDeclaration, location, error));
            return;
        }

        context.AddSource(HintName(machine), SourceText.From(Emit(model!), Encoding.UTF8));
    }

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

    private static string Emit(MachineModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        var namespaceName = model.Machine.ContainingNamespace.IsGlobalNamespace
            ? null
            : model.Machine.ContainingNamespace.ToDisplayString();
        if (namespaceName is not null)
        {
            builder.Append("namespace ").Append(namespaceName).AppendLine(";");
            builder.AppendLine();
        }
        builder.Append(model.Machine.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ")
            .Append("sealed partial class ").Append(Identifier(model.Machine.Name)).Append(" : ")
            .Append("global::TedToolkit.Orchestration.StateMachine.StateMachine<")
            .Append(model.StateType.ToDisplayString(TypeFormat)).AppendLine(">");
        builder.AppendLine("{");
        builder.Append("    public ").Append(Identifier(model.Machine.Name)).Append('(')
            .Append(model.StateType.ToDisplayString(TypeFormat)).AppendLine(" initialState) : base(initialState)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        if (model.InitialState is not null)
        {
            builder.AppendLine();
            builder.Append("    public ").Append(Identifier(model.Machine.Name)).Append("() : this(")
                .Append(model.InitialState).AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
        }
        foreach (var trigger in model.Triggers)
        {
            builder.AppendLine();
            EmitTrigger(builder, model, trigger);
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EmitTrigger(StringBuilder builder, MachineModel model, TriggerModel trigger)
    {
        var parameters = Parameters(trigger.Method, includeCancellationDefault: false);
        var companionParameters = Parameters(trigger.Method, includeCancellationDefault: true);
        var arguments = string.Join(", ", trigger.Method.Parameters.Select(parameter => Identifier(parameter.Name)));
        var stateType = model.StateType.ToDisplayString(TypeFormat);

        builder.Append("    public partial async global::System.Threading.Tasks.ValueTask ")
            .Append(Identifier(trigger.Method.Name)).Append('(').Append(parameters).AppendLine(")");
        builder.AppendLine("    {");
        builder.Append("        var __result = await ").Append(Identifier(trigger.TryName)).Append('(').Append(arguments)
            .AppendLine(").ConfigureAwait(false);");
        builder.AppendLine("        if (!__result.Succeeded)");
        builder.Append("            throw new global::TedToolkit.Orchestration.StateMachine.TriggerRejectedException(nameof(")
            .Append(Identifier(trigger.Method.Name)).AppendLine("), __result.Source, __result.Rejection!.Value);");
        builder.AppendLine("    }");

        builder.AppendLine();
        builder.Append("    public async global::System.Threading.Tasks.ValueTask<global::TedToolkit.Orchestration.StateMachine.TriggerResult<")
            .Append(stateType).Append(">> ").Append(Identifier(trigger.TryName)).Append('(').Append(companionParameters).AppendLine(")");
        builder.AppendLine("    {");
        builder.AppendLine("        if (IsConsumerCallbackActive)");
        builder.Append("            throw new global::TedToolkit.Orchestration.StateMachine.ReentrantTriggerException(nameof(")
            .Append(Identifier(trigger.Method.Name)).AppendLine("), State);");
        builder.AppendLine("        var __source = State;");
        builder.AppendLine("        switch (__source)");
        builder.AppendLine("        {");
        foreach (var group in trigger.Routes.GroupBy(route => route.Source, StringComparer.Ordinal))
            EmitTryGroup(builder, stateType, trigger, group.ToArray());
        builder.AppendLine("            default:");
        builder.Append("                return global::TedToolkit.Orchestration.StateMachine.TriggerResult<").Append(stateType)
            .AppendLine(">.Rejected(__source, global::TedToolkit.Orchestration.StateMachine.TriggerRejection.NotPermitted);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");

        builder.AppendLine();
        builder.Append("    public async global::System.Threading.Tasks.ValueTask<bool> ").Append(Identifier(trigger.CanName))
            .Append('(').Append(companionParameters).AppendLine(")");
        builder.AppendLine("    {");
        builder.AppendLine("        var __source = State;");
        builder.AppendLine("        switch (__source)");
        builder.AppendLine("        {");
        foreach (var group in trigger.Routes.GroupBy(route => route.Source, StringComparer.Ordinal))
            EmitCanGroup(builder, trigger, group.ToArray());
        builder.AppendLine("            default:");
        builder.AppendLine("                return false;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitTryGroup(StringBuilder builder, string stateType, TriggerModel trigger, RouteModel[] routes)
    {
        builder.Append("            case ").Append(routes[0].Source).AppendLine(":");
        builder.AppendLine("            {");
        var candidates = routes.Where(route => !route.IsFallback).ToArray();
        var fallback = routes.SingleOrDefault(route => route.IsFallback);
        if (candidates.Length == 1 && candidates[0].Guard is null)
        {
            EmitTransition(builder, stateType, candidates[0], 16);
            builder.AppendLine("            }");
            return;
        }
        builder.AppendLine("                var __matches = 0;");
        builder.AppendLine("                var __target = __source;");
        builder.AppendLine("                var __accepted = false;");
        foreach (var candidate in candidates)
        {
            EmitGuardEvaluation(builder, candidate.Guard!, "__accepted", 16);
            builder.AppendLine("                if (__accepted)");
            builder.AppendLine("                {");
            builder.AppendLine("                    __matches++;");
            builder.Append("                    __target = ").Append(candidate.Target).AppendLine(";");
            builder.AppendLine("                }");
        }
        builder.AppendLine("                if (__matches > 1)");
        builder.Append("                    throw new global::TedToolkit.Orchestration.StateMachine.AmbiguousTransitionException(nameof(")
            .Append(Identifier(trigger.Method.Name)).AppendLine("), __source);");
        builder.AppendLine("                if (__matches == 1)");
        builder.AppendLine("                {");
        EmitSelectedTransition(builder, stateType, candidates, 20);
        builder.AppendLine("                }");
        if (fallback is not null) EmitTransition(builder, stateType, fallback, 16);
        else
            builder.Append("                return global::TedToolkit.Orchestration.StateMachine.TriggerResult<").Append(stateType)
                .AppendLine(">.Rejected(__source, global::TedToolkit.Orchestration.StateMachine.TriggerRejection.GuardRejected);");
        builder.AppendLine("            }");
    }

    private static void EmitTransition(StringBuilder builder, string stateType, RouteModel route, int spaces)
    {
        var indent = new string(' ', spaces);
        EmitActionCall(builder, route.ExitAction, spaces);
        builder.Append(indent).Append("State = ").Append(route.Target).AppendLine(";");
        builder.Append(indent).Append("RaiseTransitioned(__source, ").Append(route.Target).AppendLine(");");
        EmitActionCall(builder, route.EntryAction, spaces);
        EmitActionCall(builder, route.EntryFromAction, spaces);
        builder.Append(indent).Append("RaiseTransitionCompleted(__source, ").Append(route.Target).AppendLine(");");
        builder.Append(indent).Append("return global::TedToolkit.Orchestration.StateMachine.TriggerResult<").Append(stateType)
            .Append(">.Success(__source, ").Append(route.Target).AppendLine(");");
    }

    private static void EmitSelectedTransition(StringBuilder builder, string stateType,
        IReadOnlyList<RouteModel> routes, int spaces)
    {
        var indent = new string(' ', spaces);
        EmitActionCall(builder, routes[0].ExitAction, spaces);
        builder.Append(indent).AppendLine("State = __target;");
        builder.Append(indent).AppendLine("RaiseTransitioned(__source, __target);");
        var entries = routes.GroupBy(route => route.Target, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(route => route.EntryAction is not null || route.EntryFromAction is not null).ToArray();
        if (entries.Length != 0)
        {
            builder.Append(indent).AppendLine("switch (__target)");
            builder.Append(indent).AppendLine("{");
            foreach (var route in entries)
            {
                builder.Append(indent).Append("    case ").Append(route.Target).AppendLine(":");
                EmitActionCall(builder, route.EntryAction, spaces + 8);
                EmitActionCall(builder, route.EntryFromAction, spaces + 8);
                builder.Append(indent).AppendLine("        break;");
            }
            builder.Append(indent).AppendLine("}");
        }
        builder.Append(indent).AppendLine("RaiseTransitionCompleted(__source, __target);");
        builder.Append(indent).Append("return global::TedToolkit.Orchestration.StateMachine.TriggerResult<").Append(stateType)
            .AppendLine(">.Success(__source, __target);");
    }

    private static void EmitActionCall(StringBuilder builder, ActionModel? action, int spaces)
    {
        if (action is null) return;
        var indent = new string(' ', spaces);
        var call = Identifier(action.Method.Name) + "(" + string.Join(", ", action.Arguments) + ")";
        builder.Append(indent).AppendLine("using (EnterConsumerCallback())");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ");
        if (action.IsAsync) builder.Append("await ");
        builder.Append(call);
        if (action.IsAsync) builder.Append(".ConfigureAwait(false)");
        builder.AppendLine(";");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitCanGroup(StringBuilder builder, TriggerModel trigger, RouteModel[] routes)
    {
        builder.Append("            case ").Append(routes[0].Source).AppendLine(":");
        builder.AppendLine("            {");
        var candidates = routes.Where(route => !route.IsFallback).ToArray();
        var fallback = routes.SingleOrDefault(route => route.IsFallback);
        if (candidates.Length == 1 && candidates[0].Guard is null)
        {
            builder.AppendLine("                return true;");
            builder.AppendLine("            }");
            return;
        }
        builder.AppendLine("                var __matches = 0;");
        builder.AppendLine("                var __accepted = false;");
        foreach (var candidate in candidates)
        {
            EmitGuardEvaluation(builder, candidate.Guard!, "__accepted", 16);
            builder.AppendLine("                if (__accepted) __matches++;");
        }
        builder.AppendLine("                if (__matches > 1)");
        builder.Append("                    throw new global::TedToolkit.Orchestration.StateMachine.AmbiguousTransitionException(nameof(")
            .Append(Identifier(trigger.Method.Name)).AppendLine("), __source);");
        builder.Append("                return __matches == 1").Append(fallback is null ? ";" : " || __matches == 0;").AppendLine();
        builder.AppendLine("            }");
    }

    private static string GuardCall(GuardModel guard)
    {
        var call = Identifier(guard.Method.Name) + "(" + string.Join(", ", guard.Arguments) + ")";
        return guard.IsAsync ? "await " + call + ".ConfigureAwait(false)" : call;
    }

    private static void EmitGuardEvaluation(StringBuilder builder, GuardModel guard, string result, int spaces)
    {
        var indent = new string(' ', spaces);
        builder.Append(indent).AppendLine("using (EnterConsumerCallback())");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ").Append(result).Append(" = ").Append(GuardCall(guard)).AppendLine(";");
        builder.Append(indent).AppendLine("}");
    }

    private static string Parameters(IMethodSymbol method, bool includeCancellationDefault)
    {
        return string.Join(", ", method.Parameters.Select(parameter =>
        {
            var modifier = parameter.IsParams ? "params " : "";
            var result = modifier + parameter.Type.ToDisplayString(TypeFormat) + " " + Identifier(parameter.Name);
            if (includeCancellationDefault && IsCancellationToken(parameter.Type) && parameter.HasExplicitDefaultValue)
                result += " = default";
            return result;
        }));
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

    private static string Identifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "@" + value : value;

    private static string HintName(INamedTypeSymbol machine)
    {
        var name = machine.ToDisplayString().Replace('<', '_').Replace('>', '_').Replace('.', '_').Replace('+', '_');
        return name + ".StateMachine.g.cs";
    }

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
