using Rox.Security;
using Xunit;

public class LicenseTests
{
    [Fact]
    public void Missing_key_is_reported_not_thrown()
    {
        Assert.Equal(LicenseState.Missing, LicenseValidator.Validate(null).State);
        Assert.Equal(LicenseState.Missing, LicenseValidator.Validate("  ").State);
    }

    [Fact]
    public void Wellformed_key_validates_and_tampered_key_fails()
    {
        var key = LicenseValidator.Complete("ROX-ABCD-1234-EF56");
        Assert.True(LicenseValidator.Validate(key).IsValid);

        var tampered = key[..^2] + "00";
        Assert.Equal(LicenseState.Invalid, LicenseValidator.Validate(tampered).State);
    }

    [Fact]
    public void Malformed_key_is_invalid()
    {
        Assert.Equal(LicenseState.Invalid, LicenseValidator.Validate("NOTALICENCE").State);
        Assert.Equal(LicenseState.Invalid, LicenseValidator.Validate("ROX-1-2-3-00").State);
    }
}
