using Rox.Core;
using Xunit;

public class CoreTests
{
    [Theory]
    [InlineData(Nrc.ResponsePending, NrcAction.WaitAndRepoll)]
    [InlineData(Nrc.SecurityAccessDenied, NrcAction.Reauthenticate)]
    [InlineData(Nrc.ExceededNumberOfAttempts, NrcAction.Lockout)]
    [InlineData(Nrc.WrongBlockSequenceCounter, NrcAction.AbortTransfer)]
    public void Nrc_maps_to_recommended_action(byte nrc, NrcAction expected)
        => Assert.Equal(expected, Nrc.RecommendedAction(nrc));

    [Fact]
    public void Dtc_decodes_p0301_and_status_bits()
    {
        Assert.Equal("P0301", DtcDecoder.ToJ2012(0x03, 0x01));
        var dtc = new Dtc("P0301", new byte[] { 0x03, 0x01, 0x00 }, 0x08);
        Assert.True(dtc.Confirmed);
        Assert.False(dtc.Pending);
        Assert.Equal("Confirmed", dtc.StatusText);
    }

    [Fact]
    public void Parse_read_dtc_by_status_mask_returns_records()
    {
        byte[] resp = { 0x59, 0x02, 0xFF, 0x03, 0x01, 0x00, 0x08, 0xC1, 0x23, 0x00, 0x04 };
        var dtcs = DtcDecoder.ParseReadDtcByStatusMask(resp);
        Assert.Equal(2, dtcs.Count);
        Assert.Equal("P0301", dtcs[0].Code);
        Assert.True(dtcs[0].Confirmed);
        Assert.True(dtcs[1].Pending);
    }

    [Fact]
    public void UdsResponse_detects_negative_and_pending()
    {
        var neg = UdsResponse.Parse(new byte[] { 0x7F, 0x27, 0x78 });
        Assert.True(neg.IsNegative);
        Assert.True(neg.IsPending);
        Assert.Equal(0x27, neg.RequestSid);

        var pos = UdsResponse.Parse(new byte[] { 0x67, 0x01, 0xAA });
        Assert.True(pos.IsPositive);
    }
}
