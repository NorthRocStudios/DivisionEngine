//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Serialization;
using System.Reflection;

namespace DivisionEngine
{
    /// <summary>
    /// Stores all entities, components, and systems. This is the main ECS API.
    /// </summary>
    public class World
    {
        /// <summary>
        /// The name of this world.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// All entities in the world.
        /// </summary>
        public HashSet<uint> entities;

        /// <summary>
        /// Next ID used to register a new entity.
        /// </summary>
        public uint NextEntityId { get; set; }

        /// <summary>
        /// All componenents in the world organized by component type => entity => component data.
        /// </summary>
        public Dictionary<Type, Dictionary<uint, IComponent>> components;

        /// <summary>
        /// All systems in the world.
        /// </summary>
        public List<SystemBase> systems;

        private readonly List<SystemBase> appStartSystems, awakeSystems, 
            updateSystems, editorUpdateSystems, fixedUpdateSystems,
            unloadSystems, appExitSystems, renderSystems;

        /// <summary>
        /// Create a new world.
        /// </summary>
        public World(string name)
        {
            Name = name;
            entities = [];
            components = [];
            systems = [];

            awakeSystems = [];
            appStartSystems = [];
            updateSystems = [];
            editorUpdateSystems = [];
            fixedUpdateSystems = [];
            unloadSystems = [];
            appExitSystems = [];
            renderSystems = [];
            NextEntityId = 0;

            RegisterAllSystems();
        }

        #region entities

        /// <summary>
        /// Check if an entity exists in the world.
        /// </summary>
        /// <param name="id">Entity id to check</param>
        /// <returns>Whether the entity exists in the world</returns>
        public bool EntityExists(uint id) => entities.Contains(id);

        /// <summary>
        /// Creates a new entity in the world.
        /// </summary>
        /// <returns>The new entity id created</returns>
        public uint CreateEntity()
        {
            uint id = NextEntityId;
            entities.Add(id);
            NextEntityId++;
            return id;
        }

        /// <summary>
        /// Creates a new entity in the world with a transform component.
        /// </summary>
        /// <returns>The new entity id created</returns>
        public uint CreateTransformEntity()
        {
            uint id = CreateEntity();
            AddComponent(id, new Transform());
            return id;
        }

        /// <summary>
        /// Creates a new entity in the world.
        /// </summary>
        /// <param name="name">The name of the entity</param>
        /// <returns>The new entity id created</returns>
        public uint CreateEntity(string name)
        {
            uint id = CreateEntity();
            AddComponent(id, new Name(name));
            return id;
        }

        /// <summary>
        /// Creates a new entity in the world with a transform component.
        /// </summary>
        /// <param name="name">The name of the entity</param>
        /// <returns>The new entity id created</returns>
        public uint CreateTransformEntity(string name)
        {
            uint id = CreateEntity(name);
            AddComponent(id, new Name(name));
            AddComponent(id, new Transform());
            return id;
        }

        /// <summary>
        /// Creates a new entity with all the components and their values as another source entity.
        /// </summary>
        /// <param name="sourceEntityId">Entity source id to clone from</param>
        /// <returns>The duplicated entity id created</returns>
        public uint DuplicateEntity(uint sourceEntityId)
        {
            if (!EntityExists(sourceEntityId)) return sourceEntityId;
            List<IComponent> components = GetAllComponents(sourceEntityId);
            uint id = CreateEntity();

            for (int i = 0; i < components.Count; i++)
            {
                AddComponent(id, components[i].Clone());
                Debug.Error($"Component: {components[i].GetType()}");
            }
            return id;
        }

        /// <summary>
        /// Destroy an entity in the world.
        /// </summary>
        /// <param name="entityId">Entity to destroy</param>
        /// <returns>Whether entity of <paramref name="entityId"/> was destroyed.</returns>
        public bool DestroyEntity(uint entityId)
        {
            if (entities.Remove(entityId))
            {
                foreach (Type t in components.Keys) components[t].Remove(entityId);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Attempts to get the entity's name from name component.
        /// </summary>
        /// <param name="entityId">Entity to find name for</param>
        /// <returns>Entity name if exists, otherwise empty string</returns>
        public string TryGetEntityName(uint entityId)
        {
            if (HasComponent<Name>(entityId))
            {
                Name nameComp = GetComponent<Name>(entityId)!;
                return nameComp.name!;
            }
            return string.Empty;
        }

        public bool AddEntityWithId(uint id, string? name = null)
        {
            if (entities.Contains(id)) return false;
            entities.Add(id);
            if (id >= NextEntityId) NextEntityId = id + 1;
            if (!string.IsNullOrEmpty(name))
                AddComponent(id, new Name(name));
            return true;
        }

        #endregion
        #region systems

        /// <summary>
        /// Searches all assemblies in the Application Domain to find all classes that inherit from SystemBase and registers them automatically.
        /// </summary>
        /// <exception cref="NotImplementedException">Throws an exception if a system is not implemented correctly</exception>
        public void RegisterAllSystems()
        {
            systems.Clear();
            appStartSystems.Clear();
            awakeSystems.Clear();
            updateSystems.Clear();
            editorUpdateSystems.Clear();
            fixedUpdateSystems.Clear();
            unloadSystems.Clear();
            appExitSystems.Clear();
            renderSystems.Clear();

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;

                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = [.. ex.Types.Where(t => t != null).Cast<Type>()];
                    Debug.Warning($"World: partial type load from {a.GetName().Name}");
                }
                catch (Exception ex)
                {
                    Debug.Warning($"World: skipping assembly {a.GetName().Name}", ex);
                    continue;
                }

                foreach (Type t in types)
                {
                    if (typeof(SystemBase).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    {
                        try
                        {
                            if (Activator.CreateInstance(t) is SystemBase sys) RegisterSystem(sys);
                            else Debug.Warning($"World: could not instantiate {t.Name}");
                        }
                        catch (Exception ex)
                        {
                            Debug.Warning($"World: failed to instantiate {t.Name}", ex);
                        }
                    }
                }
            }
        }

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
        public void RegisterNewSystems()
        {
            // Match by fully-qualified name, not Type reference. See remarks.
            HashSet<string> existingNames = new(StringComparer.Ordinal);
            foreach (SystemBase sys in systems)
            {
                string? name = sys.GetType().FullName;
                if (name != null) existingNames.Add(name);
            }

            List<SystemBase> newSystems = [];
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;

                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = [.. ex.Types.Where(t => t != null).Cast<Type>()];
                    Debug.Warning($"World: partial type load from {a.GetName().Name}");
                }
                catch (Exception ex)
                {
                    Debug.Warning($"World: skipping assembly {a.GetName().Name}", ex);
                    continue;
                }

                foreach (Type t in types)
                {
                    if (!typeof(SystemBase).IsAssignableFrom(t)) continue;
                    if (t.IsAbstract || t.IsInterface) continue;

                    string? fullName = t.FullName;
                    if (fullName == null || existingNames.Contains(fullName)) continue;

                    try
                    {
                        if (Activator.CreateInstance(t) is SystemBase sys)
                        {
                            RegisterSystem(sys);
                            newSystems.Add(sys);
                            existingNames.Add(fullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Warning($"World: failed to instantiate system {t.Name}", ex);
                    }
                }
            }

            if (newSystems.Count == 0) return;
            foreach (SystemBase sys in newSystems) sys.Awake();
            foreach (SystemBase sys in newSystems) sys.AppStart();

            Debug.Info($"World: registered {newSystems.Count} new system(s)");
        }

        /// <summary>
        /// Registers a system and adds it to the correct callback loops.
        /// </summary>
        /// <param name="system">System to register</param>
        private void RegisterSystem(SystemBase system)
        {
            systems.Add(system);

            int priority = system.Priority;
            Type sysBaseType = typeof(SystemBase), sysType = system.GetType();
            MethodInfo? appStartInfo = sysType.GetMethod("AppStart"),
                awakeInfo = sysType.GetMethod("Awake"),
                updateInfo = sysType.GetMethod("Update"),
                editorUpdateInfo = sysType.GetMethod("EditorUpdate"),
                fixedUpdateInfo = sysType.GetMethod("FixedUpdate"),
                unloadInfo = sysType.GetMethod("Unload"),
                appExitInfo = sysType.GetMethod("AppExit"),
                renderInfo = sysType.GetMethod("Render");

            if (appStartInfo != null && appStartInfo.DeclaringType != sysBaseType) AddWithPriority(appStartSystems, system, priority);
            if (awakeInfo != null && awakeInfo.DeclaringType != sysBaseType) AddWithPriority(awakeSystems, system, priority);
            if (updateInfo != null && updateInfo.DeclaringType != sysBaseType) AddWithPriority(updateSystems, system, priority);
            if (editorUpdateInfo != null && editorUpdateInfo.DeclaringType != sysBaseType) AddWithPriority(editorUpdateSystems, system, priority);
            if (fixedUpdateInfo != null && fixedUpdateInfo.DeclaringType != sysBaseType) AddWithPriority(fixedUpdateSystems, system, priority);
            if (unloadInfo != null && unloadInfo.DeclaringType != sysBaseType) AddWithPriority(unloadSystems, system, priority);
            if (appExitInfo != null && appExitInfo.DeclaringType != sysBaseType) AddWithPriority(appExitSystems, system, priority);
            if (renderInfo != null && renderInfo.DeclaringType != sysBaseType) AddWithPriority(renderSystems, system, priority);
        }

        private static void AddWithPriority(List<SystemBase> list, SystemBase system, int priority)
        {
            // Find the correct position to insert based on priority
            int index = 0;
            while (index < list.Count && list[index].Priority < priority) index++;

            if (index < list.Count && list[index].Priority == priority)
            {   // Same priority, add at the end of that priority group
                while (index < list.Count && list[index].Priority == priority) index++;
            }
            list.Insert(index, system);
        }

        /// <summary>
        /// Calls all systems that implement "AppStart".
        /// </summary>
        public void CallAppStart()
        {
            for (int i = 0; i < appStartSystems.Count; i++) appStartSystems[i].AppStart();
        }

        /// <summary>
        /// Calls all systems that implement "Awake".
        /// </summary>
        public void CallAwake()
        {
            for (int i = 0; i < awakeSystems.Count; i++) awakeSystems[i].Awake();
        }

        /// <summary>
        /// Calls all systems that implement "Update".
        /// </summary>
        public void CallUpdate()
        {
            for (int i = 0; i < updateSystems.Count; i++) updateSystems[i].Update();
        }

        /// <summary>
        /// Calls all systems that implement "EditorUpdate".
        /// </summary>
        public void CallEditorUpdate()
        {
            for (int i = 0; i < editorUpdateSystems.Count; i++) editorUpdateSystems[i].EditorUpdate();
        }

        /// <summary>
        /// Calls all systems that implement "FixedUpdate".
        /// </summary>
        public void CallFixedUpdate()
        {
            for (int i = 0; i < fixedUpdateSystems.Count; i++) fixedUpdateSystems[i].FixedUpdate();
        }

        /// <summary>
        /// Calls all systems that implement "Unload".
        /// </summary>
        public void CallUnload()
        {
            for (int i = 0; i < unloadSystems.Count; i++) unloadSystems[i].Unload();
        }

        /// <summary>
        /// Calls all systems that implement "AppExit".
        /// </summary>
        public void CallAppExit()
        {
            for (int i = 0; i < appExitSystems.Count; i++) appExitSystems[i].AppExit();
        }

        /// <summary>
        /// Calls all systems that implement "Render".
        /// </summary>
        public void CallRender()
        {
            for (int i = 0; i < renderSystems.Count; i++) renderSystems[i].Render();
        }

        /// <summary>
        /// Gets a system of the specified type from the world.
        /// </summary>
        /// <typeparam name="T">Type of system to retrieve</typeparam>
        /// <returns>The system instance, or null if not found</returns>
        public T? GetSystem<T>() where T : SystemBase
        {
            for (int i = 0; i < systems.Count; i++)
                if (systems[i] is T system)
                    return system;
            return null;
        }

        /// <summary>
        /// Gets a system of the specified type from the world.
        /// </summary>
        /// <param name="systemType">Type of system to retrieve</param>
        /// <returns>The system instance, or null if not found</returns>
        public SystemBase? GetSystem(Type systemType)
        {
            for (int i = 0; i < systems.Count; i++)
                if (systems[i].GetType() == systemType)
                    return systems[i];
            return null;
        }

        /// <summary>
        /// Checks if a system of the specified type exists in the world.
        /// </summary>
        /// <typeparam name="T">Type of system to check</typeparam>
        /// <returns>True if the system exists</returns>
        public bool HasSystem<T>() where T : SystemBase => GetSystem<T>() != null;

        /// <summary>
        /// Checks if a system of the specified type exists in the world.
        /// </summary>
        /// <param name="systemType">Type of system to check</param>
        /// <returns>True if the system exists</returns>
        public bool HasSystem(Type systemType) => GetSystem(systemType) != null;

        #endregion
        #region components

        /// <summary>
        /// Adds a component onto an entity in the world.
        /// </summary>
        /// <typeparam name="T">Component type to add</typeparam>
        /// <param name="entityId">Entity to add component to</param>
        /// <param name="component">Component data to add</param>
        /// <returns>True if the component was added</returns>
        public bool AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            if (!EntityExists(entityId))
                return false;

            Type type = component.GetType();
            if (components.ContainsKey(type))
            {
                if (!components[type].ContainsKey(entityId))
                {
                    components[type].Add(entityId, component);
                    return true;
                }
                return false;
            }

            Dictionary<uint, IComponent> temp = new Dictionary<uint, IComponent>
            {
                { entityId, component }
            };
            components.Add(type, temp);
            return true;
        }

        /// <summary>
        /// Removes a component from an entity in the world.
        /// </summary>
        /// <typeparam name="T">Component to remove</typeparam>
        /// <param name="entityId">Entity to remove component from</param>
        /// <returns>True if the component was removed</returns>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            Type type = typeof(T);
            if (EntityExists(entityId) && components.TryGetValue(type, out var value))
            {
                bool removed = value.Remove(entityId);
                if (value.Count < 1)
                    components.Remove(type);
                return removed;
            }
            return false;
        }

        /// <summary>
        /// Removes a component from an entity in the world.
        /// </summary>
        /// <param name="entityId">Entity to remove component from</param>
        /// <param name="type">Type of component to remove</param>
        /// <returns>True if the component was removed</returns>
        public bool RemoveComponent(uint entityId, Type type)
        {
            if (EntityExists(entityId) && components.TryGetValue(type, out var value))
            {
                bool removed = value.Remove(entityId);
                if (value.Count < 1)
                    components.Remove(type);
                return removed;
            }
            return false;
        }

        /// <summary>
        /// Gets a component on an entity.
        /// </summary>
        /// <typeparam name="T">Type of component to get</typeparam>
        /// <param name="entityId">The entity</param>
        /// <returns>The component on the entity</returns>
        public T? GetComponent<T>(uint entityId) where T : IComponent
        {
            Type type = typeof(T);
            if (components.TryGetValue(type, out var value) && value.TryGetValue(entityId, out var component))
                return (T)component;
            return default;
        }

        /// <summary>
        /// Gets a component on an entity.
        /// </summary>
        /// <param name="entityId">The entity</param>
        /// <param name="componentType">Type of component to retrieve (must be IComponent)</param>
        /// <returns>The component on the entity</returns>
        public IComponent? GetComponent(uint entityId, Type componentType)
        {
            if (components.TryGetValue(componentType, out var value) && value.TryGetValue(entityId, out var component))
                return component;
            return default;
        }

        /// <summary>
        /// Gets all components on a specific entity.
        /// </summary>
        /// <param name="entityId">Entity ID to check</param>
        /// <returns>List of all components on entity of <paramref name="entityId"/></returns>
        public List<IComponent> GetAllComponents(uint entityId)
        {
            List<IComponent> entityComponents = [];
            foreach (var componentType in components.Values)
            {
                if (componentType.TryGetValue(entityId, out IComponent? component))
                    entityComponents.Add(component);
            }
            return entityComponents;
        }

        /// <summary>
        /// Checks if an entity has a component of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of component to check for</typeparam>
        /// <param name="entityId">Entity ID to check on</param>
        /// <returns>Whether the entity of <paramref name="entityId"/> has a component type <typeparamref name="T"/></returns>
        public bool HasComponent<T>(uint entityId) where T : IComponent
        {
            Type type = typeof(T);
            return components.ContainsKey(type) && components[type].ContainsKey(entityId);
        }

        /// <summary>
        /// Checks if an entity has a component of a certain type.
        /// </summary>
        /// <param name="entityId">Entity ID to check on</param>
        /// <param name="type">Component type to check for</param>
        /// <returns>Whether the entity of <paramref name="entityId"/> has a certain component type</returns>
        public bool HasComponent(uint entityId, Type type) => components.ContainsKey(type) && components[type].ContainsKey(entityId);

        /// <summary>
        /// Adds a component to the world from ComponentData serialized data.
        /// </summary>
        /// <param name="entityId">Entity to add component to</param>
        /// <param name="componentData">ComponentData object to parse and add</param>
        public void AddComponentFromData(uint entityId, ComponentData componentData)
        {
            try
            {
                // Find component type
                Type? componentType = Type.GetType($"{componentData.TypeName}, {componentData.AssemblyName}") ?? AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == componentData.TypeName &&
                                             typeof(IComponent).IsAssignableFrom(t));
                if (componentType == null || !typeof(IComponent).IsAssignableFrom(componentType))
                {
                    Debug.Error($"Component type not found: {componentData.TypeName}");
                    return;
                }

                // Create component instance
                if (Activator.CreateInstance(componentType) is not IComponent component)
                {
                    Debug.Error($"Failed to create component: {componentData.TypeName}");
                    return;
                }

                // Deserialize component properties
                Deserialize.SetComponentProperties(component, componentData.Properties);

                // Add to world, call generic AddComponent
                var addMethod = GetType().GetMethod("AddComponent")!
                    .MakeGenericMethod(componentType);
                addMethod.Invoke(this, [entityId, component]);
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load component {componentData.TypeName}", ex);
            }
        }

        #endregion
        #region queries

        /// <summary>
        /// Queries the world to find entities with a component.
        /// </summary>
        /// <typeparam name="T">Type of components to find</typeparam>
        /// <returns>All entities with component type <typeparamref name="T"/></returns>
        public IEnumerable<uint> Query<T>() where T : IComponent
        {
            Type type = typeof(T);
            if (components.Count > 0 && components.TryGetValue(type, out Dictionary<uint, IComponent>? value))
                return value.Keys;
            return [];
        }

        /// <summary>
        /// Queries the world to find entities with certain component types.
        /// </summary>
        /// <param name="componentTypes">Component types to discover</param>
        /// <returns>List of entities with component types</returns>
        public IEnumerable<uint> Query(params Type[] componentTypes)
        {
            if (componentTypes.Length == 0) return [];

            IEnumerable<uint> queryResult = GetComponentStore(componentTypes[0]).Keys;
            for (int i = 1; i < componentTypes.Length; i++)
                queryResult = queryResult.Intersect(GetComponentStore(componentTypes[i]).Keys);
            return queryResult;
        }

        /// <summary>
        /// Queries the world to find entities with component types and returns the component data.
        /// </summary>
        /// <typeparam name="T">Type of component to search for</typeparam>
        /// <returns>Tuple of (entityID, T component)</returns>
        public IEnumerable<(uint, T)> QueryData<T>() where T : IComponent
        {
            foreach (uint entityId in Query<T>())
            {
                T queryResultComponent = (T)components[typeof(T)][entityId];
                yield return (entityId, queryResultComponent);
            }
        }

        /// <summary>
        /// Queries the world to find entities with component types and returns the component data.
        /// </summary>
        /// <typeparam name="T1">First type of component to search for</typeparam>
        /// <typeparam name="T2">Second type of component to search for</typeparam>
        /// <returns>Tuple of (entityID, T1 component, T2 component)</returns>
        public IEnumerable<(uint, T1, T2)> QueryData<T1, T2>()
            where T1 : IComponent where T2 : IComponent
        {
            foreach (uint entityId in Query(typeof(T1), typeof(T2)))
            {
                T1 queryResultComponent1 = (T1)components[typeof(T1)][entityId];
                T2 queryResultComponent2 = (T2)components[typeof(T2)][entityId];
                yield return (entityId, queryResultComponent1, queryResultComponent2);
            }
        }

        /// <summary>
        /// Queries all entities that have components of the specified types and returns their identifiers along with
        /// the associated component instances.
        /// </summary>
        /// <remarks>The returned sequence includes only entities that have all three specified component
        /// types. The order of the results is not guaranteed.</remarks>
        /// <typeparam name="T1">The type of the first component to query. Must implement <see cref="IComponent"/>.</typeparam>
        /// <typeparam name="T2">The type of the second component to query. Must implement <see cref="IComponent"/>.</typeparam>
        /// <typeparam name="T3">The type of the third component to query. Must implement <see cref="IComponent"/>.</typeparam>
        /// <returns>An enumerable collection of tuples, each containing the entity identifier and the corresponding instances of
        /// <typeparamref name="T1"/>, <typeparamref name="T2"/>, and <typeparamref name="T3"/> for entities that
        /// possess all specified components.</returns>
        public IEnumerable<(uint, T1, T2, T3)> QueryData<T1, T2, T3>()
            where T1 : IComponent where T2 : IComponent where T3 : IComponent
        {
            foreach (uint entityId in Query(typeof(T1), typeof(T2), typeof(T3)))
            {
                T1 queryResultComponent1 = (T1)components[typeof(T1)][entityId];
                T2 queryResultComponent2 = (T2)components[typeof(T2)][entityId];
                T3 queryResultComponent3 = (T3)components[typeof(T3)][entityId];
                yield return (entityId, queryResultComponent1, queryResultComponent2, queryResultComponent3);
            }
        }

        /// <summary>
        /// Returns an enumerable collection of entity IDs and their associated components matching the specified
        /// component types.
        /// </summary>
        /// <remarks>The order of components in the returned array matches the order of the specified
        /// component types. This method performs a query for entities that have all the requested component types and
        /// yields their IDs along with the corresponding components.</remarks>
        /// <param name="componentTypes">An array of component types to query for. Each entity in the result will have all of these component types
        /// present. Cannot be null or contain null elements.</param>
        /// <returns>An enumerable sequence of tuples, each containing the entity ID and an array of components corresponding to
        /// the specified types. The sequence is empty if no entities match the query.</returns>
        public IEnumerable<(uint, IComponent[])> QueryData(params Type[] componentTypes)
        {
            foreach (uint entityId in Query(componentTypes))
            {
                IComponent[] queryResultComponents = new IComponent[componentTypes.Length];
                for (int i = 0; i < componentTypes.Length; i++)
                    queryResultComponents[i] = components[componentTypes[i]][entityId];

                yield return (entityId, queryResultComponents);
            }
        }

        private Dictionary<uint, IComponent> GetComponentStore(Type t) => components.GetValueOrDefault(t, []);

        #endregion
        #region cloning

        /// <summary>
        /// Creates a deep copy of this world.
        /// </summary>
        /// <returns>A new world with cloned entities and components</returns>
        public World Clone()
        {
            // Make sure editor camera is not cloned
            if (EntityExists(EditorCamera.EditorCameraId)) DestroyEntity(EditorCamera.EditorCameraId);

            World newWorld = new World($"{Name}_Copy")
            {
                NextEntityId = 0
            };

            // Create a mapping from old entity IDs to new entity IDs
            Dictionary<uint, uint> entityIdMap = [];

            // Create all entities in the new world
            foreach (uint oldEntityId in entities)
            {
                uint newEntityId = newWorld.CreateEntity();
                entityIdMap[oldEntityId] = newEntityId;
                
                if (HasComponent<Name>(oldEntityId)) // Copy name component if it exists
                {
                    Name oldName = GetComponent<Name>(oldEntityId)!;
                    newWorld.AddComponent(newEntityId, new Name(oldName.name ?? string.Empty));
                }
            }

            // Copy all components (except Name, already handled)
            foreach (var componentType in components.Keys)
            {
                if (componentType == typeof(Name)) continue;

                foreach (var kvp in components[componentType])
                {
                    uint oldEntityId = kvp.Key;
                    IComponent oldComponent = kvp.Value;

                    if (entityIdMap.TryGetValue(oldEntityId, out uint newEntityId))
                    {
                        IComponent clonedComponent = oldComponent.Clone();
                        newWorld.AddComponent(newEntityId, clonedComponent);
                    }
                }
            }

            // Note: systems are automatically registered and do not need to be cloned
            return newWorld;
        }

        /// <summary>
        /// Restores this world's state from another world (preserving entity IDs).
        /// </summary>
        /// <param name="sourceWorld">The source world to restore from</param>
        public void RestoreFrom(World sourceWorld)
        {
            // Delete editor camera before restoring
            if (EntityExists(EditorCamera.EditorCameraId)) DestroyEntity(EditorCamera.EditorCameraId);

            // Clear current world state
            entities.Clear();
            components.Clear();
            NextEntityId = sourceWorld.NextEntityId;

            // Copy all entities
            foreach (uint entityId in sourceWorld.entities)
            {
                entities.Add(entityId);

                // Copy name component if it exists
                if (sourceWorld.HasComponent<Name>(entityId))
                {
                    Name sourceName = sourceWorld.GetComponent<Name>(entityId)!;
                    AddComponent(entityId, new Name(sourceName.name ?? string.Empty));
                }
            }

            // Copy all other components
            foreach (Type componentType in sourceWorld.components.Keys)
            {
                if (componentType == typeof(Name)) continue;

                foreach (var kvp in sourceWorld.components[componentType])
                {
                    uint entityId = kvp.Key;
                    IComponent sourceComponent = kvp.Value;
                    IComponent clonedComponent = sourceComponent.Clone();
                    AddComponent(entityId, clonedComponent);
                }
            }
        }

        /// <summary>
        /// Gets a dictionary of components cloned from an entity, organized by type.
        /// </summary>
        /// <param name="entityId">Entity to clone components</param>
        /// <returns>Ordered dictionary of cloned components</returns>
        public Dictionary<Type, IComponent> GetClonedComponents(uint entityId)
        {
            Dictionary<Type, IComponent> dict = [];
            foreach (var kv in components)
                if (kv.Value.TryGetValue(entityId, out var comp))
                    dict[kv.Key] = comp.Clone();
            return dict;
        }

        #endregion
    }
}
