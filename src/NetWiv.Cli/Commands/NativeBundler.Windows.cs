using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    private static void ValidateWindowsConfiguration(ConfigDocument config, BuildTarget target, IReadOnlyList<string> formats)
    {
        var options = WindowsInstallerOptions.FromJson(config.Root);
        if (target.Platform != "windows" || formats.Count == 0) return; // Windows options may live in a shared cross-platform config.
        _ = WindowsInstallerIdentifier(config);
        WindowsInstallerOptions.ValidateFileName(Product(config), "productName");
        if (config.String("mainBinaryName") is { } binary) WindowsInstallerOptions.ValidateFileName(binary, "mainBinaryName");
        if (formats.Contains("msi"))
        {
            // WiX adds an 8.3 alias and separator to MSI's 128-character Shortcut.Name column.
            if (options.ShortcutName is { Length: > 115 })
                throw new NetWivConfigException("MSI shortcut names must not exceed 115 characters.");
            _ = MsiVersion.Resolve(Version(config), config.String("bundle", "windows", "wix", "version"));
            if (!Guid.TryParse(UpgradeCode(config), out _)) throw new NetWivConfigException("bundle.windows.wix.upgradeCode must be a GUID.");
        }
        if (!options.StartMenu && options.Desktop == false &&
            (options.Arguments.Count > 0 || options.WorkingDirectory != "." || options.ShortcutName is not null))
            throw new NetWivConfigException("Shortcut launch settings require at least one enabled shortcut.");
        if (formats.Contains("msi") && !options.StartMenu && options.Desktop is null &&
            (options.Arguments.Count > 0 || options.WorkingDirectory != "." || options.ShortcutName is not null))
            throw new NetWivConfigException("MSI defaults to no desktop shortcut; enable one to use shortcut launch settings.");
    }

    internal static string RenderNsis(ConfigDocument config, string payload, string binary, string output)
    {
        var identifier = WindowsInstallerIdentifier(config);
        var options = WindowsInstallerOptions.FromJson(config.Root);
        ValidateWorkingDirectory(payload, options.WorkingDirectory);
        var product = EscapeNsis(Product(config));
        var defaultName = InstallerDestinationName(Product(config), identifier + ":" + options.RegistryHive, 200);
        var destination = EscapeNsis(defaultName);
        var name = EscapeNsis(options.ShortcutName ?? defaultName);
        var hive = options.RegistryHive;
        var key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + EscapeNsis(identifier);
        var context = options.PerMachine ? "all" : "current";
        var shortcuts = new StringBuilder();
        // CreateShortcut captures $OUTDIR as the shortcut's working directory.
        shortcuts.AppendLine($"  SetOutPath \"$INSTDIR{(options.WorkingDirectory == "." ? "" : "\\" + EscapeNsis(options.WorkingDirectory))}\"");
        if (options.Desktop ?? true)
            shortcuts.AppendLine($"  CreateShortcut \"$DESKTOP\\{name}.lnk\" \"$INSTDIR\\{EscapeNsis(binary)}\" \"{EscapeNsis(options.CommandLine)}\"");
        if (options.StartMenu)
        {
            shortcuts.AppendLine($"  CreateDirectory \"$SMPROGRAMS\\{destination}\"");
            shortcuts.AppendLine($"  CreateShortcut \"$SMPROGRAMS\\{destination}\\{name}.lnk\" \"$INSTDIR\\{EscapeNsis(binary)}\" \"{EscapeNsis(options.CommandLine)}\"");
        }
        // Only remove package-owned files; a selected directory can contain unrelated user data.
        var longestSuffix = Math.Max(binary.Length, "uninstall.exe".Length);
        longestSuffix = Math.Max(longestSuffix, options.WorkingDirectory == "." ? 0 : options.WorkingDirectory.Length);
        var removal = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            longestSuffix = Math.Max(longestSuffix, Path.GetRelativePath(payload, file).Length);
            removal.AppendLine("  ClearErrors");
            removal.AppendLine($"  Delete \"$INSTDIR\\{EscapeNsis(Path.GetRelativePath(payload, file).Replace(Path.DirectorySeparatorChar, '\\'))}\"");
            removal.AppendLine("  IfErrors uninstall_failed");
        }
        var directories = Directory.EnumerateDirectories(payload, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(payload, path).Replace(Path.DirectorySeparatorChar, '\\')).ToHashSet(StringComparer.Ordinal);
        for (var directory = options.WorkingDirectory; directory != "." && directory.Length > 0;)
        {
            directories.Add(directory);
            var separator = directory.LastIndexOf('\\');
            directory = separator < 0 ? "" : directory[..separator];
        }
        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            longestSuffix = Math.Max(longestSuffix, directory.Length);
            removal.AppendLine($"  RMDir \"$INSTDIR\\{EscapeNsis(directory)}\"");
        }
        return $"""
Unicode true
!include "FileFunc.nsh"
Name "{product}"
OutFile "{EscapeNsisFilePath(output)}"
InstallDir "{(options.PerMachine ? "$PROGRAMFILES64" : "$LOCALAPPDATA")}\{destination}"
RequestExecutionLevel {(options.PerMachine ? "admin" : "user")}
{(options.AllowDirectorySelection ? "Page directory" : "")}
Page instfiles
UninstPage instfiles
Function .onInit
  SetShellVarContext {context}
  ; NSIS hides /D from $CMDLINE and InstallDirRegKey cannot read a 64-bit registry view.
  ; Inspect the original command line only to detect the override; NSIS already parsed its path.
  System::Call 'kernel32::GetCommandLineW() w .r1'
  ClearErrors
  {"${GetOptions}"} $1 "/D=" $2
  IfErrors 0 install_path_ready
  SetRegView 64
  ReadRegStr $0 {hive} "{key}" "InstallLocation"
  StrCmp $0 "" install_path_ready
  StrCpy $INSTDIR $0
install_path_ready:
  SetRegView 32
{(options.PerMachine ? "" : $"""
  ReadRegStr $0 HKCU "Software\Microsoft\EdgeUpdate\Clients\{WebView2Client}" "pv"
  StrCmp $0 "" check_machine
  StrCmp $0 "0.0.0.0" check_machine runtime_found
check_machine:
""")}
  ReadRegStr $0 HKLM "Software\Microsoft\EdgeUpdate\Clients\{WebView2Client}" "pv"
  StrCmp $0 "" missing_runtime
  StrCmp $0 "0.0.0.0" missing_runtime runtime_found
missing_runtime:
  IfSilent runtime_abort
  MessageBox MB_YESNO|MB_ICONEXCLAMATION "Microsoft Edge WebView2 Runtime{(options.PerMachine ? " installed for all users" : "")} is required. Install it, then run this installer again. Open the Microsoft download page?" IDNO runtime_abort
  ExecShell "open" "https://developer.microsoft.com/microsoft-edge/webview2/"
runtime_abort:
  SetErrorLevel 2
  Abort
runtime_found:
  SetRegView 64
FunctionEnd
Section
  ; Check numeric lengths before expansion into an NSIS string can truncate the path.
  StrLen $0 $INSTDIR
  IntOp $0 $0 + {longestSuffix + 1}
  IntCmp $0 1000 path_length_ok path_length_ok
  SetErrorLevel 2
  Abort "Installation paths exceed 1000 characters."
path_length_ok:
  SetShellVarContext {context}
  SetRegView 64
  ; A file-scoped uninstaller knows the previous payload, including files removed in this release.
  ; Never auto-run legacy uninstallers that recursively deleted the selected directory.
  ReadRegDWORD $1 {hive} "{key}" "FileScopedUninstall"
  IntCmp $1 1 0 legacy_links legacy_links
  ReadRegStr $0 {hive} "{key}" "InstallLocation"
  IfFileExists "$0\uninstall.exe" 0 upgrade_failed
  ClearErrors
  ExecWait '$\"$0\uninstall.exe$\" /S _?=$0' $1
  IfErrors upgrade_failed
  StrCmp $1 0 0 upgrade_failed
  ; The in-place uninstaller cannot delete itself; remove it after the process exits.
  ClearErrors
  Delete "$0\uninstall.exe"
  IfErrors upgrade_failed
  RMDir "$0"
  Goto links_clean
upgrade_failed:
  SetErrorLevel 2
  Abort "Could not remove the previous version's package files."
legacy_links:
  ; Remove legacy links before applying changed shortcut options.
  ReadRegStr $0 {hive} "{key}" "InstallLocation"
  StrCmp $0 "" links_clean
  StrCmp $0 $INSTDIR 0 legacy_relocation
  ReadRegStr $0 {hive} "{key}" "ShortcutName"
  StrCmp $0 "" 0 +2
  StrCpy $0 "{product}"
  Delete "$DESKTOP\$0.lnk"
  Delete "$SMPROGRAMS\{product}\$0.lnk"
  RMDir "$SMPROGRAMS\{product}"
  Goto links_clean
legacy_relocation:
  SetErrorLevel 2
  Abort "Uninstall the legacy version before choosing a different installation directory."
links_clean:
  SetOutPath "$INSTDIR"
  File /r "{EscapeNsisFilePath(payload)}\*"
  WriteUninstaller "$INSTDIR\uninstall.exe"
{shortcuts}
  WriteRegDWORD {hive} "{key}" "FileScopedUninstall" 1
  WriteRegStr {hive} "{key}" "ShortcutName" "{name}"
  WriteRegStr {hive} "{key}" "DisplayName" "{product}"
  WriteRegStr {hive} "{key}" "DisplayVersion" "{EscapeNsis(Version(config))}"
  WriteRegStr {hive} "{key}" "Publisher" "{EscapeNsis(Publisher(config))}"
  WriteRegStr {hive} "{key}" "InstallLocation" "$INSTDIR"
  WriteRegStr {hive} "{key}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegStr {hive} "{key}" "QuietUninstallString" '$\"$INSTDIR\uninstall.exe$\" /S'
  WriteRegDWORD {hive} "{key}" "NoModify" 1
  WriteRegDWORD {hive} "{key}" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext {context}
  SetRegView 64
  ; Keep registration, links and the ownership list if a payload file is locked.
{removal}
  {(options.Desktop ?? true ? $"Delete \"$DESKTOP\\{name}.lnk\"" : "")}
  {(options.StartMenu ? $"Delete \"$SMPROGRAMS\\{destination}\\{name}.lnk\"" : "")}
  RMDir "$SMPROGRAMS\{destination}"
  DeleteRegKey {hive} "{key}"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
  Goto uninstall_done
uninstall_failed:
  SetErrorLevel 2
  Abort "Could not remove package files. Close the application and retry removal or upgrade."
uninstall_done:
SectionEnd
""";
    }

    internal static string RenderMsi(ConfigDocument config, string payload, string binary)
    {
        _ = WindowsInstallerIdentifier(config);
        var options = WindowsInstallerOptions.FromJson(config.Root);
        ValidateWorkingDirectory(payload, options.WorkingDirectory);
        var product = Product(config);
        var configuredUpgradeCode = Guid.Parse(UpgradeCode(config)).ToString("D").ToUpperInvariant();
        var upgradeCode = options.PerMachine ? Uuid5(configuredUpgradeCode + ":perMachine") : configuredUpgradeCode;
        var registryKey = @"Software\NetWiv\" + upgradeCode;
        var destination = InstallerDestinationName(product, upgradeCode, 115);
        var shortcutName = options.ShortcutName ?? destination;
        var refs = new StringBuilder();
        var tree = new WixDirectoryNode(string.Empty);
        var machineExecutable = "";
        foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(payload, file);
            var id = "F" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16];
            var guid = Uuid5($"{upgradeCode}:file:{relative}");
            if (options.PerMachine && relative == binary)
                machineExecutable = $"""
<Component Id="C{id}" Guid="{guid}" Win64="yes"><File Id="{id}" Source="{WixXml(file)}" KeyPath="yes">
  {(options.StartMenu ? AdvertisedShortcut("AppShortcut", "ApplicationProgramsFolder") : "")}
  {(options.Desktop == true ? AdvertisedShortcut("DesktopAppShortcut", "DesktopFolder") : "")}
</File>{(options.StartMenu ? "<RemoveFolder Id=\"RemoveProgramsFolder\" Directory=\"ApplicationProgramsFolder\" On=\"uninstall\" />" : "")}</Component>
""";
            else tree.Add(relative, file, id, guid);
            refs.AppendLine($"<ComponentRef Id=\"C{id}\" />");
        }
        if (options.WorkingDirectory != ".") tree.AddDirectory(options.WorkingDirectory.Replace('\\', Path.DirectorySeparatorChar));
        var components = tree.Render(refs, registryKey, "INSTALLDIR", options.RegistryHive, options.PerMachine);
        var shortcuts = new StringBuilder();
        if (options.StartMenu)
            shortcuts.Append($"<Directory Id=\"ProgramMenuFolder\"><Directory Id=\"ApplicationProgramsFolder\" Name=\"{WixXml(destination)}\">{(options.PerMachine ? "" : Shortcut("StartMenuShortcut", "AppShortcut", true))}</Directory></Directory>");
        if (options.Desktop ?? false)
            shortcuts.Append($"<Directory Id=\"DesktopFolder\">{(options.PerMachine ? "" : Shortcut("DesktopShortcut", "DesktopAppShortcut", false))}</Directory>");
        var workDir = options.WorkingDirectory == "." ? "" : MsiLiteral(options.WorkingDirectory);
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="{WixXml(product)}" Language="1033" Codepage="65001" Version="{MsiVersion.Resolve(Version(config), config.String("bundle", "windows", "wix", "version"))}" Manufacturer="{WixXml(Publisher(config))}" UpgradeCode="{upgradeCode}">
    <Package InstallerVersion="500" Compressed="yes" SummaryCodepage="1252" InstallScope="{(options.PerMachine ? "perMachine" : "perUser")}" />
    <MajorUpgrade AllowDowngrades="yes" />
    <MediaTemplate EmbedCab="yes" />
    <Condition Message="This package has a fixed {(options.PerMachine ? "per-machine" : "current-user")} installation scope.">Installed OR {(options.PerMachine ? "ALLUSERS = 1" : "NOT ALLUSERS")}</Condition>
    {(options.PerMachine ? "" : $"<Property Id=\"WEBVIEW2_USER\"><RegistrySearch Id=\"WebView2User\" Root=\"HKCU\" Key=\"Software\\Microsoft\\EdgeUpdate\\Clients\\{WebView2Client}\" Name=\"pv\" Type=\"raw\" Win64=\"no\" /></Property>")}
    <Property Id="WEBVIEW2_MACHINE"><RegistrySearch Id="WebView2Machine" Root="HKLM" Key="Software\Microsoft\EdgeUpdate\Clients\{WebView2Client}" Name="pv" Type="raw" Win64="no" /></Property>
    <Condition Message="Microsoft Edge WebView2 Runtime{(options.PerMachine ? " installed for all users" : "")} is required. Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and run this installer again.">Installed OR {(options.PerMachine ? "" : "(WEBVIEW2_USER AND WEBVIEW2_USER &lt;&gt; &quot;0.0.0.0&quot;) OR ")}(WEBVIEW2_MACHINE AND WEBVIEW2_MACHINE &lt;&gt; "0.0.0.0")</Condition>
    {(options.Arguments.Count == 0 ? "" : $"<Property Id=\"NetWivShortcutArguments\" Value=\"{WixXml(options.CommandLine)}\" />")}
    <Property Id="INSTALLDIR" Secure="yes" />
    <Property Id="EXISTINGINSTALLDIR" Secure="yes"><RegistrySearch Id="PreviousInstallPath" Root="{options.RegistryHive}" Key="{WixXml(registryKey)}" Name="InstallLocation" Type="raw" Win64="{(options.PerMachine ? "yes" : "no")}" /></Property>
    <SetProperty Id="INSTALLDIR" Value="[EXISTINGINSTALLDIR]" Before="CostInitialize" Sequence="both">NOT INSTALLDIR AND EXISTINGINSTALLDIR</SetProperty>
    <SetProperty Id="ARPINSTALLLOCATION" Value="[INSTALLDIR]" After="CostFinalize" Sequence="execute" />
    <SetProperty Id="NETWIVTARGET" Value="[INSTALLDIR]{WixXml(MsiLiteral(binary))}" After="CostFinalize" Sequence="both" />
    <SetProperty Id="NETWIVWORKDIR" Value="[INSTALLDIR]{WixXml(workDir)}" After="CostFinalize" Sequence="both" />
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="{(options.PerMachine ? "ProgramFiles64Folder" : "LocalAppDataFolder")}"><Directory Id="INSTALLDIR" Name="{WixXml(destination)}">
        {components}{machineExecutable}
        <Component Id="InstallLocation" Guid="{Uuid5(upgradeCode + ":location")}" Win64="{(options.PerMachine ? "yes" : "no")}"><RegistryValue Root="{options.RegistryHive}" Key="{WixXml(registryKey)}" Name="InstallLocation" Type="string" Value="[INSTALLDIR]" KeyPath="yes" /></Component>
      </Directory></Directory>
      {shortcuts}
    </Directory>
    <Feature Id="MainFeature" Title="{WixXml(product)}" Level="1">{refs}<ComponentRef Id="InstallLocation" /></Feature>
    {(options.AllowDirectorySelection ? """
    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLDIR" />
    <UIRef Id="WixUI_InstallDir" />
    <UI>
      <!-- No synthetic license agreement: go straight to directory selection. -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg" Order="2">1</Publish>
      <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg" Order="2">1</Publish>
    </UI>
""" : "")}
  </Product>
</Wix>
""";
        // Advertised machine shortcuts use the executable as their key path, avoiding
        // per-user repair keys and ICE38/43/57 violations in an all-users installation.
        string AdvertisedShortcut(string id, string directory) => $"""
<Shortcut Id="{id}" Directory="{directory}" Name="{WixXml(shortcutName)}" Advertise="yes" WorkingDirectory="NETWIVWORKDIR" {(options.Arguments.Count == 0 ? "" : "Arguments=\"[NetWivShortcutArguments]\"")} />
""";

        string Shortcut(string component, string id, bool startMenu)
        {
            refs.AppendLine($"<ComponentRef Id=\"{component}\" />");
            return $"""
<Component Id="{component}" Guid="{Uuid5(upgradeCode + (startMenu ? ":shortcut" : ":desktop-shortcut"))}" Win64="{(options.PerMachine ? "yes" : "no")}">
  <Shortcut Id="{id}" Name="{WixXml(shortcutName)}" Target="[NETWIVTARGET]" WorkingDirectory="NETWIVWORKDIR" {(options.Arguments.Count == 0 ? "" : "Arguments=\"[NetWivShortcutArguments]\"")} />
  {(startMenu ? "<RemoveFolder Id=\"RemoveProgramsFolder\" Directory=\"ApplicationProgramsFolder\" On=\"uninstall\" />" : "")}
  <RegistryValue Root="{options.RegistryHive}" Key="{WixXml(registryKey)}" Name="{component}" Type="integer" Value="1" KeyPath="yes" />
</Component>
""";
        }
    }

    // Display names are not identities: isolate default files and folders across packages.
    private static string InstallerDestinationName(string product, string identity, int limit)
    {
        var suffix = " (" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16] + ")";
        var length = Math.Min(product.Length, limit - suffix.Length);
        if (char.IsHighSurrogate(product[length - 1])) length--;
        return product[..length] + suffix;
    }

    private static void ValidateWorkingDirectory(string payload, string relative)
    {
        var path = payload;
        foreach (var segment in relative.Split('\\'))
        {
            path = Path.Combine(path, segment);
            if (File.Exists(path))
                throw new NetWivConfigException("shortcuts.workingDirectory conflicts with a payload file: " + relative);
        }
    }

    private static string EscapeNsisFilePath(string value) => value.Replace("$", "${U+24}", StringComparison.Ordinal).Replace("\"", "$\\\"", StringComparison.Ordinal);

    // MSI formatted fields interpret brackets; WiX has a separate preprocessing/linking language.
    private static string MsiLiteral(string value) => string.Concat(value.Select(c => c switch { '[' => @"[\[]", ']' => @"[\]]", _ => c.ToString() }));
    // Only escape expressions recognized by WiX v3's linker. Prefix-only matching would
    // add a visible bang to ordinary text such as !(loc.) or $(loc.x).
    [GeneratedRegex(@"[!$]\((?:loc|wix|bind|bindpath)\.[_A-Za-z][0-9A-Za-z_]+(?:\.[_A-Za-z][0-9A-Za-z_.]*)?(?:=.+?)?\)", RegexOptions.Singleline)]
    private static partial Regex WixLinkerExpression();

    private static string WixXml(string value) => Xml(WixLinkerExpression().Replace(value, match => "!" + match.Value)
        .Replace("$", "$$", StringComparison.Ordinal));
}
