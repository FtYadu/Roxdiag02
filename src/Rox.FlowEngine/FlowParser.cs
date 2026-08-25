using System.Xml.Linq;

namespace Rox.FlowEngine;

/// <summary>
/// Parses the OEM FlowConfiguration XML into an AST.
/// The condition grammar (LeftValue/OpSign/RightValue/ConnectSign, If@Description) is the
/// OBSERVED format. &lt;Request&gt; is the RECONSTRUCTED request-side adapter point (schema 5.2):
/// swap its element names here when a complete flow file confirms them.
/// </summary>
public static class FlowParser
{
    public static FlowConfiguration Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("Empty document");
        var processesEl = root.Name.LocalName == "FlowConfiguration"
            ? root.Element("Processes")
            : root.Descendants("Processes").FirstOrDefault();
        var config = new FlowConfiguration();
        if (processesEl is null) return config;
        foreach (var p in processesEl.Elements("Process"))
            config.Processes.Add(new FlowProcess
            {
                Name = (string?)p.Attribute("Name") ?? (string?)p.Attribute("Description"),
                Steps = ParseSteps(p)
            });
        return config;
    }

    private static List<IStep> ParseSteps(XElement parent)
    {
        var steps = new List<IStep>();
        foreach (var el in parent.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "ChildStep": steps.AddRange(ParseSteps(el)); break; // transparent container
                case "EcuService": steps.Add(ParseEcuService(el)); break;
                case "If": steps.Add(ParseIf(el)); break;
                case "SecurityAccess": steps.Add(ParseSecurityAccess(el)); break;
                case "UserPrompt":
                    steps.Add(new UserPromptStep
                    {
                        Message = (string?)el.Attribute("Message") ?? "",
                        TimeoutSeconds = ParseInt(el.Attribute("Timeout")) ?? 0
                    });
                    break;
                case "Assign":
                    steps.Add(new AssignStep
                    {
                        VariableName = (string?)el.Attribute("Var") ?? "",
                        SourceExpression = (string?)el.Attribute("From") ?? ""
                    });
                    break;
                case "Loop": steps.Add(ParseLoop(el)); break;
                default: if (el.HasElements) steps.AddRange(ParseSteps(el)); break;
            }
        }
        return steps;
    }

    private static EcuServiceStep ParseEcuService(XElement el)
    {
        UdsRequestSpec? req = null;
        var reqEl = el.Element("Request");
        if (reqEl is not null)
            req = new UdsRequestSpec
            {
                ServiceId = ParseByte(reqEl.Attribute("Service")) ?? 0,
                SubFunction = ParseByte(reqEl.Attribute("Sub")),
                Data = reqEl.Elements("Data").Select(d => new Operand
                {
                    IsVariable = ParseBool(d.Attribute("IsVariable")),
                    Value = (string?)d.Attribute("Value") ?? ""
                }).ToList()
            };

        var responseParent = new XElement("resp", el.Elements().Where(e => e.Name.LocalName != "Request"));
        return new EcuServiceStep { Ecu = (string?)el.Attribute("Ecu"), Request = req, ResponseSteps = ParseSteps(responseParent) };
    }

    private static IfStep ParseIf(XElement el)
    {
        var thenEl = el.Element("Then");
        var elseEl = el.Element("Else");
        return new IfStep
        {
            Description = (string?)el.Attribute("Description"),
            Condition = ParseCondition(el.Element("Condition")),
            Then = thenEl is not null ? ParseSteps(thenEl) : new(),
            Else = elseEl is not null ? ParseSteps(elseEl) : new()
        };
    }

    private static ConditionGroup ParseCondition(XElement? condEl)
    {
        var group = new ConditionGroup();
        if (condEl is null) return group;
        foreach (var oc in condEl.Elements("OneCondition"))
            group.Conditions.Add(new OneCondition
            {
                Left = ParseValueOperand(oc.Element("LeftValue")),
                Op = ParseOp((string?)oc.Element("OpSign")?.Attribute("Value")),
                Right = ParseValueOperand(oc.Element("RightValue")),
                Connect = ParseConnect((string?)oc.Element("ConnectSign")?.Attribute("Value"))
            });
        return group;
    }

    private static Operand ParseValueOperand(XElement? el) =>
        el is null ? Operand.Literal("")
                   : new Operand { IsVariable = ParseBool(el.Attribute("IsVariable")), Value = (string?)el.Attribute("Value") ?? "" };

    private static SecurityAccessStep ParseSecurityAccess(XElement el) => new()
    {
        Ecu = (string?)el.Attribute("Ecu"),
        RequestSeedSub = ParseByte(el.Attribute("SeedSub")) ?? 0x01,
        SendKeySub = ParseByte(el.Attribute("KeySub")) ?? 0x02
    };

    private static LoopStep ParseLoop(XElement el)
    {
        var kind = ((string?)el.Attribute("Kind") ?? "PollUntil")
            .Equals("ForEachEcu", StringComparison.OrdinalIgnoreCase) ? LoopKind.ForEachEcu : LoopKind.PollUntil;
        var untilEl = el.Element("Until");
        return new LoopStep
        {
            Kind = kind,
            MaxIterations = ParseInt(el.Attribute("Max")) ?? 100,
            Until = untilEl is not null ? ParseCondition(untilEl.Element("Condition") ?? untilEl) : null,
            Body = ParseSteps(el.Element("Body") ?? new XElement("Body"))
        };
    }

    private static OpSign ParseOp(string? v) => v switch
    {
        "NotEqual" => OpSign.NotEqual, "Greater" => OpSign.Greater, "Less" => OpSign.Less,
        "GreaterOrEqual" => OpSign.GreaterOrEqual, "LessOrEqual" => OpSign.LessOrEqual, _ => OpSign.Equal
    };
    private static ConnectSign ParseConnect(string? v) => v switch
    { "OR" => ConnectSign.Or, "AND" => ConnectSign.And, _ => ConnectSign.None };

    private static bool ParseBool(XAttribute? a) => bool.TryParse((string?)a, out var b) && b;
    private static int? ParseInt(XAttribute? a) => int.TryParse((string?)a, out var v) ? v : null;
    private static byte? ParseByte(XAttribute? a)
    {
        var s = ((string?)a)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToByte(s[2..], 16);
        return byte.TryParse(s, out var v) ? v : Convert.ToByte(s, 16);
    }
}
