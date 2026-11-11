//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
// ComputeSharp used to inject these HLSL type names into every compilation unit.
// They now resolve to DivisionMath, whose vector types work on both the CPU and
// the GPU, so the existing code keeps spelling float3 / int2 / float4x4.
//
global using float2 = DivisionEngine.MathLib.float2;
global using float3 = DivisionEngine.MathLib.float3;
global using float4 = DivisionEngine.MathLib.float4;
global using int2 = DivisionEngine.MathLib.int2;
global using int3 = DivisionEngine.MathLib.int3;
global using int4 = DivisionEngine.MathLib.int4;
global using uint2 = DivisionEngine.MathLib.uint2;
global using uint3 = DivisionEngine.MathLib.uint3;
global using uint4 = DivisionEngine.MathLib.uint4;
global using bool2 = DivisionEngine.MathLib.bool2;
global using bool3 = DivisionEngine.MathLib.bool3;
global using bool4 = DivisionEngine.MathLib.bool4;
global using float2x2 = DivisionEngine.MathLib.float2x2;
global using float2x3 = DivisionEngine.MathLib.float2x3;
global using float2x4 = DivisionEngine.MathLib.float2x4;
global using float3x2 = DivisionEngine.MathLib.float3x2;
global using float3x3 = DivisionEngine.MathLib.float3x3;
global using float3x4 = DivisionEngine.MathLib.float3x4;
global using float4x2 = DivisionEngine.MathLib.float4x2;
global using float4x3 = DivisionEngine.MathLib.float4x3;
global using float4x4 = DivisionEngine.MathLib.float4x4;
