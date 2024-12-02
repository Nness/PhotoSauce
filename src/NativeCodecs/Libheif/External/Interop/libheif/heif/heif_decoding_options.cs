// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

// Ported from libheif headers (heif.h)
// Original source Copyright (c) struktur AG, Dirk Farin
// See third-party-notices in the repository root for more information.

namespace PhotoSauce.Interop.Libheif;

internal unsafe partial struct heif_decoding_options
{
    [NativeTypeName("uint8_t")]
    public byte version;

    [NativeTypeName("uint8_t")]
    public byte ignore_transformations;

    [NativeTypeName("void (*)(enum heif_progress_step, int, void *)")]
#if NET5_0_OR_GREATER
    public delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void> start_progress;
#else
    public void* _start_progress;

    public delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void> start_progress
    {
        get => (delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void>)_start_progress;
        set => _start_progress = value;
    }
#endif

    [NativeTypeName("void (*)(enum heif_progress_step, int, void *)")]
#if NET5_0_OR_GREATER
    public delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void> on_progress;
#else
    public void* _on_progress;

    public delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void> on_progress
    {
        get => (delegate* unmanaged[Cdecl]<heif_progress_step, int, void*, void>)_on_progress;
        set => _on_progress = value;
    }
#endif

    [NativeTypeName("void (*)(enum heif_progress_step, void *)")]
#if NET5_0_OR_GREATER
    public delegate* unmanaged[Cdecl]<heif_progress_step, void*, void> end_progress;
#else
    public void* _end_progress;

    public delegate* unmanaged[Cdecl]<heif_progress_step, void*, void> end_progress
    {
        get => (delegate* unmanaged[Cdecl]<heif_progress_step, void*, void>)_end_progress;
        set => _end_progress = value;
    }
#endif

    public void* progress_user_data;

    [NativeTypeName("uint8_t")]
    public byte convert_hdr_to_8bit;

    [NativeTypeName("uint8_t")]
    public byte strict_decoding;

    [NativeTypeName("const char *")]
    public sbyte* decoder_id;

    [NativeTypeName("struct heif_color_conversion_options")]
    public heif_color_conversion_options color_conversion_options;

    [NativeTypeName("int (*)(void *)")]
#if NET5_0_OR_GREATER
    public delegate* unmanaged[Cdecl]<void*, int> cancel_decoding;
#else
    public void* _cancel_decoding;

    public delegate* unmanaged[Cdecl]<void*, int> cancel_decoding
    {
        get => (delegate* unmanaged[Cdecl]<void*, int>)_cancel_decoding;
        set => _cancel_decoding = value;
    }
#endif
}
