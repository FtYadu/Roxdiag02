namespace Rox.Core;

public enum NrcAction
{
    Abort, RetryAfterPrecondition, RestartSequence, Reauthenticate,
    CheckSecurityModule, Lockout, Wait, WaitAndRepoll, ChangeSessionRetry, AbortTransfer
}

/// <summary>Negative Response Code table + decoding (format 7F &lt;SID&gt; &lt;NRC&gt;).</summary>
public static class Nrc
{
    public const byte GeneralReject = 0x10;
    public const byte ServiceNotSupported = 0x11;
    public const byte SubFunctionNotSupported = 0x12;
    public const byte IncorrectMessageLengthOrInvalidFormat = 0x13;
    public const byte ConditionsNotCorrect = 0x22;
    public const byte RequestSequenceError = 0x24;
    public const byte RequestOutOfRange = 0x31;
    public const byte SecurityAccessDenied = 0x33;
    public const byte InvalidKey = 0x35;
    public const byte ExceededNumberOfAttempts = 0x36;
    public const byte RequiredTimeDelayNotExpired = 0x37;
    public const byte UploadDownloadNotAccepted = 0x70;
    public const byte GeneralProgrammingFailure = 0x72;
    public const byte WrongBlockSequenceCounter = 0x73;
    public const byte ResponsePending = 0x78;
    public const byte SubFunctionNotSupportedInActiveSession = 0x7E;
    public const byte ServiceNotSupportedInActiveSession = 0x7F;

    public static string Describe(byte nrc) => nrc switch
    {
        GeneralReject => "General reject",
        ServiceNotSupported => "Service not supported",
        SubFunctionNotSupported => "Sub-function not supported",
        IncorrectMessageLengthOrInvalidFormat => "Incorrect message length or invalid format",
        ConditionsNotCorrect => "Conditions not correct",
        RequestSequenceError => "Request sequence error",
        RequestOutOfRange => "Request out of range",
        SecurityAccessDenied => "Security access denied",
        InvalidKey => "Invalid key",
        ExceededNumberOfAttempts => "Exceeded number of attempts (locked)",
        RequiredTimeDelayNotExpired => "Required time delay not expired",
        UploadDownloadNotAccepted => "Upload/download not accepted",
        GeneralProgrammingFailure => "General programming failure",
        WrongBlockSequenceCounter => "Wrong block sequence counter",
        ResponsePending => "Response pending",
        SubFunctionNotSupportedInActiveSession => "Sub-function not supported in active session",
        ServiceNotSupportedInActiveSession => "Service not supported in active session",
        _ => $"Unknown NRC 0x{nrc:X2}"
    };

    public static NrcAction RecommendedAction(byte nrc) => nrc switch
    {
        ConditionsNotCorrect => NrcAction.RetryAfterPrecondition,
        RequestSequenceError => NrcAction.RestartSequence,
        SecurityAccessDenied => NrcAction.Reauthenticate,
        InvalidKey => NrcAction.CheckSecurityModule,
        ExceededNumberOfAttempts => NrcAction.Lockout,
        RequiredTimeDelayNotExpired => NrcAction.Wait,
        WrongBlockSequenceCounter => NrcAction.AbortTransfer,
        ResponsePending => NrcAction.WaitAndRepoll,
        SubFunctionNotSupportedInActiveSession or ServiceNotSupportedInActiveSession => NrcAction.ChangeSessionRetry,
        _ => NrcAction.Abort
    };
}
