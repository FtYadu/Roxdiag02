using Rox.Profile;
using Xunit;

public class ProfileTests
{
    private const string Xml = @"<ETSData><Vehicle Name='R11_Oversea'><Buses>
      <Bus><HPin>6</HPin><LPin>14</LPin><BusType>CANBUS</BusType><Name>CANBUS</Name>
        <Baudrate>500000</Baudrate><IsUse>True</IsUse><NetworkType>0</NetworkType></Bus>
      <Bus><RHPin>3</RHPin><RLPin>11</RLPin><THPin>12</THPin><TLPin>13</TLPin><APin>8</APin>
        <ProtocolVersion>1</ProtocolVersion><IPVersion>IPv4</IPVersion>
        <BusType>Ethernet</BusType><Name>Ethernet</Name><Baudrate>0</Baudrate>
        <IsUse>True</IsUse><NetworkType>10</NetworkType></Bus>
    </Buses></Vehicle></ETSData>";

    [Fact]
    public void Parses_both_buses_with_pins()
    {
        var p = ProfileParser.Parse(Xml);
        Assert.Equal("R11_Oversea", p.Name);
        Assert.Equal(2, p.Buses.Count);

        var can = Assert.Single(p.Buses, b => b.Kind == BusKind.Can);
        Assert.Equal(500000, can.Baudrate);
        Assert.Equal(6, can.HPin);
        Assert.Equal(14, can.LPin);

        var eth = Assert.Single(p.Buses, b => b.Kind == BusKind.Ethernet);
        Assert.Equal(8, eth.APin);
        Assert.Equal("IPv4", eth.IpVersion);
        Assert.Equal(10, eth.NetworkType);
    }
}
