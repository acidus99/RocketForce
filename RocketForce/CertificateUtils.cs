using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RocketForce;

public static class CertificateUtils
{
    /// <summary>
    /// Attempts to load a certificate, given a certificate file ane private key
    /// </summary>
    /// <param name="certPath"></param>
    /// <param name="keyPath"></param>
    /// <param name="certificate"></param>
    /// <returns>true if certificate could be loaded</returns>
    public static bool TryLoadCertificate(string certPath, string keyPath, out X509Certificate2? certificate)
    {
        certificate = null;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool CreateLocalCertificates(string certPath, string keyPath, string subjectName)
    {
        // Create an elliptic curve (EC) key pair with P-256 curve
        try
        {
            using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                // Define the certificate request
                var certRequest = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);

                // Add certificate extensions (e.g., key usage, subject key identifier)
                certRequest.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));
                certRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                certRequest.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

                // Generate the self-signed certificate with a 5-year expiration
                X509Certificate2 certificate =
                    certRequest.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

                string privateKeyPem = ToPem(ecdsa.ExportPkcs8PrivateKey(), "PRIVATE KEY");
                string publicKeyPem = ToPem(certificate.Export(X509ContentType.Cert), "CERTIFICATE");
                File.WriteAllText(keyPath, privateKeyPem);
                File.WriteAllText(certPath, publicKeyPem);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    // Helper method to convert a byte array to PEM format
    private static string ToPem(byte[] data, string type)
    {
        // PEM format headers and footers
        string base64 = Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN {type}-----\n{base64}\n-----END {type}-----";
    }
}