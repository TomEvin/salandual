// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/SelectionCopy.cs

using System.Globalization;
using System.Text;

namespace Salandual.Core.Viewer;

public enum CopyFormat
{
    Text,

    HexBytes,

    CArray,

    Base64,
}

public static class SelectionCopy
{
    public static string Format(ReadOnlySpan<byte> bytes, CopyFormat format)
    {
        switch (format)
        {
            case CopyFormat.HexBytes:
                return string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            case CopyFormat.CArray:
                var text = new StringBuilder("{ ");
                for (var i = 0; i < bytes.Length; i++)
                {
                    if (i > 0)
                    {
                        text.Append(i % 16 == 0 ? ",\n  " : ", ");
                    }

                    text.Append("0x").Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
                }

                return text.Append(" }").ToString();
            case CopyFormat.Base64:
                return Convert.ToBase64String(bytes);
            default:
                return Encoding.Latin1.GetString(bytes);
        }
    }
}
