// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libjxl headers (color_encoding.h)
// Original source Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libjxl;

internal enum JxlRenderingIntent
{
    JXL_RENDERING_INTENT_PERCEPTUAL = 0,
    JXL_RENDERING_INTENT_RELATIVE,
    JXL_RENDERING_INTENT_SATURATION,
    JXL_RENDERING_INTENT_ABSOLUTE,
}
