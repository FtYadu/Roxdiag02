using Rox.FlowEngine;
using Rox.Simulator;
using Xunit;

public class FlowEngineTests
{
    // Quirk 1: OR chain with a trailing ConnectSign that must be ignored.
    [Fact]
    public void Or_chain_true_and_trailing_connect_ignored()
    {
        var vars = new VariableStore();
        vars.Set("x", "5");
        var group = new ConditionGroup
        {
            Conditions =
            {
                new OneCondition { Left = Operand.Var("x"), Op = OpSign.Equal, Right = Operand.Literal("9"), Connect = ConnectSign.Or },
                new OneCondition { Left = Operand.Var("x"), Op = OpSign.Equal, Right = Operand.Literal("5"), Connect = ConnectSign.Or } // trailing OR is a no-op
            }
        };
        var iff = new IfStep { Condition = group, Then = { new UserPromptStep { Message = "hit" } } };
        var proc = new FlowProcess { Name = "q1", Steps = { iff } };
        var interp = new FlowInterpreter(new EcuSimulator(), vars: vars);
        interp.Run(proc); // false OR true => true => Then executes without throwing
        Assert.Contains(interp.Log, l => l.Contains("If [] -> True"));
    }

    // Quirk 2: consecutive bare acceptance-If steps form an OR-set {2,3}.
    [Fact]
    public void Consecutive_accept_ifs_pass_for_2_and_3_and_reject_5()
    {
        static FlowProcess GateProc() => new()
        {
            Name = "q2",
            Steps =
            {
                Bare("ResponseStatus", "2"),
                Bare("ResponseStatus", "3"),
            }
        };

        var okVars = new VariableStore(); okVars.Set("ResponseStatus", "2");
        new FlowInterpreter(new EcuSimulator(), vars: okVars).Run(GateProc()); // no throw

        var badVars = new VariableStore(); badVars.Set("ResponseStatus", "5");
        var interp = new FlowInterpreter(new EcuSimulator(), vars: badVars);
        var ex = Assert.Throws<FlowException>(() => interp.Run(GateProc()));
        Assert.Contains("Acceptance failed", ex.Message);
    }

    private static IfStep Bare(string leftVar, string literal) => new()
    {
        Condition = new ConditionGroup
        {
            Conditions = { new OneCondition { Left = Operand.Var(leftVar), Op = OpSign.Equal, Right = Operand.Literal(literal), Connect = ConnectSign.Or } }
        }
    };

    // End-to-end: parse the flow XML and run Add Key against the simulator; key count must increment.
    [Fact]
    public void AddKey_flow_increments_key_count_against_simulator()
    {
        const string flowXml = @"<FlowConfiguration><Processes><Process Name='Add Key'>
          <ChildStep><EcuService Ecu='IMMO'>
            <Request Service='0x10' Sub='0x03'/>
            <ChildStep><If Description='null'><Condition><OneCondition>
              <LeftValue IsVariable='true' Value='ResponseStatus'/><OpSign Value='Equal'/>
              <RightValue IsVariable='false' Value='2'/><ConnectSign Value='OR'/>
            </OneCondition></Condition></If></ChildStep>
          </EcuService></ChildStep>
          <SecurityAccess Ecu='IMMO' SeedSub='0x01' KeySub='0x02'/>
          <EcuService Ecu='IMMO'><Request Service='0x22'><Data IsVariable='false' Value='F18C'/></Request>
            <ChildStep><Assign Var='before' From='response[3..4]'/></ChildStep></EcuService>
          <EcuService Ecu='IMMO'><Request Service='0x31' Sub='0x01'><Data IsVariable='false' Value='0201'/></Request>
            <ChildStep>
              <If Description='null'><Condition><OneCondition><LeftValue IsVariable='true' Value='ResponseStatus'/><OpSign Value='Equal'/><RightValue IsVariable='false' Value='2'/><ConnectSign Value='OR'/></OneCondition></Condition></If>
              <If Description='null'><Condition><OneCondition><LeftValue IsVariable='true' Value='ResponseStatus'/><OpSign Value='Equal'/><RightValue IsVariable='false' Value='3'/><ConnectSign Value='OR'/></OneCondition></Condition></If>
            </ChildStep></EcuService>
          <EcuService Ecu='IMMO'><Request Service='0x22'><Data IsVariable='false' Value='F18C'/></Request>
            <ChildStep><Assign Var='after' From='response[3..4]'/></ChildStep></EcuService>
        </Process></Processes></FlowConfiguration>";

        var sim = new EcuSimulator();
        var interp = new FlowInterpreter(sim, new TestSecurityProvider());
        interp.Run(FlowParser.Parse(flowXml).Processes[0]);

        Assert.True(sim.SecurityGranted);
        Assert.Equal(3, sim.KeyCount);
        interp.Variables.TryGet("before", out var before);
        interp.Variables.TryGet("after", out var after);
        Assert.Equal("02", before);
        Assert.Equal("03", after);
    }

    // Security must fail cleanly when the supplied key is wrong.
    [Fact]
    public void SecurityAccess_denied_throws_flow_exception()
    {
        var badProvider = new WrongKeyProvider();
        var proc = new FlowProcess { Name = "sec", Steps = { new SecurityAccessStep { Ecu = "IMMO" } } };
        var interp = new FlowInterpreter(new EcuSimulator(), badProvider);
        Assert.Throws<FlowException>(() => interp.Run(proc));
    }

    private sealed class WrongKeyProvider : ISecurityProvider
    {
        public byte[] ComputeKey(byte[] seed, int length) => new byte[] { 0x00, 0x00, 0x00, 0x00 };
    }
}
