namespace Rox.FlowEngine;

/// <summary>Runtime key-value store. IsVariable operands resolve against this at execution time.</summary>
public sealed class VariableStore
{
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

    public string? LastRawResponseHex { get; set; }
    public byte? LastNrc { get; set; }

    public void Set(string name, string value) => _vars[name] = value;
    public bool TryGet(string name, out string value) => _vars.TryGetValue(name, out value!);

    public string Resolve(Operand op) =>
        op.IsVariable ? (_vars.TryGetValue(op.Value, out var v) ? v : "") : op.Value;
}
