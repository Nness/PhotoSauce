// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libjxl headers (codestream_header.h)
// Original source Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libjxl;

internal partial struct JxlFrameHeader
{
    [NativeTypeName("uint32_t")]
    public uint duration;

    [NativeTypeName("uint32_t")]
    public uint timecode;

    [NativeTypeName("uint32_t")]
    public uint name_length;

    public int is_last;

    public JxlLayerInfo layer_info;
}
