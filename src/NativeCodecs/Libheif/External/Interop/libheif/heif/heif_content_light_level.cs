// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libheif headers (heif.h)
// Original source Copyright (c) struktur AG, Dirk Farin
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libheif;

internal partial struct heif_content_light_level
{
    [NativeTypeName("uint16_t")]
    public ushort max_content_light_level;

    [NativeTypeName("uint16_t")]
    public ushort max_pic_average_light_level;
}
