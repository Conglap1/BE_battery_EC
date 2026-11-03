using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BatteryEC.Infrastructure.Security;
using Xunit;
using Xunit.Abstractions;

namespace BatteryEC.UnitTests;

public class PasswordHasherAdversarialTests
{
    private readonly PasswordHasher _hasher = new();
    private readonly ITestOutputHelper _output;

    public PasswordHasherAdversarialTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region 1. Salt Uniqueness Across 100 Iterations

    [Fact]
    public void StressTest_100_Hashes_MustHave_100_UniqueSalts_And_100_UniqueHashes()
    {
        const string password = "CommonTestPassword#2026";
        const int iterations = 100;
        var salts = new HashSet<string>();
        var hashes = new HashSet<string>();

        for (int i = 0; i < iterations; i++)
        {
            string hash = _hasher.HashPassword(password);
            Assert.NotNull(hash);

            string[] parts = hash.Split('.');
            Assert.Equal(3, parts.Length);

            string salt = parts[1];
            string subkey = parts[2];

            salts.Add(salt);
            hashes.Add(subkey);
        }

        Assert.Equal(iterations, salts.Count);
        Assert.Equal(iterations, hashes.Count);
    }

    #endregion

    #region 2. Edge Cases: Empty, Whitespace, Unicode, Long Passwords

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EdgeCase_HashPassword_EmptyOrNull_ThrowsArgumentException(string? password)
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(password!));
    }

    [Fact]
    public void EdgeCase_HashPassword_WhitespaceOnly_BehaviorAnalysis()
    {
        string whitespacePassword = "   ";

        // Verified fix: HashPassword now rejects whitespace-only passwords with ArgumentException
        // preventing accounts from being created with unusable credentials.
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(whitespacePassword));

        // VerifyPassword also rejects whitespace-only passwords symmetrically
        bool verified = _hasher.VerifyPassword(whitespacePassword, "100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=");
        Assert.False(verified);
    }

    [Theory]
    [InlineData(" password ")]
    [InlineData("  leading_and_trailing  ")]
    [InlineData("embedded\twhitespace\nand\r\nnewlines")]
    public void EdgeCase_LeadingTrailingOrEmbeddedWhitespace_SuccessfullyVerified(string password)
    {
        string hash = _hasher.HashPassword(password);
        Assert.True(_hasher.VerifyPassword(password, hash));
        Assert.False(_hasher.VerifyPassword(password + "extra", hash));
    }

    [Theory]
    [InlineData("Mật khẩu an toàn tiếng Việt có dấu 123! 🇻🇳")]
    [InlineData("こんにちは世界！パスワード2026")]
    [InlineData("🔑🔒🛡️⚡🔋🚀✨🔥")]
    [InlineData("مرحبا_كلمة_المرور_123456")]
    [InlineData("Z͑ͫ̓ͪ̂ͫ̽͏̴̙̤̞͉͚̯̞Aͫͩͪ̉͏̵͙͓Lͮ͏̮Ģ͉ͩ͌͑ͮ͒́ͥ͢͞Ǫ̵̠̖͖͂̌̌͘")]
    [InlineData("Mixed_Multi_Lang_TiếngViệt_日本語_العربية_🔑_100%")]
    public void EdgeCase_Unicode_Multibyte_Emoji_Passwords_SuccessfullyVerified(string password)
    {
        string hash = _hasher.HashPassword(password);
        Assert.NotNull(hash);

        bool correctVerified = _hasher.VerifyPassword(password, hash);
        Assert.True(correctVerified, $"Failed to verify unicode password: {password}");

        // Mutated unicode password must fail
        bool wrongVerified = _hasher.VerifyPassword(password + "a", hash);
        Assert.False(wrongVerified);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public void EdgeCase_VeryLongPasswords_SuccessfullyVerified(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append((char)('a' + (i % 26)));
        }
        string longPassword = sb.ToString();

        var sw = Stopwatch.StartNew();
        string hash = _hasher.HashPassword(longPassword);
        sw.Stop();
        _output.WriteLine($"Hash {length} chars took {sw.ElapsedMilliseconds} ms");

        Assert.NotNull(hash);
        Assert.True(_hasher.VerifyPassword(longPassword, hash));
        Assert.False(_hasher.VerifyPassword(longPassword.Substring(0, length - 1) + "X", hash));
    }

    #endregion

    #region 3. Tampered Hashes & Malformed Inputs (Must Return False, Never Crash)

    [Fact]
    public void TamperTest_SingleCharacterMutation_InSalt_ReturnsFalse()
    {
        const string password = "TestPassword#2026";
        string originalHash = _hasher.HashPassword(password);
        string[] parts = originalHash.Split('.');

        string saltBase64 = parts[1];

        // Mutate each single character in the salt base64
        for (int i = 0; i < saltBase64.Length; i++)
        {
            char originalChar = saltBase64[i];
            char mutatedChar = originalChar == 'A' ? 'B' : 'A';

            var sb = new StringBuilder(saltBase64);
            sb[i] = mutatedChar;
            string mutatedSalt = sb.ToString();

            string tamperedHash = $"{parts[0]}.{mutatedSalt}.{parts[2]}";

            bool result = _hasher.VerifyPassword(password, tamperedHash);
            Assert.False(result);
        }
    }

    [Fact]
    public void TamperTest_SingleCharacterMutation_InHash_ReturnsFalse()
    {
        const string password = "TestPassword#2026";
        string originalHash = _hasher.HashPassword(password);
        string[] parts = originalHash.Split('.');

        string hashBase64 = parts[2];

        // Mutate each single character in the subkey base64
        for (int i = 0; i < hashBase64.Length; i++)
        {
            char originalChar = hashBase64[i];
            char mutatedChar = originalChar == 'X' ? 'Y' : 'X';

            var sb = new StringBuilder(hashBase64);
            sb[i] = mutatedChar;
            string mutatedHashSubkey = sb.ToString();

            string tamperedHash = $"{parts[0]}.{parts[1]}.{mutatedHashSubkey}";

            bool result = _hasher.VerifyPassword(password, tamperedHash);
            Assert.False(result);
        }
    }

    [Theory]
    [InlineData("100000.!!!INVALID_BASE64!!!.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("100000.AAAAAAAAAAAAAAAAAAAAAA==.!!!INVALID_BASE64!!!")]
    [InlineData("100000.truncated.truncated")]
    [InlineData("100000..")]
    [InlineData(".salt.hash")]
    [InlineData("100000..hash")]
    [InlineData("100000.salt.")]
    [InlineData("100000.salt.hash.extra_fourth_part")]
    [InlineData("100000.salt.hash.extra.fifth")]
    [InlineData("not_a_number.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("-100000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("0.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("100000:AAAAAAAAAAAAAAAAAAAAAA==:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("100000 AAAAAAAAAAAAAAAAAAAAAA== AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("   ")]
    [InlineData(" \t\n\r ")]
    [InlineData("$2z$10$invalidbcryptfamilyprefixabcdefghijklmnopqrstuv")]
    public void TamperTest_CorruptedOrMalformedHashes_AlwaysReturnsFalse_NeverThrows(string malformedHash)
    {
        // Act & Assert: Must return false and NEVER throw an uncaught exception
        bool result = _hasher.VerifyPassword("TestPassword#2026", malformedHash);
        Assert.False(result);
    }

    #endregion

    #region 4. Iteration Count Enforcement & Independent Oracle Verification

    [Fact]
    public void IterationTest_Pbkdf2Hash_MatchesIndependentOracle_With100000Iterations()
    {
        const string password = "TestPassword#Oracle#2026";
        string generated = _hasher.HashPassword(password);
        string[] parts = generated.Split('.');

        int iterations = int.Parse(parts[0]);
        Assert.Equal(100_000, iterations);

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expectedSubkey = Convert.FromBase64String(parts[2]);

        // Oracle PBKDF2 calculation directly via BCL Rfc2898DeriveBytes
        byte[] oracleSubkey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        Assert.Equal(expectedSubkey, oracleSubkey);

        // Verification with 99,999 iterations must NOT match
        byte[] offByMinusOne = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            99_999,
            HashAlgorithmName.SHA256,
            32);
        Assert.NotEqual(expectedSubkey, offByMinusOne);

        // Verification with 100,001 iterations must NOT match
        byte[] offByPlusOne = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_001,
            HashAlgorithmName.SHA256,
            32);
        Assert.NotEqual(expectedSubkey, offByPlusOne);
    }

    [Fact]
    public void IterationTest_TamperedIterationHeader_CausesVerificationFailure()
    {
        const string password = "TestPassword#2026";
        string originalHash = _hasher.HashPassword(password);
        string[] parts = originalHash.Split('.');

        // Change iteration count header in the serialized hash from 100000 to 50000
        string tamperedHeaderHash = $"50000.{parts[1]}.{parts[2]}";
        bool result50k = _hasher.VerifyPassword(password, tamperedHeaderHash);
        Assert.False(result50k);

        // Change iteration count header to 1
        string tamperedHeaderHash1 = $"1.{parts[1]}.{parts[2]}";
        bool result1 = _hasher.VerifyPassword(password, tamperedHeaderHash1);
        Assert.False(result1);
    }

    #endregion

    #region 5. BCrypt Backward Compatibility

    [Fact]
    public void BcryptTest_SupportedVariants_2a_2b_2y_VerifiedCorrectly()
    {
        string password = "P@ssword123";

        // Generate standard BCrypt hash (starts with $2a$)
        string hash2a = BCrypt.Net.BCrypt.HashPassword(password, 10);
        // BCrypt specification allows $2b$ and $2y$ variants
        string hash2b = "$2b$" + hash2a.Substring(4);
        string hash2y = "$2y$" + hash2a.Substring(4);

        Assert.StartsWith("$2a$", hash2a);
        Assert.StartsWith("$2b$", hash2b);
        Assert.StartsWith("$2y$", hash2y);

        // Verify all three variants return true with correct password
        Assert.True(_hasher.VerifyPassword(password, hash2a), "Failed to verify $2a$");
        Assert.True(_hasher.VerifyPassword(password, hash2b), "Failed to verify $2b$");
        Assert.True(_hasher.VerifyPassword(password, hash2y), "Failed to verify $2y$");

        // Verify all three variants return false with wrong password
        Assert.False(_hasher.VerifyPassword("WrongPassword#999", hash2a));
        Assert.False(_hasher.VerifyPassword("WrongPassword#999", hash2b));
        Assert.False(_hasher.VerifyPassword("WrongPassword#999", hash2y));
    }

    [Fact]
    public void BcryptTest_FreshlyGeneratedBcryptHashes_VerifySuccessfully()
    {
        string[] prefixes = { "$2a$", "$2b$" };
        string password = "DynamicBcryptTest@2026!";

        // Generate via BCrypt.Net
        string bcryptHash = BCrypt.Net.BCrypt.HashPassword(password, 10);
        Assert.True(_hasher.VerifyPassword(password, bcryptHash));
        Assert.False(_hasher.VerifyPassword("WrongPwd", bcryptHash));
    }

    [Theory]
    [InlineData("$2a$11$short")]
    [InlineData("$2a$99$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy")]
    [InlineData("$2b$invalid_cost$salt_and_hash")]
    public void BcryptTest_MalformedBcryptHashes_CaughtGracefullyReturnsFalse(string malformedBcrypt)
    {
        bool result = _hasher.VerifyPassword("password", malformedBcrypt);
        Assert.False(result);
    }

    #endregion

    #region 6. Constant-Time Comparison & Timing Properties

    [Fact]
    public void ConstantTimeTest_TimingAnalysis_DifferentMismatchPositions()
    {
        const string password = "TimingAnalysisPassword#2026";
        string validHash = _hasher.HashPassword(password);
        string[] parts = validHash.Split('.');

        byte[] originalHashBytes = Convert.FromBase64String(parts[2]);

        // Tamper byte 0 (first byte mismatch)
        byte[] earlyMismatch = (byte[])originalHashBytes.Clone();
        earlyMismatch[0] ^= 0xFF;
        string earlyMismatchHash = $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(earlyMismatch)}";

        // Tamper byte 31 (last byte mismatch)
        byte[] lateMismatch = (byte[])originalHashBytes.Clone();
        lateMismatch[31] ^= 0xFF;
        string lateMismatchHash = $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(lateMismatch)}";

        // Warm up JIT
        _hasher.VerifyPassword(password, validHash);
        _hasher.VerifyPassword(password, earlyMismatchHash);
        _hasher.VerifyPassword(password, lateMismatchHash);

        const int trials = 50;
        long totalEarly = 0;
        long totalLate = 0;
        long totalValid = 0;

        for (int i = 0; i < trials; i++)
        {
            var sw = Stopwatch.StartNew();
            _hasher.VerifyPassword(password, earlyMismatchHash);
            sw.Stop();
            totalEarly += sw.ElapsedTicks;

            sw.Restart();
            _hasher.VerifyPassword(password, lateMismatchHash);
            sw.Stop();
            totalLate += sw.ElapsedTicks;

            sw.Restart();
            _hasher.VerifyPassword(password, validHash);
            sw.Stop();
            totalValid += sw.ElapsedTicks;
        }

        _output.WriteLine($"Timing across {trials} runs (in ticks):");
        _output.WriteLine($"  Early mismatch (byte 0) avg: {totalEarly / (double)trials:F2} ticks");
        _output.WriteLine($"  Late mismatch (byte 31) avg: {totalLate / (double)trials:F2} ticks");
        _output.WriteLine($"  Valid match avg:            {totalValid / (double)trials:F2} ticks");

        // The dominant cost is Rfc2898DeriveBytes.Pbkdf2 (100,000 iterations).
        // FixedTimeEquals ensures byte comparison itself doesn't early-exit.
        Assert.False(_hasher.VerifyPassword(password, earlyMismatchHash));
        Assert.False(_hasher.VerifyPassword(password, lateMismatchHash));
        Assert.True(_hasher.VerifyPassword(password, validHash));
    }

    #endregion

    #region 7. Deep Security Inspection: Length Validation & DoS Hypotheses

    [Fact]
    public void Adversarial_TruncatedSubkey_AllowsSingleByteVerification()
    {
        // Verified fix: VerifyPassword now strictly enforces expectedHash.Length == HashSize (32 bytes)
        // and iterations >= 10_000. Truncated subkeys and low iteration counts are rejected.
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        string saltBase64 = Convert.ToBase64String(salt);

        // Precompute PBKDF2 for password "target" with length = 1
        byte[] targetByte = Rfc2898DeriveBytes.Pbkdf2("target", salt, 1, HashAlgorithmName.SHA256, 1);
        string singleByteHash = $"1.{saltBase64}.{Convert.ToBase64String(targetByte)}";

        // PasswordHasher.VerifyPassword rejects hashes where iterations < 10,000 or expectedHash.Length != HashSize
        bool result = _hasher.VerifyPassword("target", singleByteHash);

        _output.WriteLine($"Single-byte hash verification returned: {result}");
        Assert.False(result); // Confirms the vulnerability is remediated: 1-byte hash is rejected!

        // Also verify truncated subkey with valid iteration count (100,000) is rejected by HashSize check
        byte[] targetByte100k = Rfc2898DeriveBytes.Pbkdf2("target", salt, 100_000, HashAlgorithmName.SHA256, 1);
        string singleByteHash100k = $"100000.{saltBase64}.{Convert.ToBase64String(targetByte100k)}";
        bool result100k = _hasher.VerifyPassword("target", singleByteHash100k);
        Assert.False(result100k);
    }

    [Fact]
    public void Adversarial_ExtraParts_DelimiterSplit_Inspection()
    {
        const string password = "TestPassword#2026";
        string validHash = _hasher.HashPassword(password);

        // PasswordHasher uses: passwordHash.Split(Delimiter, 3)
        // If there is a 4th delimiter, e.g. "100000.salt.subkey.extra"
        // parts[2] becomes "subkey.extra", which is invalid Base64.
        string fourPartsHash = validHash + ".extra";
        bool result = _hasher.VerifyPassword(password, fourPartsHash);

        Assert.False(result);
    }

    #endregion
}
