using System.Text;
using PrettyDesk.Core.Catalog;

namespace PrettyDesk.Tools.CatalogSign;

/// <summary>
/// <c>catalog-sign keygen|sign|verify</c> (SPEC §6.2). The private key never lives in the repo: keep it in a CI secret
/// (<c>--key-env</c>) or offline (<c>--key</c> file). Signatures are base64 ECDSA P-256/SHA-256 in IEEE P1363 form.
/// </summary>
public static class CatalogSignCommands
{
    public const string Usage = """
        catalog-sign keygen --out <private-key-file>        Generate a P-256 key pair; prints the public key for TrustedKeys.cs
        catalog-sign sign <catalog.json> (--key <file> | --key-env <VAR>)   Write <catalog.json>.sig
        catalog-sign verify <catalog.json> (--pub <base64> | --pub-file <file>)   Check <catalog.json>.sig
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "keygen" => KeyGen(args[1..], output, error),
                "sign" => Sign(args[1..], output, error),
                "verify" => Verify(args[1..], output, error),
                _ => Fail(error, $"Unknown command '{args[0]}'.\n{Usage}"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(error, ex.Message);
        }
    }

    private static int KeyGen(string[] args, TextWriter output, TextWriter error)
    {
        var path = Option(args, "--out");
        if (path is null)
        {
            return Fail(error, "keygen needs --out <private-key-file>.");
        }

        if (File.Exists(path))
        {
            return Fail(error, $"'{path}' already exists; refusing to overwrite a private key.");
        }

        var (privateKey, publicKey) = CatalogSigning.GenerateKeyPair();
        File.WriteAllText(path, Convert.ToBase64String(privateKey));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        output.WriteLine("Private key written to " + path + " (keep it secret; never commit it).");
        output.WriteLine("Public key (paste into TrustedKeys.PublicKeysBase64):");
        output.WriteLine(Convert.ToBase64String(publicKey));
        return 0;
    }

    private static int Sign(string[] args, TextWriter output, TextWriter error)
    {
        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var keyFile = Option(args, "--key");
        var keyEnv = Option(args, "--key-env");
        if (file is null || (keyFile is null && keyEnv is null))
        {
            return Fail(error, "sign needs a catalog file and --key <file> or --key-env <VAR>.");
        }

        var keyText = keyFile is not null ? File.ReadAllText(keyFile) : Environment.GetEnvironmentVariable(keyEnv!);
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return Fail(error, "The private key is empty or the environment variable is not set.");
        }

        var data = File.ReadAllBytes(file);
        var check = CatalogValidator.ParseAndValidate(data, new Version(int.MaxValue, 0));
        if (!check.Ok && check.Rejection != CatalogRejection.RequiresNewerApp)
        {
            return Fail(error, $"Refusing to sign an invalid catalog: {check.Rejection} ({check.Detail}).");
        }

        var signature = CatalogSigning.Sign(data, Convert.FromBase64String(keyText.Trim()));
        File.WriteAllText(file + ".sig", Convert.ToBase64String(signature));
        output.WriteLine("Wrote " + file + ".sig");
        return 0;
    }

    private static int Verify(string[] args, TextWriter output, TextWriter error)
    {
        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var pub = Option(args, "--pub") ?? (Option(args, "--pub-file") is { } pubFile ? File.ReadAllText(pubFile) : null);
        if (file is null || pub is null)
        {
            return Fail(error, "verify needs a catalog file and --pub <base64> or --pub-file <file>.");
        }

        var verifier = new CatalogSignatureVerifier([Convert.FromBase64String(pub.Trim())]);
        if (verifier.VerifyBase64(File.ReadAllBytes(file), File.ReadAllText(file + ".sig")))
        {
            output.WriteLine("Signature OK.");
            return 0;
        }

        return Fail(error, "Signature does NOT match.");
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }
}
