//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using ComputeSharp;
using DivisionEngine.Components;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Fractals;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects.Assets;
using DivisionEngine.Rendering;
using System.Diagnostics.CodeAnalysis;
using Environment = DivisionEngine.Components.Environment;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Critial system in charge of packaging world render information before each render cycle.
    /// </summary>
    public class SDFRenderSystem : SystemBase
    {
        public const float EPSILON = 0.0001f;

        /// <summary>
        /// Prepared settings for the world information.
        /// </summary>
        public static SDFWorldDTO PreparedWorldDTO { get; private set; }

        /// <summary>
        /// Prepared settings for all SDF objects in the world.
        /// </summary>
        public static SDFObjectDTO[] PreparedSDFObjectsDTO { get; private set; } = [];

        /// <summary>
        /// Prepared settings for all SDF lights in the world.
        /// </summary>
        public static SDFLightDTO[] PreparedLightsDTO { get; private set; } = [];

        /// <summary>
        /// Helper used to quickly get the world data in an allocated compute buffer.
        /// </summary>
        public static ReadOnlyBuffer<SDFWorldDTO>? WorldDataBuffer { get; private set; }

        /// <summary>
        /// Called right before the world is rendered to screen.
        /// </summary>
        public override void Render() => (PreparedWorldDTO, PreparedSDFObjectsDTO, PreparedLightsDTO) = GetFullWorldSDFData();

        /// <summary>
        /// Translates the world to a GPU-relevant format.
        /// </summary>
        /// <returns>ECS world information as data buffers</returns>
        public static (SDFWorldDTO, SDFObjectDTO[], SDFLightDTO[]) GetFullWorldSDFData()
        {
            SDFWorldDTO worldData = new SDFWorldDTO();
            List<SDFObjectDTO> sdfObjects = [];
            List<SDFLightDTO> sdfLights = [];

            // Time
            worldData.time = (float)TimeSystem.Time;

            // Gather camera data
            foreach (var (cameraId, transform, camera) in W.QueryData<Transform, Camera>())
            {
                // Camera transform
                if (camera.isActive)
                {
                    worldData.cameraOrigin = transform.position;
                    worldData.camForward = transform.Forward;
                    worldData.camRight = transform.Right;
                    worldData.camUp = transform.Up;
                }

                if (cameraId != EditorCamera.EditorCameraId)
                {
                    // Camera distances
                    worldData.nearPlane = camera.nearClip;
                    worldData.farPlane = camera.farClip;
                    worldData.camScreenDist = CameraSystem.FovToScreenDistance(camera); // Calc camera screen distance

                    // Ray step counts
                    worldData.maxRaySteps = camera.maxRaySteps;
                    worldData.maxShadowRaySteps = camera.maxShadowRaySteps;
                }
            }

            // Gather post-process data
            foreach (var (cameraId, postProcess) in W.QueryData<PostProcessing>())
            {
                if (cameraId != EditorCamera.EditorCameraId)
                {
                    worldData.enableAces = postProcess.enableAcesTonemapper ? 1 : 0;
                }
            }

            // Gather fog data
            foreach (var (_, fog) in W.QueryData<VolumetricFog>())
            {
                worldData.fogDensity = fog.density;
                worldData.fogColor = fog.color;
                worldData.fogAbsorption = fog.absorption;
                worldData.fogScattering = fog.scattering;
                worldData.fogAnisotropy = fog.anisotropy;
                break; // Use first fog component
            }

            // Gather environment data
            foreach (var (_, environment) in W.QueryData<Environment>())
            {
                worldData.skyColor = environment.skyColor;
                worldData.bottomSkyColor = environment.bottomSkyColor;
                worldData.middleSkyColor = environment.middleSkyColor;
                worldData.topSkyColor = environment.topSkyColor;
                worldData.hdriTexMetaID = environment.hdriMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(environment.hdriMap.ID) : -1;
                worldData.ambientStrength = environment.ambientStrength;
                worldData.mainLightDir = new float3(1, 0, 0);
                worldData.skyType = (int)environment.skyType;
                worldData.skyIntensity = environment.skyIntensity;

                // Wind
                worldData.windDirection = environment.windDirection;
                worldData.windStrength = environment.windStrength;
                worldData.windFrequency = environment.windFrequency;
                break; // Use first environment
            }

            // Gather and transform all lights
            foreach (var (id, transform) in W.QueryData<Transform>())
            {
                // Setup
                SDFLightDTO curLight = new SDFLightDTO
                {
                    position = transform.position,
                    rotation = transform.rotation,
                };

                // Lights
                if (W.HasComponent<DirectionalLight>(id))
                {
                    DirectionalLight light = W.GetComponent<DirectionalLight>(id)!;
                    curLight.type = 0;
                    curLight.color = light.color;
                    curLight.intensity = light.intensity;
                    sdfLights.Add(curLight);
                }
                else if (W.HasComponent<PointLight>(id))
                {
                    PointLight light = W.GetComponent<PointLight>(id)!;
                    curLight.type = 1;
                    curLight.color = light.color;
                    curLight.intensity = light.intensity;
                    curLight.radius = light.radius;
                    sdfLights.Add(curLight);
                }

                // Space to add more lights in the future
            }

            // Create default sun light if no lights exits, to prevent a crash
            if (sdfLights.Count < 1)
            {
                sdfLights.Add(new SDFLightDTO
                {
                    position = new float3(0f, 0f, 0f),
                    rotation = Quaternion.Identity,
                    type = 0,
                    color = ColorPalette.Magenta,
                    intensity = 0f,
                });
            }

            // Calculate main light direction
            for (int i = 0; i < sdfLights.Count; i++)
            {
                if (sdfLights[i].type == 0)
                {
                    worldData.mainLightDir = sdfLights[i].rotation.RotateVector(new float3(0, 0, -1)).Normalize();
                    break;
                }
            }

            // Gather and transform all SDFs and effects
            foreach (var (id, transform) in W.QueryData<Transform>())
            {
                // Setup
                SDFObjectDTO curSDF = new SDFObjectDTO
                {
                    entityID = id,
                    type = -1,
                    position = transform.position,
                    rotation = transform.rotation,
                    scaling = new float3(
                        math.max(1f / transform.scaling.X, EPSILON),
                        math.max(1f / transform.scaling.Y, EPSILON),
                        math.max(1f / transform.scaling.Z, EPSILON)),
                    color = new float4(1f, 0f, 1f, 1f), // Default render color if no material active
                    stepBias = 1f,

                    // Shadows
                    shadowType = 1,
                    shadowEffects = new bool2(true, true),
                    shadowDistances = new float3(0f, 100f, 40f),
                };

                // Effects
                if (W.HasComponent<Shadows>(id))
                {
                    Shadows shadows = W.GetComponent<Shadows>(id)!;
                    curSDF.shadowType = (int)shadows.shadowStyle;
                    curSDF.shadowEffects = new bool2(shadows.shadowCaster, shadows.shadowReceiver);
                    curSDF.shadowDistances = new float3(shadows.minDistance, shadows.maxDistance, shadows.penumbraDistance);
                }
                if (W.HasComponent<Reflections>(id))
                {
                    Reflections reflect = W.GetComponent<Reflections>(id)!;
                    if (reflect.hasReflections) curSDF.hasReflection = 1;
                    else curSDF.hasReflection = 0;
                    if (reflect.reflectionShadows) curSDF.reflectionShadows = 1;
                    else curSDF.reflectionShadows = 0;
                    curSDF.reflectRayStepFalloff = reflect.rayStepsFalloff;
                    curSDF.reflectionMaxBounces = reflect.maxBounces;
                }
                if (W.HasComponent<Refractions>(id))
                {
                    Refractions refract = W.GetComponent<Refractions>(id)!;
                    if (refract.hasRefractions) curSDF.hasRefraction = 1;
                    else curSDF.hasRefraction = 0;
                    curSDF.absorptionColor = refract.absorptionColor;
                    curSDF.refractionMaxSteps = refract.maxRaySteps;
                    curSDF.refractMaxRecursion = refract.maxRecursionTraces;
                }

                // Material
                if (W.HasComponent<SDFMaterial>(id))
                {
                    SDFMaterial mat = W.GetComponent<SDFMaterial>(id)!;

                    // Textures
                    curSDF.albedoTexMetaID = GetTexMetaID(mat.albedoMap);
                    curSDF.normalTexMetaID = mat.normalMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(mat.normalMap.ID) : -1;
                    curSDF.normalStrength = mat.normalStrength;
                    curSDF.displaceTexMetaID = mat.heightMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(mat.heightMap.ID) : -1;
                    curSDF.displaceStrength = mat.displaceStrength;
                    curSDF.roughTexMetaID = mat.roughnessMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(mat.roughnessMap.ID) : -1;
                    curSDF.metalTexMetaID = mat.metallicMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(mat.metallicMap.ID) : -1;
                    curSDF.emissionTexMetaID = mat.emissiveMap.IsLoaded ? TextureSystem.GetTextureMetadataIndex(mat.emissiveMap.ID) : -1;
                    curSDF.texTilingOffset = new float4(mat.uvScale.X, mat.uvScale.Y, mat.uvOffset.X, mat.uvOffset.Y);
                    curSDF.triplanarBlend = mat.triplanarBlend;

                    // Material properties
                    curSDF.color = mat.albedoColor;
                    curSDF.metallic = mat.metallic;
                    curSDF.roughness = mat.roughness;
                    curSDF.specular = mat.specular;
                    curSDF.ior = mat.ior;
                    curSDF.aoValues = new float3(mat.ambientOcclusion, mat.ambientRange, mat.ambientFalloff);
                    curSDF.reflectance = mat.reflectance;
                    curSDF.stepBias = mat.stepBias;

                    // Precalculate material values
                    float s2016 = 0.16f * mat.specular * mat.specular;
                    float3 f0 = new float3(s2016, s2016, s2016);
                    curSDF.f0_reflectance = new float3(
                        math.lerp(f0.X, mat.albedoColor.X, mat.metallic),
                        math.lerp(f0.Y, mat.albedoColor.Y, mat.metallic),
                        math.lerp(f0.Z, mat.albedoColor.Z, mat.metallic));
                    if (curSDF.hasRefraction == 1) curSDF.f0_dielectric = math.pow((mat.ior - 1.0f) / (mat.ior + 1.0f), 2.0f);
                }

                // SDF Objects
                if (W.HasComponent<SDFSphere>(id)) // Check sphere primitive
                {
                    SDFSphere sphere = W.GetComponent<SDFSphere>(id)!;
                    curSDF.type = 0; // Sphere type
                    curSDF.parameters = new float4(sphere.radius, 0f, 0f, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFBox>(id)) // Check box primitive
                {
                    SDFBox box = W.GetComponent<SDFBox>(id)!;
                    curSDF.type = 1; // Box type
                    curSDF.parameters = new float4(box.size.X, box.size.Y, box.size.Z, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFRoundedBox>(id)) // Check rounded box primitive
                {
                    SDFRoundedBox roundedBox = W.GetComponent<SDFRoundedBox>(id)!;
                    curSDF.type = 2; // Rounded box type
                    curSDF.parameters = new float4(roundedBox.size.X, roundedBox.size.Y, roundedBox.size.Z, roundedBox.bevel);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFTorus>(id)) // Check torus primitive
                {
                    SDFTorus torus = W.GetComponent<SDFTorus>(id)!;
                    curSDF.type = 3; // Torus type
                    curSDF.parameters = new float4(torus.wholeRadius, torus.ringRadius, 0f, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFPyramid>(id)) // Check pyramid primitive
                {
                    SDFPyramid pyramid = W.GetComponent<SDFPyramid>(id)!;
                    curSDF.type = 4; // Pyramid type
                    curSDF.parameters = new float4(pyramid.height, 0f, 0f, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFPlane>(id)) // Check plane primitive
                {
                    SDFPlane plane = W.GetComponent<SDFPlane>(id)!;
                    curSDF.type = 5; // Plane type
                    curSDF.parameters = new float4(plane.normal.X, plane.normal.Y, plane.normal.Z, plane.height);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFCylinder>(id)) // Check cylinder primitive
                {
                    SDFCylinder cylinder = W.GetComponent<SDFCylinder>(id)!;
                    curSDF.type = 6; // Cylinder type
                    curSDF.parameters = new float4(cylinder.radius, cylinder.height, 0f, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFCapsule>(id)) // Check capsule primitive
                {
                    SDFCapsule capsule = W.GetComponent<SDFCapsule>(id)!;
                    curSDF.type = 7; // Capsule type
                    curSDF.parameters = new float4(capsule.radius, capsule.height, 0f, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFCone>(id)) // Check cone primitive
                {
                    SDFCone cone = W.GetComponent<SDFCone>(id)!;
                    curSDF.type = 8; // Cone type
                    curSDF.parameters = new float4(cone.cone.X, cone.cone.Y, cone.height, 0f);
                    sdfObjects.Add(curSDF);
                }

                // Fractals
                if (W.HasComponent<SDFMandelbulb>(id))
                {
                    SDFMandelbulb mandelbulb = W.GetComponent<SDFMandelbulb>(id)!;
                    curSDF.type = 10; // Mandelbulb type
                    curSDF.parameters = new float4(mandelbulb.power, mandelbulb.iterations, mandelbulb.bailout, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFMengerSponge>(id))
                {
                    SDFMengerSponge sponge = W.GetComponent<SDFMengerSponge>(id)!;
                    curSDF.type = 11; // Menger sponge type
                    curSDF.parameters = new float4(sponge.size, sponge.holeScale, sponge.iterations, 0f);
                    sdfObjects.Add(curSDF);
                }
                if (W.HasComponent<SDFMandelbox>(id))
                {
                    SDFMandelbox mandelbox = W.GetComponent<SDFMandelbox>(id)!;
                    curSDF.type = 12; // Mandelbox type
                    curSDF.parameters = new float4(mandelbox.scale, mandelbox.minRadius, mandelbox.foldLimit, mandelbox.iterations);
                    sdfObjects.Add(curSDF);
                }

                // Terrains
                if (W.HasComponent<SDFTerrain>(id))
                {
                    SDFTerrain terrain = W.GetComponent<SDFTerrain>(id)!;
                    curSDF.type = 9;
                    curSDF.terrainDataIndex = TerrainSystem.GetTerrainMetadataIndex(id);
                    curSDF.parameters = new float4(terrain.scale, terrain.height, terrain.frequency, terrain.lacunarity);
                    sdfObjects.Add(curSDF);
                }

                // Space to find more SDF objects in the future
            }

            return (worldData, sdfObjects.ToArray(), sdfLights.ToArray());
        }

        private static int GetTexMetaID(AssetRef<TextureAsset> assetRef) =>
            assetRef.IsLoaded ? TextureSystem.GetTextureMetadataIndex(assetRef.ID) : -1;

        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        public static void UploadWorldData(GraphicsDevice device)
        {
            WorldDataBuffer?.Dispose();
            WorldDataBuffer = device.AllocateReadOnlyBuffer([PreparedWorldDTO]);
        }
    }
}
