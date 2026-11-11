using System.Linq;
using Shared.database;
using Xunit;

namespace Tests
{
    public class DiscordLinksTests
    {
        [Fact]
        public void CodesAreSixEasyToReadCharacters()
        {
            for (var i = 0; i < 200; i++)
            {
                var code = DiscordLinks.NewCode();
                Assert.Equal(DiscordLinks.CodeLength, code.Length);
                Assert.All(code, c => Assert.Contains(c, DiscordLinks.CodeAlphabet));
                Assert.Equal(code, DiscordLinks.CleanCode(code));
            }
            Assert.DoesNotContain(DiscordLinks.CodeAlphabet, c => "ILO01".Contains(c));
            Assert.True(Enumerable.Range(0, 50).Select(_ => DiscordLinks.NewCode()).Distinct().Count() > 45);
        }

        [Fact]
        public void ACodeIsReadAsTyped()
        {
            Assert.Equal("ABC234", DiscordLinks.CleanCode(" abc-234 "));
            Assert.Equal("ABC234", DiscordLinks.CleanCode("ABC 234"));
            Assert.Null(DiscordLinks.CleanCode("ABC23"));
            Assert.Null(DiscordLinks.CleanCode("ABC2345"));
            Assert.Null(DiscordLinks.CleanCode("ABC10O")); // 1, 0 and O are never in a code
            Assert.Null(DiscordLinks.CleanCode("ABC23*"));
            Assert.Null(DiscordLinks.CleanCode(null));
        }

        [Fact]
        public void OnlyTheConfiguredSecretPasses()
        {
            Assert.True(DiscordLinks.SecretMatches("s3cret-value", "s3cret-value"));
            Assert.False(DiscordLinks.SecretMatches("s3cret-value", "s3cret-valuE"));
            Assert.False(DiscordLinks.SecretMatches("s3cret-value", "s3cret"));
            Assert.False(DiscordLinks.SecretMatches("s3cret-value", null));
            // no secret configured: nothing passes, not even an empty one
            Assert.False(DiscordLinks.SecretMatches("", ""));
            Assert.False(DiscordLinks.SecretMatches(null, null));
        }

        [Fact]
        public void DiscordIdsAndNamesAreCleaned()
        {
            Assert.Equal("123456789012345678", DiscordLinks.CleanId(" 123456789012345678 "));
            Assert.Null(DiscordLinks.CleanId("12ab"));
            Assert.Null(DiscordLinks.CleanId(""));
            Assert.Null(DiscordLinks.CleanId(new string('1', 30)));
            Assert.Equal("meapy", DiscordLinks.CleanName(" me\napy<> "));
            Assert.Equal(40, DiscordLinks.CleanName(new string('x', 90)).Length);
        }

        [Fact]
        public void ResetPasswordsPassThePasswordRule()
        {
            var a = DiscordLinks.NewPassword();
            Assert.Equal(12, a.Length);
            Assert.All(a, c => Assert.True(char.IsLetterOrDigit(c)));
            Assert.NotEqual(a, DiscordLinks.NewPassword());
        }
    }
}
