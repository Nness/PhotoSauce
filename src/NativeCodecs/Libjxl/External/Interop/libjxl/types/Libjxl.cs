// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libjxl headers (types.h)
// Original source Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libjxl;

internal static partial class Libjxl
{
    [NativeTypeName("#define JXL_TRUE 1")]
    public const int JXL_TRUE = 1;

    [NativeTypeName("#define JXL_FALSE 0")]
    public const int JXL_FALSE = 0;
}
