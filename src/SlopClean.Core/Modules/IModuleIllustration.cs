namespace SlopClean.Core.Modules;

/// <summary>
/// Optional capability: module ships a PNG illustration as an embedded resource.
/// </summary>
public interface IModuleIllustration : IModule
{
    /// <summary>
    /// Opens a seekable PNG stream. The caller owns disposal.
    /// </summary>
    Stream OpenIllustration();
}
