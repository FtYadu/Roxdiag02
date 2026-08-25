namespace Rox.Core;

/// <summary>UDS (ISO 14229) service identifiers used by ROX.</summary>
public static class UdsServices
{
    public const byte DiagnosticSessionControl = 0x10;
    public const byte EcuReset = 0x11;
    public const byte ClearDiagnosticInformation = 0x14;
    public const byte ReadDtcInformation = 0x19;
    public const byte ReadDataByIdentifier = 0x22;
    public const byte SecurityAccess = 0x27;
    public const byte CommunicationControl = 0x28;
    public const byte WriteDataByIdentifier = 0x2E;
    public const byte InputOutputControlByIdentifier = 0x2F;
    public const byte RoutineControl = 0x31;
    public const byte RequestDownload = 0x34;
    public const byte TransferData = 0x36;
    public const byte RequestTransferExit = 0x37;
    public const byte TesterPresent = 0x3E;
    public const byte ControlDtcSetting = 0x85;

    public const byte PositiveResponseOffset = 0x40;
    public const byte NegativeResponseCode = 0x7F;
}
