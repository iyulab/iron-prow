namespace IronProw.Core;

/// <summary>Classifies an inference provider by deployment locus.</summary>
public enum ProviderKind
{
    /// <summary>Cloud frontier provider (OpenAI, Anthropic, ...).</summary>
    Frontier,
    /// <summary>LAN provider (GpuStack over the local network).</summary>
    Lan,
    /// <summary>In-process local inference (ONNX/DirectML).</summary>
    Local
}
