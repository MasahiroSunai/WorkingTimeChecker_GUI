using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// Windows資格情報マネージャーへの
/// 保存、取得、存在確認、削除を行います。
/// </summary>
public sealed class WindowsCredentialService
{
    private const int ErrorNotFound = 1168;

    private const int MaximumCredentialBlobSize =
        5 * 512;

    /// <summary>
    /// 資格情報を保存します。
    /// 同じ識別名の資格情報がある場合は更新します。
    /// </summary>
    /// <param name="targetName">
    /// 資格情報の識別名です。
    /// </param>
    /// <param name="userName">
    /// ユーザー名です。
    /// </param>
    /// <param name="secret">
    /// パスワードまたはトークンです。
    /// </param>
    public void Save(
        string targetName,
        string userName,
        string secret)
    {
        ValidateTargetName(
            targetName);

        ValidateUserName(
            userName);

        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException(
                "パスワードまたはトークンが空です。",
                nameof(secret));
        }

        var secretBytes =
            Encoding.Unicode.GetBytes(
                secret);

        if (secretBytes.Length >
            MaximumCredentialBlobSize)
        {
            throw new ArgumentException(
                "資格情報が長すぎます。" +
                $"最大サイズは{MaximumCredentialBlobSize}バイトです。",
                nameof(secret));
        }

        var secretPointer =
            Marshal.AllocCoTaskMem(
                secretBytes.Length);

        try
        {
            Marshal.Copy(
                secretBytes,
                startIndex: 0,
                secretPointer,
                secretBytes.Length);

            var credential =
                new NativeCredential
                {
                    Type =
                        CredentialType.Generic,

                    TargetName =
                        targetName,

                    CredentialBlobSize =
                        (uint)secretBytes.Length,

                    CredentialBlob =
                        secretPointer,

                    Persist =
                        CredentialPersistence.LocalMachine,

                    UserName =
                        userName,
                };

            if (!CredWrite(
                    ref credential,
                    flags: 0))
            {
                throw CreateWin32Exception(
                    "Windows資格情報を保存できませんでした。");
            }
        }
        finally
        {
            if (secretPointer != IntPtr.Zero)
            {
                ZeroMemory(
                    secretPointer,
                    secretBytes.Length);

                Marshal.FreeCoTaskMem(
                    secretPointer);
            }

            CryptographicOperations.ZeroMemory(
                secretBytes);
        }
    }

    /// <summary>
    /// 指定した識別名の資格情報を取得します。
    /// </summary>
    /// <param name="targetName">
    /// 資格情報の識別名です。
    /// </param>
    /// <returns>
    /// 登録済みの場合は資格情報、
    /// 未登録の場合はnullです。
    /// </returns>
    public StoredCredential? Get(
        string targetName)
    {
        ValidateTargetName(
            targetName);

        if (!CredRead(
                targetName,
                CredentialType.Generic,
                flags: 0,
                out var credentialPointer))
        {
            var errorCode =
                Marshal.GetLastWin32Error();

            if (errorCode == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(
                errorCode,
                "Windows資格情報を取得できませんでした。");
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<NativeCredential>(
                    credentialPointer);

            var userName =
                credential.UserName
                ?? string.Empty;

            var secret =
                ReadSecret(
                    credential.CredentialBlob,
                    credential.CredentialBlobSize);

            return new StoredCredential(
                TargetName:
                    targetName,
                UserName:
                    userName,
                Secret:
                    secret);
        }
        finally
        {
            CredFree(
                credentialPointer);
        }
    }

    /// <summary>
    /// 指定した資格情報が登録済みか確認します。
    /// </summary>
    public bool Exists(
        string targetName)
    {
        return Get(
            targetName) is not null;
    }

    /// <summary>
    /// 指定した資格情報を削除します。
    /// </summary>
    /// <returns>
    /// 削除した場合はtrue、
    /// 未登録だった場合はfalseです。
    /// </returns>
    public bool Remove(
        string targetName)
    {
        ValidateTargetName(
            targetName);

        if (CredDelete(
                targetName,
                CredentialType.Generic,
                flags: 0))
        {
            return true;
        }

        var errorCode =
            Marshal.GetLastWin32Error();

        if (errorCode == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(
            errorCode,
            "Windows資格情報を削除できませんでした。");
    }

    private static string ReadSecret(
        IntPtr credentialBlob,
        uint credentialBlobSize)
    {
        if (credentialBlob == IntPtr.Zero ||
            credentialBlobSize == 0)
        {
            return string.Empty;
        }

        if (credentialBlobSize >
            int.MaxValue)
        {
            throw new InvalidDataException(
                "資格情報のサイズが不正です。");
        }

        var byteLength =
            checked((int)credentialBlobSize);

        if (byteLength % 2 != 0)
        {
            throw new InvalidDataException(
                "資格情報の文字列サイズが不正です。");
        }

        return Marshal.PtrToStringUni(
                   credentialBlob,
                   byteLength / 2)
               ?? string.Empty;
    }

    private static void ValidateTargetName(
        string targetName)
    {
        if (string.IsNullOrWhiteSpace(
                targetName))
        {
            throw new ArgumentException(
                "資格情報の識別名が空です。",
                nameof(targetName));
        }

        var requiredPrefix =
            CredentialNames.Prefix + "/";

        if (!targetName.StartsWith(
                requiredPrefix,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "WorkingTimeChecker以外の資格情報名は" +
                "使用できません。",
                nameof(targetName));
        }
    }

    private static void ValidateUserName(
        string userName)
    {
        if (string.IsNullOrWhiteSpace(
                userName))
        {
            throw new ArgumentException(
                "ユーザー名が空です。",
                nameof(userName));
        }
    }

    private static Win32Exception CreateWin32Exception(
        string message)
    {
        return new Win32Exception(
            Marshal.GetLastWin32Error(),
            message);
    }

    private static void ZeroMemory(
        IntPtr pointer,
        int length)
    {
        for (var index = 0;
             index < length;
             index++)
        {
            Marshal.WriteByte(
                pointer,
                index,
                0);
        }
    }

    private enum CredentialType : uint
    {
        Generic = 1,
    }

    private enum CredentialPersistence : uint
    {
        Session = 1,
        LocalMachine = 2,
        Enterprise = 3,
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;

        public CredentialType Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME
            LastWritten;

        public uint CredentialBlobSize;

        public IntPtr CredentialBlob;

        public CredentialPersistence Persist;

        public uint AttributeCount;

        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        CredentialType type,
        uint flags,
        out IntPtr credential);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        CredentialType type,
        uint flags);

    [DllImport(
        "advapi32.dll",
        SetLastError = false)]
    private static extern void CredFree(
        IntPtr buffer);
}