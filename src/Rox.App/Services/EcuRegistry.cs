namespace Rox.App.Services;

public sealed record EcuInfo(string Name, string Domain, string Description, ushort LogicalAddress, bool Doip)
{
    public string TransportLabel => Doip ? "DoIP" : "CAN";
}

/// <summary>
/// The R11_Oversea ECU map (FR-01.2/01.4), matching the vehicle's full-scan layout. Against the
/// simulator every ECU answers; on a real vehicle this is discovered from the ECU folder tree. Logical
/// addresses here are illustrative for DoIP routing until the real data package supplies them.
/// </summary>
public static class EcuRegistry
{
    public static IReadOnlyList<EcuInfo> R11Oversea { get; } = new List<EcuInfo>
    {
        // Body (车身)
        new("CCU", "Body", "Central Computing Unit", 0x1001, true),
        new("IBCM", "Body", "Integrated Body Control Module", 0x1002, false),
        new("AVAS", "Body", "Acoustic Vehicle Alerting System", 0x1003, false),
        new("WCM_L", "Body", "Left Wireless Charging Module", 0x1004, false),
        new("WCM_R", "Body", "Right Wireless Charging Module", 0x1005, false),
        new("LHCM", "Body", "Left Headlamp Control Module", 0x1006, false),
        new("RHCM", "Body", "Right Headlamp Control Module", 0x1007, false),
        new("TLCM_L", "Body", "Left Tail-Lamp Control Module", 0x1008, false),
        new("TLCM_R", "Body", "Right Tail-Lamp Control Module", 0x1009, false),
        new("SCM_L", "Body", "Left Seat Control Module", 0x100A, false),
        new("SCM_R", "Body", "Right Seat Control Module", 0x100B, false),
        new("ASCM_RL", "Body", "Rear-Left Air-Suspension Seat Control", 0x100C, false),
        new("ASCM_RR", "Body", "Rear-Right Air-Suspension Seat Control", 0x100D, false),
        // Chassis (底盘)
        new("EPS_FD", "Chassis", "Electric Power Steering (redundant, CAN-FD)", 0x2001, false),
        new("EPS", "Chassis", "Electric Power Steering (CAN)", 0x2002, false),
        new("ESC", "Chassis", "Electronic Stability Control", 0x2003, false),
        new("IB", "Chassis", "Integrated Brake (electric brake booster)", 0x2004, false),
        new("CDS", "Chassis", "Continuous Damping / Air-suspension control", 0x2005, false),
        new("ACU", "Chassis", "Airbag Control Unit", 0x2006, false),
        // Cockpit (座舱)
        new("IDCU", "Cockpit", "Cockpit Domain Controller", 0x3001, true),
        new("AMP", "Cockpit", "Amplifier", 0x3002, false),
        new("TBOX", "Cockpit", "Telematics / Connectivity Module", 0x3003, true),
        new("BTM", "Cockpit", "Bluetooth Master Module", 0x3004, false),
        // ADAS / Intelligent driving (智驾)
        new("ADCU_MCU", "ADAS", "Autonomous Driving Control Unit — MCU", 0x4001, true),
        new("ADCU_SOC", "ADAS", "Autonomous Driving Control Unit — SoC", 0x4002, true),
        new("SRR_FL", "ADAS", "Front-Left Corner Radar", 0x4003, false),
        new("SRR_FR", "ADAS", "Front-Right Corner Radar", 0x4004, false),
        new("SRR_RL", "ADAS", "Rear-Left Corner Radar", 0x4005, false),
        new("SRR_RR", "ADAS", "Rear-Right Corner Radar", 0x4006, false),
        new("MRR_F", "ADAS", "Front Mid-Range Radar", 0x4007, false),
        // Powertrain (动力)
        new("MDCU", "Powertrain", "Powertrain Domain Controller", 0x5001, true),
        new("EMS", "Powertrain", "Engine/Energy Management System", 0x5002, false),
        new("GCU", "Powertrain", "Generator Control Unit", 0x5003, false),
        new("FMC", "Powertrain", "Front Motor Controller", 0x5004, false),
        new("RMC", "Powertrain", "Rear Motor Controller", 0x5005, false),
        new("IBS", "Powertrain", "Intelligent Battery Sensor / management", 0x5006, false),
        new("ATC", "Powertrain", "Automatic Temperature/Thermal Control", 0x5007, false),
        new("SCU", "Powertrain", "Seat-return / Body Control Unit", 0x5008, false),
        new("BMS", "Powertrain", "High-Voltage Battery Management System", 0x5009, true),
        new("PMS", "Powertrain", "High-Voltage Energy Management System", 0x500A, true),
    };
}
