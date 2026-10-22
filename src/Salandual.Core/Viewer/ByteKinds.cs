// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/ByteKinds.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Viewer;

public enum ByteKind { Zero, Text, Control, High, FF }

public static class ByteKinds
{
    public static ByteKind Of(byte b, ByteColors colors) => colors == ByteColors.Two
        ? b is 0 or 0x0D or 0x0A ? ByteKind.Zero : ByteKind.Text
        : b switch
        {
            0 => ByteKind.Zero,
            0xFF => ByteKind.FF,
            >= 0x80 => ByteKind.High,
            < 0x20 or 0x7F => ByteKind.Control,
            _ => ByteKind.Text,
        };

    public static readonly string[] Tokens = [ColorTokens.HexZero, ColorTokens.HexText, ColorTokens.HexControl, ColorTokens.HexHigh, ColorTokens.HexFF];
}
