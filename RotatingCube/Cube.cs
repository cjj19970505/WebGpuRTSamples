﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RotatingCube
{
    class Cube
    {
        public const UInt32 CubeVertexSize = 4 * 10; // Byte size of one cube vertex.
        public const UInt32 CubePositionOffset = 0;
        public const UInt32 CubeColorOffset = 4 * 4; // Byte offset of cube vertex color attribute.
        public const UInt32 CubeUVOffset = 4 * 8;

        public static readonly float[] CubeVertexArray = new float[]
        {
          // float4 position, float4 color, float2 uv,
          1, -1, 1, 1,   1, 0, 1, 1,  1, 1,
          -1, -1, 1, 1,  0, 0, 1, 1,  0, 1,
          -1, -1, -1, 1, 0, 0, 0, 1,  0, 0,
          1, -1, -1, 1,  1, 0, 0, 1,  1, 0,
          1, -1, 1, 1,   1, 0, 1, 1,  1, 1,
          -1, -1, -1, 1, 0, 0, 0, 1,  0, 0,

          1, 1, 1, 1,    1, 1, 1, 1,  1, 1,
          1, -1, 1, 1,   1, 0, 1, 1,  0, 1,
          1, -1, -1, 1,  1, 0, 0, 1,  0, 0,
          1, 1, -1, 1,   1, 1, 0, 1,  1, 0,
          1, 1, 1, 1,    1, 1, 1, 1,  1, 1,
          1, -1, -1, 1,  1, 0, 0, 1,  0, 0,

          -1, 1, 1, 1,   0, 1, 1, 1,  1, 1,
          1, 1, 1, 1,    1, 1, 1, 1,  0, 1,
          1, 1, -1, 1,   1, 1, 0, 1,  0, 0,
          -1, 1, -1, 1,  0, 1, 0, 1,  1, 0,
          -1, 1, 1, 1,   0, 1, 1, 1,  1, 1,
          1, 1, -1, 1,   1, 1, 0, 1,  0, 0,

          -1, -1, 1, 1,  0, 0, 1, 1,  1, 1,
          -1, 1, 1, 1,   0, 1, 1, 1,  0, 1,
          -1, 1, -1, 1,  0, 1, 0, 1,  0, 0,
          -1, -1, -1, 1, 0, 0, 0, 1,  1, 0,
          -1, -1, 1, 1,  0, 0, 1, 1,  1, 1,
          -1, 1, -1, 1,  0, 1, 0, 1,  0, 0,

          1, 1, 1, 1,    1, 1, 1, 1,  1, 1,
          -1, 1, 1, 1,   0, 1, 1, 1,  0, 1,
          -1, -1, 1, 1,  0, 0, 1, 1,  0, 0,
          -1, -1, 1, 1,  0, 0, 1, 1,  0, 0,
          1, -1, 1, 1,   1, 0, 1, 1,  1, 0,
          1, 1, 1, 1,    1, 1, 1, 1,  1, 1,

          1, -1, -1, 1,  1, 0, 0, 1,  1, 1,
          -1, -1, -1, 1, 0, 0, 0, 1,  0, 1,
          -1, 1, -1, 1,  0, 1, 0, 1,  0, 0,
          1, 1, -1, 1,   1, 1, 0, 1,  1, 0,
          1, -1, -1, 1,  1, 0, 0, 1,  1, 1,
          -1, 1, -1, 1,  0, 1, 0, 1,  0, 0,
        };
    }
}
