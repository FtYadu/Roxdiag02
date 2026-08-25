namespace Rox.FlowEngine;

public sealed class FlowConfiguration { public List<FlowProcess> Processes { get; init; } = new(); }
public sealed class FlowProcess { public string? Name { get; init; } public List<IStep> Steps { get; init; } = new(); }

public interface IStep { }

/// <summary>A UDS transaction: a request plus response-side handling steps.</summary>
public sealed class EcuServiceStep : IStep
{
    public string? Ecu { get; init; }
    public UdsRequestSpec? Request { get; init; }
    public List<IStep> ResponseSteps { get; init; } = new();
}

public sealed class UdsRequestSpec
{
    public byte ServiceId { get; init; }
    public byte? SubFunction { get; init; }
    public List<Operand> Data { get; init; } = new();
}

public sealed class IfStep : IStep
{
    public string? Description { get; init; }
    public ConditionGroup Condition { get; init; } = new();
    public List<IStep> Then { get; init; } = new();
    public List<IStep> Else { get; init; } = new();
}

public sealed class ConditionGroup { public List<OneCondition> Conditions { get; init; } = new(); }

public enum OpSign { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }
public enum ConnectSign { And, Or, None }

public sealed class OneCondition
{
    public Operand Left { get; init; } = Operand.Literal("");
    public OpSign Op { get; init; }
    public Operand Right { get; init; } = Operand.Literal("");
    public ConnectSign Connect { get; init; } = ConnectSign.None;
}

public sealed class Operand
{
    public bool IsVariable { get; init; }
    public string Value { get; init; } = "";
    public static Operand Literal(string v) => new() { IsVariable = false, Value = v };
    public static Operand Var(string v) => new() { IsVariable = true, Value = v };
}

// --- Node types added per schema section 5.5 ---
public sealed class SecurityAccessStep : IStep
{
    public string? Ecu { get; init; }
    public byte RequestSeedSub { get; init; } = 0x01;
    public byte SendKeySub { get; init; } = 0x02;
}
public sealed class UserPromptStep : IStep
{
    public string Message { get; init; } = "";
    public int TimeoutSeconds { get; init; }
}
public sealed class AssignStep : IStep
{
    public string VariableName { get; init; } = "";
    public string SourceExpression { get; init; } = "";
}
public enum LoopKind { ForEachEcu, PollUntil }
public sealed class LoopStep : IStep
{
    public LoopKind Kind { get; init; }
    public List<IStep> Body { get; init; } = new();
    public int MaxIterations { get; init; } = 100;
    public ConditionGroup? Until { get; init; }
}
