/// <summary>
/// Signs the requests to the WOPI hosts with the proof keys ([MS-WOPI] 2.2.1), and publishes the keys in the
/// discovery (3.1.5.1.1.2.2.5).
/// </summary>
sealed class WopiProofs(IOptions<MorphWopiOptions> options) : IDisposable
{
    Lazy<RSA> made = new(() => RSA.Create(2048));

    public RSA Current => options.Value.ProofKey ?? made.Value;

    public RSA? Old => options.Value.OldProofKey;

    /// <summary>
    /// X-WOPI-Proof, signed with the current key, and X-WOPI-ProofOld, with the old key or, with none, the
    /// current one.
    /// </summary>
    public (string Proof, string ProofOld) Sign(string accessToken, string url, long ticks)
    {
        var data = Data(accessToken, url, ticks);
        var proof = Signature(Current, data);
        if (Old is { } old)
        {
            return (proof, Signature(old, data));
        }

        return (proof, proof);
    }

    static string Signature(RSA key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    /// <summary>
    /// What is signed: the token, the URL in upper case and the timestamp, each after its length as a 4-byte
    /// big-endian integer; the timestamp is an 8-byte big-endian integer.
    /// </summary>
    public static byte[] Data(string accessToken, string url, long ticks)
    {
        var token = Encoding.UTF8.GetBytes(accessToken);
        var address = Encoding.UTF8.GetBytes(url.ToUpperInvariant());
        var data = new byte[4 + token.Length + 4 + address.Length + 4 + 8];
        var span = data.AsSpan();
        BinaryPrimitives.WriteInt32BigEndian(span, token.Length);
        token.CopyTo(span[4..]);
        span = span[(4 + token.Length)..];
        BinaryPrimitives.WriteInt32BigEndian(span, address.Length);
        address.CopyTo(span[4..]);
        span = span[(4 + address.Length)..];
        BinaryPrimitives.WriteInt32BigEndian(span, 8);
        BinaryPrimitives.WriteInt64BigEndian(span[4..], ticks);
        return data;
    }

    /// <summary>
    /// The proof-key element: each key as its modulus and exponent, and as the CSP blob of its value.
    /// </summary>
    public XElement Element()
    {
        var element = new XElement("proof-key");
        Add(element, "", Current);
        if (Old is { } old)
        {
            Add(element, "old", old);
        }

        return element;
    }

    static void Add(XElement element, string prefix, RSA key)
    {
        var parameters = key.ExportParameters(false);
        element.SetAttributeValue($"{prefix}value", Convert.ToBase64String(CspBlob(parameters)));
        element.SetAttributeValue($"{prefix}modulus", Convert.ToBase64String(parameters.Modulus!));
        element.SetAttributeValue($"{prefix}exponent", Convert.ToBase64String(parameters.Exponent!));
    }

    /// <summary>
    /// The public key as a CryptoAPI PUBLICKEYBLOB, as Office publishes it: a BLOBHEADER, an RSAPUBKEY, then
    /// the modulus, little-endian. Written out rather than exported, which only Windows can.
    /// </summary>
    public static byte[] CspBlob(RSAParameters parameters)
    {
        var modulus = parameters.Modulus!;
        var exponent = 0u;
        foreach (var part in parameters.Exponent!)
        {
            exponent = (exponent << 8) | part;
        }

        var blob = new byte[20 + modulus.Length];
        var span = blob.AsSpan();
        // PUBLICKEYBLOB, CUR_BLOB_VERSION, reserved, CALG_RSA_KEYX
        span[0] = 0x06;
        span[1] = 0x02;
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0xA400);
        // "RSA1", the length in bits, the public exponent
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 0x31415352);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint) modulus.Length * 8);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], exponent);
        modulus.CopyTo(span[20..]);
        span[20..].Reverse();
        return blob;
    }

    public void Dispose()
    {
        if (made.IsValueCreated)
        {
            made.Value.Dispose();
        }
    }
}
