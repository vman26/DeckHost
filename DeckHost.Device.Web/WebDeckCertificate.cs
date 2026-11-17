using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DeckHost.Device.Web;

/// <summary>
/// The Web Deck's certificate for HTTPS: made here, once, for this computer's names and addresses, and kept next to the
/// deck's settings. It is self-signed, so a phone has to be told to trust it once (compare its fingerprint with the one in
/// Device Settings); nothing leaves the computer and no certificate authority is involved.
/// </summary>
public static class WebDeckCertificate
{
    private const string FileName = "web.deck.pfx";
    private const string NamesFileName = "web.deck.names";

    /// <summary>How long a new certificate is valid; one with less than <see cref="RenewBefore"/> left is replaced.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    /// <summary>The names and addresses this computer answers to (the certificate lists them): localhost, the machine name and its network addresses.</summary>
    public static IReadOnlyList<string> CurrentNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1" };
        try
        {
            names.Add(Dns.GetHostName());
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(static adapter => adapter.OperationalStatus == OperationalStatus.Up))
            {
                foreach (var address in adapter.GetIPProperties().UnicastAddresses.Select(static unicast => unicast.Address))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork || (address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal))
                    {
                        names.Add(address.ToString());
                    }
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException)
        {
            // localhost still works.
        }

        return [.. names];
    }

    /// <summary>
    /// The certificate kept in <paramref name="directory"/> for <paramref name="names"/>, made when there is none, when the
    /// names changed (a new network address) or when it is about to expire. <paramref name="created"/> says whether it was made now.
    /// </summary>
    public static X509Certificate2 LoadOrCreate(string directory, IReadOnlyList<string> names, DateTimeOffset now, out bool created)
    {
        var pfxPath = Path.Combine(directory, FileName);
        var namesPath = Path.Combine(directory, NamesFileName);
        var wanted = string.Join('\n', names.Order(StringComparer.OrdinalIgnoreCase));
        created = false;
        try
        {
            if (File.Exists(pfxPath) && File.Exists(namesPath) && File.ReadAllText(namesPath) == wanted)
            {
                var existing = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter.ToUniversalTime() - now.UtcDateTime > RenewBefore && existing.NotBefore.ToUniversalTime() <= now.UtcDateTime)
                {
                    return existing;
                }

                existing.Dispose();
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Unreadable or damaged: make a new one.
        }

        var fresh = Create(names, now);
        created = true;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(pfxPath, fresh.Export(X509ContentType.Pfx));
            File.WriteAllText(namesPath, wanted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept: the next start makes another one (the phone has to trust it again).
        }

        // A key loaded from a PFX works with every platform's TLS stack; the one just created in memory doesn't on Windows.
        var loaded = X509CertificateLoader.LoadPkcs12(fresh.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        fresh.Dispose();
        return loaded;
    }

    private static X509Certificate2 Create(IReadOnlyList<string> names, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=DeckHost Web Deck", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var alternative = new SubjectAlternativeNameBuilder();
        foreach (var name in names)
        {
            if (IPAddress.TryParse(name, out var address))
            {
                alternative.AddIpAddress(address);
            }
            else
            {
                alternative.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(alternative.Build());
        return request.CreateSelfSigned(now.AddDays(-1), now.Add(Lifetime));
    }

    /// <summary>The certificate's SHA-256 fingerprint as hex pairs (<c>AB:CD:…</c>), what a phone shows when asked to trust it.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        string.Join(':', SHA256.HashData(certificate.RawData).Select(static b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}
