// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libheif headers (heif.h)
// Original source Copyright (c) struktur AG, Dirk Farin
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libheif;

internal unsafe partial struct heif_error
{
    [NativeTypeName("enum heif_error_code")]
    public heif_error_code code;

    [NativeTypeName("enum heif_suberror_code")]
    public heif_suberror_code subcode;

    [NativeTypeName("const char *")]
    public sbyte* message;
}
