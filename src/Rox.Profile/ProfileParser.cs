using System.Xml.Linq;

namespace Rox.Profile;

/// <summary>Parses the R11_Oversea ETSData vehicle profile (buses/pinout).</summary>
public static class ProfileParser
{
    public static VehicleProfile Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var vehicle = doc.Root?.Element("Vehicle") ?? throw new FormatException("Missing <Vehicle> element");
        var buses = new List<BusDefinition>();
        foreach (var bus in vehicle.Element("Buses")?.Elements("Bus") ?? Enumerable.Empty<XElement>())
            buses.Add(ParseBus(bus));
        return new VehicleProfile { Name = (string?)vehicle.Attribute("Name") ?? "", Buses = buses };
    }

    private static BusDefinition ParseBus(XElement bus)
    {
        string busType = (string?)bus.Element("BusType") ?? "";
        var kind = busType.Equals("CANBUS", StringComparison.OrdinalIgnoreCase) ? BusKind.Can
                 : busType.Equals("Ethernet", StringComparison.OrdinalIgnoreCase) ? BusKind.Ethernet
                 : BusKind.Unknown;

        int? I(string n) => int.TryParse((string?)bus.Element(n), out var v) ? v : null;

        return new BusDefinition
        {
            Kind = kind,
            Name = (string?)bus.Element("Name") ?? "",
            Baudrate = I("Baudrate") ?? 0,
            NetworkType = I("NetworkType") ?? 0,
            IsUse = bool.TryParse((string?)bus.Element("IsUse"), out var u) && u,
            HPin = I("HPin"), LPin = I("LPin"),
            RHPin = I("RHPin"), RLPin = I("RLPin"), THPin = I("THPin"), TLPin = I("TLPin"), APin = I("APin"),
            IpVersion = (string?)bus.Element("IPVersion"), ProtocolVersion = I("ProtocolVersion"),
        };
    }
}
