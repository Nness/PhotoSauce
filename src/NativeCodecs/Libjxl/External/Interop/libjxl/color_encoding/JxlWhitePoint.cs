// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libjxl headers (color_encoding.h)
// Original source Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libjxl;

internal enum JxlWhitePoint
{
    JXL_WHITE_POINT_D65 = 1,
    JXL_WHITE_POINT_CUSTOM = 2,
    JXL_WHITE_POINT_E = 10,
    JXL_WHITE_POINT_DCI = 11,
}
