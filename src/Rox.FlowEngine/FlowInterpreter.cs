using Rox.Core;

namespace Rox.FlowEngine;

/// <summary>
/// Executes a FlowProcess AST against an IEcuServiceExecutor. Implements the two parser rules
/// from schema 5.4: (1) a trailing ConnectSign is a no-op; (2) consecutive bare acceptance-If
/// steps on the same variable are coalesced into an OR-set gate.
/// </summary>
public sealed class FlowInterpreter
{
    private readonly IEcuServiceExecutor _executor;
    private readonly ISecurityProvider? _security;
    private readonly IUserPrompt _prompt;
    private readonly FlowSemantics _semantics;
    private readonly VariableStore _vars;
    private readonly List<string> _log = new();

    public IReadOnlyList<string> Log => _log;
    public VariableStore Variables => _vars;

    public FlowInterpreter(IEcuServiceExecutor executor, ISecurityProvider? security = null,
        IUserPrompt? prompt = null, FlowSemantics? semantics = null, VariableStore? vars = null)
    {
        _executor = executor;
        _security = security;
        _prompt = prompt ?? new AutoContinuePrompt();
        _semantics = semantics ?? new FlowSemantics();
        _vars = vars ?? new VariableStore();
    }

    public void Run(FlowProcess process)
    {
        _log.Add($"Process: {process.Name ?? "(unnamed)"}");
        Execute(process.Steps);
    }

    private void Execute(List<IStep> steps)
    {
        int i = 0;
        while (i < steps.Count)
        {
            if (steps[i] is IfStep bare && IsBareAcceptance(bare))
            {
                Operand? left = null;
                var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int j = i;
                while (j < steps.Count && steps[j] is IfStep g && IsBareAcceptance(g) && SameSingleLeft(g, ref left))
                {
                    accepted.Add(_vars.Resolve(g.Condition.Conditions[0].Right));
                    j++;
                }
                EvaluateAcceptanceGate(left!, accepted);
                i = j;
                continue;
            }
            ExecuteStep(steps[i]);
            i++;
        }
    }

    private void ExecuteStep(IStep step)
    {
        switch (step)
        {
            case EcuServiceStep svc: RunEcuService(svc); break;
            case SecurityAccessStep sa: RunSecurityAccess(sa); break;
            case UserPromptStep up:
                _log.Add($"Prompt: {up.Message}");
                if (!_prompt.Prompt(up.Message, up.TimeoutSeconds)) throw new FlowException("User cancelled at prompt.");
                break;
            case AssignStep asn: RunAssign(asn); break;
            case LoopStep lp: RunLoop(lp); break;
            case IfStep iff: RunIf(iff); break;
            default: throw new FlowException($"Unknown step type {step.GetType().Name}");
        }
    }

    private void RunEcuService(EcuServiceStep svc)
    {
        if (svc.Request is null) { _log.Add($"EcuService {svc.Ecu}: (no request)"); Execute(svc.ResponseSteps); return; }
        var req = BuildRequest(svc.Request);
        var resp = UdsResponse.Parse(_executor.Execute(svc.Ecu, req));
        _vars.LastRawResponseHex = Convert.ToHexString(resp.Raw);
        _vars.LastNrc = resp.IsNegative ? resp.Nrc : null;
        var status = resp.IsPositive ? _semantics.PositiveStatus
                   : resp.IsPending ? _semantics.PendingStatus
                   : _semantics.NegativeStatus;
        _vars.Set("ResponseStatus", status);
        _log.Add($"EcuService {svc.Ecu}: req={Convert.ToHexString(req)} resp={_vars.LastRawResponseHex} ResponseStatus={status}"
               + (resp.IsNegative ? $" NRC={Nrc.Describe(resp.Nrc)}" : ""));
        Execute(svc.ResponseSteps);
    }

    private void RunSecurityAccess(SecurityAccessStep sa)
    {
        if (_security is null) throw new FlowException("No security provider configured (user-supplied module required).");
        var seedResp = UdsResponse.Parse(_executor.Execute(sa.Ecu, new[] { UdsServices.SecurityAccess, sa.RequestSeedSub }));
        if (seedResp.IsNegative) { _vars.Set("ResponseStatus", _semantics.NegativeStatus); throw new FlowException($"Seed request denied: {Nrc.Describe(seedResp.Nrc)}"); }
        var seed = seedResp.Payload.Length > 1 ? seedResp.Payload.Slice(1).ToArray() : Array.Empty<byte>();
        var key = _security.ComputeKey(seed, seed.Length);
        var keyReq = new byte[2 + key.Length];
        keyReq[0] = UdsServices.SecurityAccess; keyReq[1] = sa.SendKeySub;
        Array.Copy(key, 0, keyReq, 2, key.Length);
        var keyResp = UdsResponse.Parse(_executor.Execute(sa.Ecu, keyReq));
        _vars.Set("ResponseStatus", keyResp.IsPositive ? _semantics.PositiveStatus : _semantics.NegativeStatus);
        _vars.Set("SecurityGranted", keyResp.IsPositive ? "1" : "0");
        _log.Add($"SecurityAccess {sa.Ecu}: seed={Convert.ToHexString(seed)} -> {(keyResp.IsPositive ? "GRANTED" : "DENIED")}");
        if (keyResp.IsNegative) throw new FlowException($"Security denied: {Nrc.Describe(keyResp.Nrc)}");
    }

    private void RunAssign(AssignStep asn)
    {
        var src = asn.SourceExpression.Trim();
        string value;
        if (src.StartsWith("response[", StringComparison.OrdinalIgnoreCase) && src.EndsWith("]"))
        {
            var inner = src.Substring("response[".Length, src.Length - "response[".Length - 1);
            var parts = inner.Split("..", StringSplitOptions.TrimEntries);
            var bytes = Convert.FromHexString(_vars.LastRawResponseHex ?? "");
            int a = int.Parse(parts[0]);
            int b = parts.Length > 1 ? int.Parse(parts[1]) : a + 1;
            b = Math.Clamp(b, 0, bytes.Length); a = Math.Clamp(a, 0, b);
            value = Convert.ToHexString(bytes[a..b]);
        }
        else value = _vars.TryGet(src, out var v) ? v : src;
        _vars.Set(asn.VariableName, value);
        _log.Add($"Assign {asn.VariableName} = {value}");
    }

    private void RunLoop(LoopStep lp)
    {
        if (lp.Kind == LoopKind.PollUntil)
        {
            for (int n = 0; n < lp.MaxIterations; n++)
            {
                Execute(lp.Body);
                if (lp.Until is not null && EvaluateCondition(lp.Until)) { _log.Add($"Loop poll complete after {n + 1} iteration(s)"); return; }
            }
            throw new FlowException("Poll loop exceeded max iterations.");
        }
        Execute(lp.Body); // ForEachEcu: single pass in the core slice
    }

    private void RunIf(IfStep iff)
    {
        bool ok = EvaluateCondition(iff.Condition);
        _log.Add($"If [{iff.Description}] -> {ok}");
        Execute(ok ? iff.Then : iff.Else);
    }

    private bool EvaluateCondition(ConditionGroup group)
    {
        if (group.Conditions.Count == 0) return true;
        bool acc = EvaluateOne(group.Conditions[0]);
        for (int k = 0; k < group.Conditions.Count - 1; k++)   // trailing ConnectSign never read (quirk 1)
        {
            bool next = EvaluateOne(group.Conditions[k + 1]);
            acc = group.Conditions[k].Connect switch
            {
                ConnectSign.Or => acc || next,
                _ => acc && next
            };
        }
        return acc;
    }

    private bool EvaluateOne(OneCondition c)
    {
        string l = _vars.Resolve(c.Left), r = _vars.Resolve(c.Right);
        bool numeric = long.TryParse(l, out var ln) & long.TryParse(r, out var rn);
        return c.Op switch
        {
            OpSign.Equal => numeric ? ln == rn : string.Equals(l, r, StringComparison.OrdinalIgnoreCase),
            OpSign.NotEqual => numeric ? ln != rn : !string.Equals(l, r, StringComparison.OrdinalIgnoreCase),
            OpSign.Greater => numeric && ln > rn,
            OpSign.Less => numeric && ln < rn,
            OpSign.GreaterOrEqual => numeric && ln >= rn,
            OpSign.LessOrEqual => numeric && ln <= rn,
            _ => false
        };
    }

    private static bool IsBareAcceptance(IfStep s) =>
        s.Then.Count == 0 && s.Else.Count == 0 &&
        s.Condition.Conditions.Count == 1 && s.Condition.Conditions[0].Op == OpSign.Equal;

    private static bool SameSingleLeft(IfStep s, ref Operand? left)
    {
        var l = s.Condition.Conditions[0].Left;
        if (left is null) { left = l; return true; }
        return left.IsVariable == l.IsVariable && string.Equals(left.Value, l.Value, StringComparison.OrdinalIgnoreCase);
    }

    private void EvaluateAcceptanceGate(Operand left, HashSet<string> accepted)
    {
        string val = _vars.Resolve(left);
        bool ok = accepted.Contains(val)
               || (left.IsVariable && left.Value.Equals("ResponseStatus", StringComparison.OrdinalIgnoreCase)
                   && _semantics.AcceptedResponseStatus.Contains(val));
        _log.Add($"AcceptanceGate {left.Value} in {{{string.Join(",", accepted)}}} (value={val}) -> {ok}");
        if (!ok) throw new FlowException($"Acceptance failed: {left.Value}={val} not in accepted set {{{string.Join(",", accepted)}}}.");
    }

    private byte[] BuildRequest(UdsRequestSpec spec)
    {
        var bytes = new List<byte> { spec.ServiceId };
        if (spec.SubFunction.HasValue) bytes.Add(spec.SubFunction.Value);
        foreach (var op in spec.Data) bytes.AddRange(HexToBytes(_vars.Resolve(op)));
        return bytes.ToArray();
    }

    private static IEnumerable<byte> HexToBytes(string v)
    {
        v = v.Trim();
        if (v.Length == 0) yield break;
        if (v.Length % 2 != 0) v = "0" + v;
        for (int k = 0; k < v.Length; k += 2) yield return Convert.ToByte(v.Substring(k, 2), 16);
    }
}
