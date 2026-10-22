// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/FileSignature.cs

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Shell;

public enum SignatureState
{
    Valid,

    NotSigned,

    Changed,

    NotTrusted,
}

public sealed record SignatureInfo(
    SignatureState State,
    bool ThroughCatalog,
    string? SignedBy,
    string? IssuedBy,
    DateTime? SignedOn,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string? Algorithm,
    byte[]? Certificate,
    int ErrorCode);

public static unsafe partial class FileSignature
{
    public static bool CanBeSigned(string path)
        => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".sys" or ".ocx" or ".cpl" or ".scr" or ".drv" or ".efi"
            or ".msi" or ".msp" or ".msix" or ".appx" or ".msixbundle" or ".appxbundle" or ".cab" or ".cat"
            or ".ps1" or ".psm1" or ".psd1" or ".vbs" or ".js" or ".wsf" or ".mui" or ".winmd";

    public static SignatureInfo Check(string path)
    {
        var full = WindowsPath.FromExtended(path);
        var result = Verify(full, catalog: null, out var details);
        if (result is TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown && FindInCatalog(full) is { } found)
        {
            using (found)
            {
                result = Verify(full, found, out details);
                return Make(result, details, throughCatalog: true);
            }
        }

        return Make(result, details, throughCatalog: false);
    }

    private static SignatureInfo Make(int result, Details? d, bool throughCatalog)
    {
        var state = result switch
        {
            0 => SignatureState.Valid,
            TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => SignatureState.NotSigned,
            TrustEBadDigest or CryptEHashValue => SignatureState.Changed,
            _ => SignatureState.NotTrusted,
        };
        if (state == SignatureState.NotSigned || d is null)
        {
            return new SignatureInfo(state, throughCatalog, null, null, null, null, null, null, null, result);
        }

        return new SignatureInfo(state, throughCatalog, d.SignedBy, d.IssuedBy, d.SignedOn, d.ValidFrom, d.ValidTo, d.Algorithm, d.Certificate, result);
    }

    private sealed record Details(string? SignedBy, string? IssuedBy, DateTime? SignedOn, DateTime? ValidFrom, DateTime? ValidTo, string? Algorithm, byte[]? Certificate);

    private static int Verify(string path, CatalogMatch? catalog, out Details? details)
    {
        details = null;
        var action = ActionGenericVerifyV2;
        fixed (char* file = path)
        fixed (char* catalogPath = catalog?.CatalogPath)
        fixed (char* tag = catalog?.MemberTag)
        fixed (byte* hash = catalog?.Hash)
        {
            var fileInfo = new WintrustFileInfo { Size = (uint)sizeof(WintrustFileInfo), FilePath = file };
            var catInfo = new WintrustCatalogInfo
            {
                Size = (uint)sizeof(WintrustCatalogInfo),
                CatalogFilePath = catalogPath,
                MemberTag = tag,
                MemberFilePath = file,
                CalculatedFileHash = hash,
                CalculatedFileHashSize = (uint)(catalog?.Hash.Length ?? 0),
                CatAdmin = catalog?.Admin ?? 0,
            };
            var data = new WintrustData
            {
                Size = (uint)sizeof(WintrustData),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = catalog is null ? WtdChoiceFile : WtdChoiceCatalog,
                Union = catalog is null ? &fileInfo : &catInfo,
                StateAction = WtdStateActionVerify,
                ProvFlags = WtdCacheOnlyUrlRetrieval | WtdRevocationCheckNone,
            };
            var result = WinVerifyTrust(-1 , &action, &data);
            try
            {
                if (data.StateData != 0)
                {
                    details = ReadDetails(data.StateData);
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                details = null;
            }
            finally
            {
                data.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(-1, &action, &data);
            }

            return result;
        }
    }

    private static Details? ReadDetails(nint state)
    {
        var provider = WTHelperProvDataFromStateData(state);
        if (provider == 0)
        {
            return null;
        }

        var signer = (ProviderSigner*)WTHelperGetProvSignerFromChain(provider, 0, 0, 0);
        if (signer is null || signer->CertChainCount == 0 || signer->CertChain is null)
        {
            return null;
        }

        var context = (CertContext*)signer->CertChain[0].Cert;
        if (context is null || context->Encoded is null)
        {
            return null;
        }

        var raw = new ReadOnlySpan<byte>(context->Encoded, (int)context->EncodedSize).ToArray();
        using var cert = X509CertificateLoader.LoadCertificate(raw);
        DateTime? signedOn = null;
        if (signer->CounterSignerCount > 0 && signer->CounterSigners is not null)
        {
            signedOn = FromFileTime(signer->CounterSigners[0].VerifyAsOf);
        }

        string? algorithm = null;
        if (signer->Signer is not null && signer->Signer->HashAlgorithmOid is not null)
        {
            var oid = Marshal.PtrToStringAnsi((nint)signer->Signer->HashAlgorithmOid);
            algorithm = oid switch
            {
                "2.16.840.1.101.3.4.2.1" => "SHA-256",
                "2.16.840.1.101.3.4.2.2" => "SHA-384",
                "2.16.840.1.101.3.4.2.3" => "SHA-512",
                "1.3.14.3.2.26" => "SHA-1",
                "1.2.840.113549.2.5" => "MD5",
                _ => oid,
            };
        }

        return new Details(
            NullIfEmpty(cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false)),
            NullIfEmpty(cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true)),
            signedOn,
            cert.NotBefore,
            cert.NotAfter,
            algorithm,
            raw);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTime? FromFileTime(FileTime t)
    {
        var ticks = ((long)t.High << 32) | t.Low;
        return ticks <= 0 ? null : DateTime.FromFileTime(ticks);
    }

    public static void ViewCertificate(nint owner, byte[] certificate)
    {
        fixed (byte* p = certificate)
        {
            var context = CertCreateCertificateContext(X509AsnEncoding | Pkcs7AsnEncoding, p, (uint)certificate.Length);
            if (context == 0)
            {
                return;
            }

            try
            {
                _ = CryptUIDlgViewContext(CertStoreCertificateContext, context, owner, null, 0, null);
            }
            finally
            {
                CertFreeCertificateContext(context);
            }
        }
    }

    private sealed class CatalogMatch : IDisposable
    {
        public nint Admin;
        public nint Catalog;
        public string CatalogPath = "";
        public string MemberTag = "";
        public byte[] Hash = [];

        public void Dispose()
        {
            if (Catalog != 0)
            {
                CryptCATAdminReleaseCatalogContext(Admin, Catalog, 0);
            }

            if (Admin != 0)
            {
                CryptCATAdminReleaseContext(Admin, 0);
            }
        }
    }

    private static CatalogMatch? FindInCatalog(string path)
    {
        using var file = File.OpenHandle(WindowsPath.ToExtended(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        foreach (var algorithm in new[] { "SHA256", "SHA1" })
        {
            var match = new CatalogMatch();
            var subsystem = DriverActionVerify;
            if (!CryptCATAdminAcquireContext2(out match.Admin, &subsystem, algorithm, 0, 0))
            {
                continue;
            }

            uint size = 0;
            _ = CryptCATAdminCalcHashFromFileHandle2(match.Admin, file.DangerousGetHandle(), &size, null, 0);
            if (size == 0 || size > 128)
            {
                match.Dispose();
                continue;
            }

            match.Hash = new byte[size];
            fixed (byte* h = match.Hash)
            {
                if (!CryptCATAdminCalcHashFromFileHandle2(match.Admin, file.DangerousGetHandle(), &size, h, 0)
                    || (match.Catalog = CryptCATAdminEnumCatalogFromHash(match.Admin, h, size, 0, null)) == 0)
                {
                    match.Dispose();
                    continue;
                }
            }

            var info = new CatalogInfo { Size = (uint)sizeof(CatalogInfo) };
            if (!CryptCATCatalogInfoFromContext(match.Catalog, &info, 0))
            {
                match.Dispose();
                continue;
            }

            match.CatalogPath = new string(info.CatalogFile);
            match.MemberTag = Convert.ToHexString(match.Hash);
            return match;
        }

        return null;
    }

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int CryptEHashValue = unchecked((int)0x80091007);
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdChoiceCatalog = 2;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const uint X509AsnEncoding = 0x1;
    private const uint Pkcs7AsnEncoding = 0x10000;
    private const uint CertStoreCertificateContext = 1;

    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11d1-85E5-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint Size;
        public char* FilePath;
        public nint File;
        public Guid* KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustCatalogInfo
    {
        public uint Size;
        public uint CatalogVersion;
        public char* CatalogFilePath;
        public char* MemberTag;
        public char* MemberFilePath;
        public nint MemberFile;
        public byte* CalculatedFileHash;
        public uint CalculatedFileHashSize;
        public nint CatalogContext;
        public nint CatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public void* Union;
        public uint StateAction;
        public nint StateData;
        public char* UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCert
    {
        public uint Size;
        public nint Cert;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public uint Size;
        public byte* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SignerInfoHead
    {
        public uint Version;
        public Blob Issuer;
        public Blob Serial;
        public byte* HashAlgorithmOid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderSigner
    {
        public uint Size;
        public FileTime VerifyAsOf;
        public uint CertChainCount;
        public ProviderCert* CertChain;
        public uint SignerType;
        public SignerInfoHead* Signer;
        public uint Error;
        public uint CounterSignerCount;
        public ProviderSigner* CounterSigners;
        public nint ChainContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CertContext
    {
        public uint EncodingType;
        public byte* Encoded;
        public uint EncodedSize;
        public nint CertInfo;
        public nint Store;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CatalogInfo
    {
        public uint Size;
        public fixed char CatalogFile[260];
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, Guid* action, WintrustData* data);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperProvDataFromStateData(nint state);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperGetProvSignerFromChain(nint provider, uint signerIndex, int counterSigner, uint counterSignerIndex);

    [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminAcquireContext2(out nint admin, Guid* subsystem, string hashAlgorithm, nint strongHashPolicy, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminCalcHashFromFileHandle2(nint admin, nint file, uint* hashSize, byte* hash, uint flags);

    [LibraryImport("wintrust.dll")]
    private static partial nint CryptCATAdminEnumCatalogFromHash(nint admin, byte* hash, uint hashSize, uint flags, nint* previous);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATCatalogInfoFromContext(nint catalog, CatalogInfo* info, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseCatalogContext(nint admin, nint catalog, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseContext(nint admin, uint flags);

    [LibraryImport("crypt32.dll")]
    private static partial nint CertCreateCertificateContext(uint encoding, byte* data, uint size);

    [LibraryImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CertFreeCertificateContext(nint context);

    [LibraryImport("cryptui.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUIDlgViewContext(uint contextType, nint context, nint owner, string? title, uint flags, void* reserved);
}
