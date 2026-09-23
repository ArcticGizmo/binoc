namespace Binoc.Core.Android;

/// <summary>
/// Reads the split dimensions an AAB is configured to split on, from its root <c>BundleConfig.pb</c>. bundletool
/// splits by ABI, screen density and language <em>by default</em>; a bundle can disable a dimension (negate) in
/// this config. We decode just enough of the <c>BundleConfig</c> proto to see those overrides (decision D1: our
/// own minimal protobuf reader, not <c>Google.Protobuf</c>).
///
/// <para>Proto path (config.proto): <c>BundleConfig{ optimizations=2 }</c>,
/// <c>Optimizations{ splits_config=1 }</c>, <c>SplitsConfig{ split_dimension=1 (repeated) }</c>,
/// <c>SplitDimension{ value=1 (enum varint), negate=2 (bool varint) }</c>. Enum: ABI=1, SCREEN_DENSITY=2,
/// LANGUAGE=3.</para>
/// </summary>
public static class BundleConfigReader
{
    public const string Abi = "ABI";
    public const string ScreenDensity = "Screen density";
    public const string Language = "Language";

    /// <summary>The dimensions bundletool splits on by default.</summary>
    private static readonly string[] Defaults = { Abi, ScreenDensity, Language };

    /// <summary>The resolved enabled dimensions and whether they came from a parsed config.</summary>
    public readonly record struct Result(IReadOnlyList<string> EnabledDimensions, bool FromConfig);

    /// <summary>Resolves the enabled split dimensions. With no config bytes, returns the bundletool defaults.
    /// With a config, starts from the defaults and applies any <c>negate</c> overrides it declares.</summary>
    public static Result Resolve(byte[]? configBytes)
    {
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [Abi] = true, [ScreenDensity] = true, [Language] = true,
        };

        bool parsed = false;
        if (configBytes is { Length: > 0 })
        {
            foreach (var (dim, negate) in ReadSplitDimensions(configBytes))
            {
                parsed = true;
                if (enabled.ContainsKey(dim)) enabled[dim] = !negate;
            }
        }

        var result = Defaults.Where(d => enabled[d]).ToList();
        return new Result(result, parsed);
    }

    private static IEnumerable<(string Dim, bool Negate)> ReadSplitDimensions(byte[] d)
    {
        // BundleConfig.optimizations (2)
        foreach (var opt in Protobuf.Fields(d, 0, d.Length))
        {
            if (opt.Number != 2 || opt.Wire != Protobuf.WireLen) continue;
            // Optimizations.splits_config (1)
            foreach (var sc in Protobuf.Fields(d, opt.PayloadStart, opt.PayloadStart + opt.PayloadLen))
            {
                if (sc.Number != 1 || sc.Wire != Protobuf.WireLen) continue;
                // SplitsConfig.split_dimension (1, repeated)
                foreach (var sd in Protobuf.Fields(d, sc.PayloadStart, sc.PayloadStart + sc.PayloadLen))
                {
                    if (sd.Number != 1 || sd.Wire != Protobuf.WireLen) continue;
                    if (ReadDimension(d, sd.PayloadStart, sd.PayloadStart + sd.PayloadLen) is { } dim)
                        yield return dim;
                }
            }
        }
    }

    private static (string, bool)? ReadDimension(byte[] d, int start, int end)
    {
        int value = 0; bool negate = false;
        foreach (var f in Protobuf.Fields(d, start, end))
        {
            if (f.Wire != Protobuf.WireVarint) continue;
            if (f.Number == 1) value = (int)f.VarintValue;
            else if (f.Number == 2) negate = f.VarintValue != 0;
        }

        return value switch
        {
            1 => (Abi, negate),
            2 => (ScreenDensity, negate),
            3 => (Language, negate),
            _ => null,
        };
    }
}
