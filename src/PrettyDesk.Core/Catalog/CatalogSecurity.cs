using System.Security.Cryptography;

namespace PrettyDesk.Core.Catalog;

/// <summary>
/// ECDSA P-256 / SHA-256 signing helpers (FR-CON-2). Signatures are the 64-byte IEEE P1363 (r‖s) form, stored as base64
/// in <c>catalog.json.sig</c>. Public keys are SubjectPublicKeyInfo DER; private keys PKCS#8 DER.
/// </summary>
public static class CatalogSigning
{
    public static (byte[] PrivatePkcs8, byte[] PublicSpki) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKey(), key.ExportSubjectPublicKeyInfo());
    }

    public static byte[] Sign(ReadOnlySpan<byte> data, ReadOnlySpan<byte> privatePkcs8)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privatePkcs8, out _);
        return key.SignData(data, HashAlgorithmName.SHA256);
    }
}

/// <summary>Verifies a catalog against a list of trusted public keys (key rotation: any key may match).</summary>
public sealed class CatalogSignatureVerifier
{
    private readonly IReadOnlyList<byte[]> _publicKeys;

    public CatalogSignatureVerifier(IReadOnlyList<byte[]> publicKeysSpki) => _publicKeys = publicKeysSpki;

    public bool HasKeys => _publicKeys.Count > 0;

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64)
        {
            return false;
        }

        foreach (var spki in _publicKeys)
        {
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(spki, out _);
                if (key.VerifyData(data, signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // A malformed trusted key must not break verification against the others.
            }
        }

        return false;
    }

    /// <summary>Verifies a base64 signature file's text.</summary>
    public bool VerifyBase64(ReadOnlySpan<byte> data, string signatureBase64)
    {
        try
        {
            return Verify(data, Convert.FromBase64String(signatureBase64.Trim()));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Public keys compiled into the app (FR-CON-2: "the app accepts a list of public keys").
/// OWNER-DECISION: generate a production key pair with <c>catalog-sign keygen</c>, keep the private key in the CI secret
/// store (or offline), and paste the base64 public key here. Until then remote catalogs are rejected (fail closed) and
/// the app runs on its bundled snapshot.
/// </summary>
public static class TrustedKeys
{
    public static IReadOnlyList<string> PublicKeysBase64 { get; } = [];

    public static CatalogSignatureVerifier CreateVerifier() =>
        new(PublicKeysBase64.Select(Convert.FromBase64String).ToList());
}
