namespace EnvSync.Core.CodeGeneration;

/// <summary>
/// Identifies the supported output languages for generated environment bindings.
/// </summary>
public enum BindingLanguage
{
    /// <summary>
    /// Generate TypeScript bindings.
    /// </summary>
    TypeScript,

    /// <summary>
    /// Generate C# bindings.
    /// </summary>
    CSharp,
}
