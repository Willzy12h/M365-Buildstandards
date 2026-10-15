using System.Security.Cryptography;

namespace BuildStandard.Graph.Setup;

public static class ProductLogo
{
    public static byte[] Read()
    {
        using var input = typeof(ProductLogo).Assembly.GetManifestResourceStream("BuildStandard.Graph.Assets.buildstandard.png")
            ?? throw new InvalidOperationException("Product icon resource is missing.");
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    public static string Digest => Convert.ToHexString(SHA256.HashData(Read())).ToLowerInvariant();
}
