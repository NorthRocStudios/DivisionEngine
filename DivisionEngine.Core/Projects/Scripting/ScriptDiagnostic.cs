//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Projects.Scripting
{
    /// <summary>
    /// Severity of a script compilation diagnostic.
    /// </summary>
    public enum ScriptDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// A single message produced by a script compiler. Shaped to mirror Roslyn's
    /// <c>Diagnostic</c> so the Roslyn backend is a direct mapping.
    /// </summary>
    public sealed class ScriptDiagnostic(
        ScriptDiagnosticSeverity severity,
        string message,
        string? filePath = null,
        int line = 0,
        int column = 0)
    {
        public ScriptDiagnosticSeverity Severity { get; } = severity;
        public string Message { get; } = message;
        public string? FilePath { get; } = filePath;
        public int Line { get; } = line;
        public int Column { get; } = column;

        public override string ToString()
        {
            if (string.IsNullOrEmpty(FilePath))
                return $"[{Severity}] {Message}";
            return $"{FilePath}({Line},{Column}): [{Severity}] {Message}";
        }
    }
}
