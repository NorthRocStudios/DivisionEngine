//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Serialization;

namespace DivisionEngine
{
    /// <summary>
    /// Helper class for accessing the current world.
    /// </summary>
    public static class W
    {
        #region entities

        /// <summary>
        /// Check if an entity exists in the world.
        /// </summary>
        /// <param name="id">Entity id to check</param>
        /// <returns>Whether the entity exists in the world</returns>
        public static bool EntityExists(uint id) => WorldManager.CurrentWorld!.EntityExists(id);

        /// <summary>
        /// Creates a new entity in the current world.
        /// </summary>
        /// <returns>The new entity id created</returns>
        public static uint CreateEntity() => WorldManager.CurrentWorld!.CreateEntity();

        /// <summary>
        /// Creates a new entity in the current world with a transform component.
        /// </summary>
        /// <returns>The new entity id created</returns>
        public static uint CreateTransformEntity() => WorldManager.CurrentWorld!.CreateTransformEntity();

        /// <summary>
        /// Creates a new entity in the world.
        /// </summary>
        /// <param name="name">The name of the entity</param>
        /// <returns>The new entity id created</returns>
        public static uint CreateEntity(string name) => WorldManager.CurrentWorld!.CreateEntity(name);

        /// <summary>
        /// Creates a new entity in the world with a transform component.
        /// </summary>
        /// <param name="name">The name of the entity</param>
        /// <returns>The new entity id created</returns>
        public static uint CreateTransformEntity(string name) => WorldManager.CurrentWorld!.CreateTransformEntity(name);

        /// <summary>
        /// Creates a new entity with all the components and their values as another source entity.
        /// </summary>
        /// <param name="sourceEntityId">Entity source id to clone from</param>
        /// <returns>The duplicated entity id created</returns>
        public static uint DuplicateEntity(uint sourceEntityId) => WorldManager.CurrentWorld!.DuplicateEntity(sourceEntityId);

        /// <summary>
        /// Destroy an entity in the world.
        /// </summary>
        /// <param name="entityId">Entity to destroy</param>
        /// <returns>Whether entity of <paramref name="entityId"/> was destroyed.</returns>
        public static bool DestroyEntity(uint entityId) => WorldManager.CurrentWorld!.DestroyEntity(entityId);

        /// <summary>
        /// Attempts to get the entity's name from name component.
        /// </summary>
        /// <param name="entityId">Entity to find name for</param>
        /// <returns>Entity name if exists, otherwise empty string</returns>
        public static string TryGetEntityName(uint entityId) => WorldManager.CurrentWorld!.TryGetEntityName(entityId);

        #endregion
        #region systems

        /// <summary>
        /// Searches all assemblies in the Application Domain to find all classes that inherit from SystemBase and registers them automatically.
        /// </summary>
        /// <exception cref="NotImplementedException">Throws an exception if a system is not implemented correctly</exception>
        public static void RegisterAllSystems() => WorldManager.CurrentWorld!.RegisterAllSystems();

        /// <summary>
        /// Registers any SystemBase types that are loaded in the current AppDomain
        /// but not already present in this world. Preserves existing system instances
        /// (and their state) - only genuinely new types are instantiated.
        /// <para>
        /// Matching is done by <see cref="Type.FullName"/> rather than Type reference
        /// because recompiling scripts produces a new assembly with new Type objects
        /// for the same classes, and the old load context can't be unloaded while
        /// existing system instances still reference it. Matching by name keeps the
        /// existing instances authoritative.
        /// </para>
        /// </summary>
        public static void RegisterNewSystems() => WorldManager.CurrentWorld!.RegisterNewSystems();

        /// <summary>
        /// Gets a system of the specified type from the world.
        /// </summary>
        /// <typeparam name="T">Type of system to retrieve</typeparam>
        /// <returns>The system instance, or null if not found</returns>
        public static T? GetSystem<T>() where T : SystemBase => WorldManager.CurrentWorld!.GetSystem<T>();

        /// <summary>
        /// Gets a system of the specified type from the world.
        /// </summary>
        /// <param name="systemType">Type of system to retrieve</param>
        /// <returns>The system instance, or null if not found</returns>
        public static SystemBase? GetSystem(Type systemType) => WorldManager.CurrentWorld!.GetSystem(systemType);

        /// <summary>
        /// Checks if a system of the specified type exists in the world.
        /// </summary>
        /// <typeparam name="T">Type of system to check</typeparam>
        /// <returns>True if the system exists</returns>
        public static bool HasSystem<T>() where T : SystemBase => WorldManager.CurrentWorld!.HasSystem<T>();

        /// <summary>
        /// Checks if a system of the specified type exists in the world.
        /// </summary>
        /// <param name="systemType">Type of system to check</param>
        /// <returns>True if the system exists</returns>
        public static bool HasSystem(Type systemType) => WorldManager.CurrentWorld!.HasSystem(systemType);

        #endregion
        #region components

        /// <summary>
        /// Adds a component onto an entity in the world.
        /// </summary>
        /// <typeparam name="T">Component type to add</typeparam>
        /// <param name="entityId">Entity to add component to</param>
        /// <param name="component">Component data to add</param>
        /// <returns>True if the component was added</returns>
        public static bool AddComponent<T>(uint entityId, T component) where T : IComponent =>
            WorldManager.CurrentWorld!.AddComponent(entityId, component);

        /// <summary>
        /// Removes a component from an entity in the world.
        /// </summary>
        /// <typeparam name="T">Component to remove</typeparam>
        /// <param name="entityId">Entity to remove component from</param>
        /// <returns>True if the component was removed</returns>
        public static bool RemoveComponent<T>(uint entityId) where T : IComponent =>
            WorldManager.CurrentWorld!.RemoveComponent<T>(entityId);

        /// <summary>
        /// Removes a component from an entity in the world.
        /// </summary>
        /// <param name="entityId">Entity to remove component from</param>
        /// <param name="type">Type of component to remove</param>
        /// <returns>True if the component was removed</returns>
        public static bool RemoveComponent(uint entityId, Type type) =>
            WorldManager.CurrentWorld!.RemoveComponent(entityId, type);

        /// <summary>
        /// Gets a component on an entity.
        /// </summary>
        /// <typeparam name="T">Type of component to get</typeparam>
        /// <param name="entityId">The entity</param>
        /// <returns>The component on the entity</returns>
        /// <exception cref="InvalidOperationException">Throws an exception when entity does not have component</exception>
        public static T? GetComponent<T>(uint entityId) where T : IComponent =>
            WorldManager.CurrentWorld!.GetComponent<T>(entityId);

        /// <summary>
        /// Gets all components on a specific entity.
        /// </summary>
        /// <param name="entityId">Entity ID to check</param>
        /// <returns>List of all components on entity of <paramref name="entityId"/></returns>
        public static List<IComponent> GetAllComponents(uint entityId) =>
            WorldManager.CurrentWorld!.GetAllComponents(entityId);

        /// <summary>
        /// Checks if an entity has a component of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of component to check for</typeparam>
        /// <param name="entityId">Entity ID to check on</param>
        /// <returns>Whether the entity of <paramref name="entityId"/> has a component type <typeparamref name="T"/></returns>
        public static bool HasComponent<T>(uint entityId) where T : IComponent =>
            WorldManager.CurrentWorld!.HasComponent<T>(entityId);

        /// <summary>
        /// Checks if an entity has a component of a certain type.
        /// </summary>
        /// <param name="entityId">Entity ID to check on</param>
        /// <param name="type">Component type to check for</param>
        /// <returns>Whether the entity of <paramref name="entityId"/> has a certain component type</returns>
        public static bool HasComponent(uint entityId, Type type) =>
            WorldManager.CurrentWorld!.HasComponent(entityId, type);

        /// <summary>
        /// Adds a component to the world from ComponentData serialized data.
        /// </summary>
        /// <param name="entityId">Entity to add component to</param>
        /// <param name="componentData">ComponentData object to parse and add</param>
        public static void AddComponentFromData(uint entityId, ComponentData componentData) =>
            WorldManager.CurrentWorld!.AddComponentFromData(entityId, componentData);

        #endregion
        #region queries

        /// <summary>
        /// Queries the world to find entities with a component.
        /// </summary>
        /// <typeparam name="T">Type of components to find</typeparam>
        /// <returns>All entities with component type <typeparamref name="T"/></returns>
        public static IEnumerable<uint> Query<T>() where T : IComponent => WorldManager.CurrentWorld!.Query<T>();

        /// <summary>
        /// Queries the world to find entities with certain component types.
        /// </summary>
        /// <param name="componentTypes">Component types to discover</param>
        /// <returns>List of entities with component types</returns>
        public static IEnumerable<uint> Query(params Type[] componentTypes) => WorldManager.CurrentWorld!.Query(componentTypes);

        /// <summary>
        /// Queries the world to find entities with component types and returns the component data.
        /// </summary>
        /// <typeparam name="T">Type of component to search for</typeparam>
        /// <returns>Tuple of (entityID, T component)</returns>
        public static IEnumerable<(uint, T)> QueryData<T>() where T : IComponent => WorldManager.CurrentWorld!.QueryData<T>();

        public static IEnumerable<(uint, T1, T2)> QueryData<T1, T2>()
            where T1 : IComponent where T2 : IComponent =>
            WorldManager.CurrentWorld!.QueryData<T1, T2>();

        public static IEnumerable<(uint, T1, T2, T3)> QueryData<T1, T2, T3>()
            where T1 : IComponent where T2 : IComponent where T3 : IComponent =>
            WorldManager.CurrentWorld!.QueryData<T1, T2, T3>();

        public static IEnumerable<(uint, IComponent[])> QueryData(params Type[] componentTypes) =>
            WorldManager.CurrentWorld!.QueryData(componentTypes);

        #endregion
    }
}
