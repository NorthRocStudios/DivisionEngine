//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Serialization
{
    /// <summary>
    /// Used for marking objects as serializable for hard state management.
    /// </summary>
    public interface ISerializable
    {
        /// <summary>
        /// Serializes an object.
        /// </summary>
        /// <returns>Serialized json structure</returns>
        string Serialize();

        /// <summary>
        /// Deseralizes an object.
        /// </summary>
        void Deserialize(string obj);
    }
}
