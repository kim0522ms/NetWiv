using System.Security.Cryptography;
using System.Text;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    internal static void ValidateConfiguration(ConfigDocument config, BuildTarget target, string? requested)
    {
        var formats = ParseFormats(config, target, requested);
        var options = WindowsInstallerOptions.FromJson(config.Root);
        if (target.Platform != "windows") return; // Windows options may live in a shared cross-platform config.
        WindowsInstallerOptions.ValidateFileName(Product(config), "productName");
        if (config.String("mainBinaryName") is { } binary) WindowsInstallerOptions.ValidateFileName(binary, "mainBinaryName");
        if (formats.Contains("msi"))
        {
            // WiX adds an 8.3 alias and separator to MSI's 128-character Shortcut.Name column.
            if ((options.ShortcutName ?? Product(config)).Length > 115)
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
        var options = WindowsInstallerOptions.FromJson(config.Root);
        var product = EscapeNsis(Product(config));
        var name = EscapeNsis(options.ShortcutName ?? Product(config));
        var hive = options.RegistryHive;
        var key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + EscapeNsis(config.String("identifier")!);
        var context = options.PerMachine ? "all" : "current";
        var shortcuts = new StringBuilder();
        // CreateShortcut captures $OUTDIR as the shortcut's working directory.
        shortcuts.AppendLine($"  SetOutPath \"$INSTDIR{(options.WorkingDirectory == "." ? "" : "\\" + EscapeNsis(options.WorkingDirectory))}\"");
        if (options.Desktop ?? true)
            shortcuts.AppendLine($"  CreateShortcut \"$DESKTOP\\{name}.lnk\" \"$INSTDIR\\{EscapeNsis(binary)}\" \"{EscapeNsis(options.CommandLine)}\"");
        if (options.StartMenu)
        {
            shortcuts.AppendLine($"  CreateDirectory \"$SMPROGRAMS\\{product}\"");
            shortcuts.AppendLine($"  CreateShortcut \"$SMPROGRAMS\\{product}\\{name}.lnk\" \"$INSTDIR\\{EscapeNsis(binary)}\" \"{EscapeNsis(options.CommandLine)}\"");
        }
        return $"""
Unicode true
!include "FileFunc.nsh"
Name "{product}"
OutFile "{EscapeNsisFilePath(output)}"
InstallDir "{(options.PerMachine ? "$PROGRAMFILES64" : "$LOCALAPPDATA")}\{product}"
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
  SetShellVarContext {context}
  SetRegView 64
  ; Remove the previous version's links before applying changed shortcut options.
  ReadRegStr $0 {hive} "{key}" "InstallLocation"
  StrCmp $0 "" links_clean
  ReadRegStr $0 {hive} "{key}" "ShortcutName"
  StrCmp $0 "" 0 +2
  StrCpy $0 "{product}"
  Delete "$DESKTOP\$0.lnk"
  Delete "$SMPROGRAMS\{product}\$0.lnk"
  RMDir "$SMPROGRAMS\{product}"
links_clean:
  SetOutPath "$INSTDIR"
  File /r "{EscapeNsisFilePath(payload)}\*"
  WriteUninstaller "$INSTDIR\uninstall.exe"
{shortcuts}
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
  {(options.Desktop ?? true ? $"Delete \"$DESKTOP\\{name}.lnk\"" : "")}
  {(options.StartMenu ? $"Delete \"$SMPROGRAMS\\{product}\\{name}.lnk\"" : "")}
  RMDir "$SMPROGRAMS\{product}"
  DeleteRegKey {hive} "{key}"
  RMDir /r "$INSTDIR"
SectionEnd
""";
    }

    internal static string RenderMsi(ConfigDocument config, string payload, string binary)
    {
        var options = WindowsInstallerOptions.FromJson(config.Root);
        var product = Product(config);
        var upgradeCode = options.PerMachine ? Uuid5(UpgradeCode(config) + ":perMachine") : UpgradeCode(config);
        var registryKey = @"Software\NetWiv\" + upgradeCode;
        var refs = new StringBuilder();
        var tree = new WixDirectoryNode(string.Empty);
        foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(payload, file);
            var id = "F" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16];
            tree.Add(relative, file, id, Uuid5($"{product}:{relative}" + (options.PerMachine ? ":perMachine" : "")));
            refs.AppendLine($"<ComponentRef Id=\"C{id}\" />");
        }
        if (options.WorkingDirectory != ".") tree.AddDirectory(options.WorkingDirectory.Replace('\\', Path.DirectorySeparatorChar));
        var components = tree.Render(refs, registryKey, "INSTALLDIR", options.RegistryHive, options.PerMachine);
        var shortcuts = new StringBuilder();
        if (options.StartMenu)
            shortcuts.Append($"<Directory Id=\"ProgramMenuFolder\"><Directory Id=\"ApplicationProgramsFolder\" Name=\"{WixXml(SafeName(product))}\">{Shortcut("StartMenuShortcut", "AppShortcut", true)}</Directory></Directory>");
        if (options.Desktop ?? false)
            shortcuts.Append($"<Directory Id=\"DesktopFolder\">{Shortcut("DesktopShortcut", "DesktopAppShortcut", false)}</Directory>");
        var workDir = options.WorkingDirectory == "." ? "" : MsiLiteral(options.WorkingDirectory);
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="{WixXml(product)}" Language="1033" Codepage="65001" Version="{MsiVersion.Resolve(Version(config), config.String("bundle", "windows", "wix", "version"))}" Manufacturer="{WixXml(Publisher(config))}" UpgradeCode="{upgradeCode}">
    <Package InstallerVersion="500" Compressed="yes" SummaryCodepage="65001" InstallScope="{(options.PerMachine ? "perMachine" : "perUser")}" />
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
      <Directory Id="{(options.PerMachine ? "ProgramFiles64Folder" : "LocalAppDataFolder")}"><Directory Id="INSTALLDIR" Name="{WixXml(SafeName(product))}">
        {components}
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
        string Shortcut(string component, string id, bool startMenu)
        {
            refs.AppendLine($"<ComponentRef Id=\"{component}\" />");
            return $"""
<Component Id="{component}" Guid="{Uuid5(upgradeCode + (startMenu ? ":shortcut" : ":desktop-shortcut"))}" Win64="{(options.PerMachine ? "yes" : "no")}">
  <Shortcut Id="{id}" Name="{WixXml(options.ShortcutName ?? product)}" Target="[NETWIVTARGET]" WorkingDirectory="NETWIVWORKDIR" {(options.Arguments.Count == 0 ? "" : "Arguments=\"[NetWivShortcutArguments]\"")} />
  {(startMenu ? "<RemoveFolder Id=\"RemoveProgramsFolder\" Directory=\"ApplicationProgramsFolder\" On=\"uninstall\" />" : "")}
  <RegistryValue Root="{options.RegistryHive}" Key="{WixXml(registryKey)}" Name="{component}" Type="integer" Value="1" KeyPath="yes" />
</Component>
""";
        }
    }

    private static string EscapeNsisFilePath(string value) => value.Replace("$", "${U+24}", StringComparison.Ordinal).Replace("\"", "$\\\"", StringComparison.Ordinal);

    // MSI formatted fields interpret brackets; WiX has a separate preprocessing/linking language.
    private static string MsiLiteral(string value) => string.Concat(value.Select(c => c switch { '[' => @"[\[]", ']' => @"[\]]", _ => c.ToString() }));
    private static string WixXml(string value)
    {
        value = value.Replace("$", "$$", StringComparison.Ordinal);
        foreach (var prefix in new[] { "loc", "wix", "bind", "bindpath" })
            value = value.Replace("!(" + prefix + ".", "!!(" + prefix + ".", StringComparison.Ordinal)
                .Replace("$$(" + prefix + ".", "!$$(" + prefix + ".", StringComparison.Ordinal);
        return Xml(value);
    }
}
