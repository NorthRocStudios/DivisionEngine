//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.Systems;

namespace DivisionEngine.Tests
{
    internal class TestObjMovementSystem : SystemBase
    {
        public override void Update()
        {
            //UpdateBoxes();
        }

        private void UpdateBoxes()
        {
            foreach (var (_, transform, box) in W.QueryData<Transform, SDFBox>())
            {
                float3 curPos = transform.position;
                transform.position = new float3(curPos.x - TimeSystem.DeltaTimeF * 0.5f, curPos.y, curPos.z);
                box.size = new float3(box.size.x + TimeSystem.DeltaTimeF * 0.1f, box.size.y, box.size.z);
            }
        }
    }
}
