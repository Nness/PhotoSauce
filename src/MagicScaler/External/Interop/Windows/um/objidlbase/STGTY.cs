// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT).
// See third-party-notices in the repository root for more information.

// Ported from um/objidlbase.h in the Windows SDK for Windows 10.0.22000.0
// Original source is Copyright © Microsoft. All rights reserved.

namespace TerraFX.Interop.Windows;

internal enum STGTY
{
    STGTY_STORAGE = 1,
    STGTY_STREAM = 2,
    STGTY_LOCKBYTES = 3,
    STGTY_PROPERTY = 4,
}
