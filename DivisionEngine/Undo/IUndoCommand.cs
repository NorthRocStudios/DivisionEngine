//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Editor.Undo
{
    /// <summary>
    /// Represents an undo/redo operation.
    /// </summary>
    public interface IUndoCommand
    {
        string Description { get; } // description of operation
        void Do(); // execute or redo
        void Undo(); // reverse
    }
}
