namespace EnvSync.Core.CodeGeneration;

/// <summary>
/// Represents a generated binding file and its text content.
/// </summary>
/// <param name="FileName">The recommended file name for the generated binding.</param>
/// <param name="Content">The generated file content.</param>
public sealed record GeneratedBindingFile(string FileName, string Content);
