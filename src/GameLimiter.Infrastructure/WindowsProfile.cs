using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace GameLimiter.Infrastructure;

public sealed record WindowsProfile(string User, string Domain, string Sid, string Secret);

[SupportedOSPlatform("windows")]
public static class WindowsProfiles
{
    public static IReadOnlyList<string> LocalAccounts()
    {
        var result = new List<string>(); var resume = 0;
        int status;
        do
        {
            status = NetUserEnum(null, 0, 2, out var buffer, -1, out var read, out _, ref resume);
            try
            {
                if (status != 0 && status != 234) throw new Win32Exception(status);
                for (var i = 0; i < read; i++) result.Add(Environment.MachineName + "\\" + Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * IntPtr.Size)));
            }
            finally { if (buffer != 0) NetApiBufferFree(buffer); }
        } while (status == 234);
        return result.Order().ToArray();
    }
    private static string Key(long child) => "WindowsProfile:" + child;
    public static WindowsProfile? Get(Store store, long child) => JsonSerializer.Deserialize<WindowsProfile>(store.Get(Key(child)) ?? "null");
    public static void Clear(Store store, long child) => store.Set(Key(child), "null");
    public static void Save(Store store, long child, string account, SecureString password)
    {
        if (!store.Children().Any(c => c.Id == child)) throw new ArgumentException("Сначала сохраните ребёнка.");
        var (domain, user) = Parse(account);
        if (password.Length == 0) throw new ArgumentException("Нужен пароль Windows, не PIN Windows Hello. Пустые пароли не поддерживаются.");
        var sid = Validate(user, domain, password);
        var bytes = SecretBytes(password);
        try { store.Set(Key(child), JsonSerializer.Serialize(new WindowsProfile(user, domain, sid, Convert.ToBase64String(Protect(bytes, false))))); }
        finally { Array.Clear(bytes); }
    }
    public static (string Domain, string User) Parse(string account)
    {
        var parts = account.Trim().Split('\\');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || parts[1].Contains('@'))
            throw new ArgumentException("Укажите локального пользователя в формате ИМЯ-ПК\\имя или .\\имя.");
        if (parts[0] != "." && !parts[0].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("В этой beta поддерживаются только локальные пользователи этого компьютера.");
        return (Environment.MachineName, parts[1]);
    }
    public static SecureString ReadPassword(WindowsProfile profile)
    {
        byte[] bytes;
        try { bytes = Protect(Convert.FromBase64String(profile.Secret), true); }
        catch (Exception ex) when (ex is Win32Exception or FormatException) { throw new InvalidOperationException("Не удалось прочитать пароль. Сохраните его заново под текущим пользователем Windows."); }
        try
        {
            var password = new SecureString();
            for (var i = 0; i < bytes.Length; i += 2) password.AppendChar((char)(bytes[i] | bytes[i + 1] << 8));
            password.MakeReadOnly(); return password;
        }
        finally { Array.Clear(bytes); }
    }
    public static string Validate(string user, string domain, SecureString password)
    {
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            if (!LogonUser(user, domain, pointer, 2, 0, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows отклонила вход. Проверьте имя, пароль и состояние учётной записи.");
            try { using var identity = new WindowsIdentity(token); return identity.User!.Value; }
            finally { CloseHandle(token); }
        }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }
    public static bool IsElevated() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    public static bool OwnedBy(Process process, string sid)
    {
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { using var identity = new WindowsIdentity(token); return identity.User?.Value == sid; }
        finally { CloseHandle(token); }
    }
    private static byte[] SecretBytes(SecureString password)
    {
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        try { var bytes = new byte[password.Length * 2]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }
    // DPAPI user scope: encrypted data can be decrypted only in the configuring Windows profile.
    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            var ok = decrypt ? CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output) : CryptProtectData(ref input, null, 0, 0, 0, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
        finally { for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LogonUser(string user, string domain, nint password, int type, int provider, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, int access, out nint token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserEnum(string? server, int level, int filter, out nint buffer, int maximum, out int read, out int total, ref int resume);
    [DllImport("netapi32.dll")] private static extern int NetApiBufferFree(nint buffer);
}
