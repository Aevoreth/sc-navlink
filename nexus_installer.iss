#define AppName    "SC-navLink"
#define ExeName    "SC-navLink.exe"
#define IconFile   "sc-navlink.ico"
; PublishDir can be overridden from the command line (CI passes /DPublishDir=publish_out).
#ifndef PublishDir
  #define PublishDir "NexusApp\bin\x64\Release\net10.0-windows10.0.17763.0\win-x64\publish"
#endif
; Version is single-sourced: by default it's read from the built exe's file
; version (which comes from the csproj <Version>). CI can still override with a
; clean tag value via /DAppVersion=X.Y.Z.
#ifndef AppVersion
  #define AppVersion GetFileVersion(AddBackslash(PublishDir) + ExeName)
#endif

[Setup]
AppId={{F7A2C8D5-3E91-4B6F-A012-7C5E3D8B9F04}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=T3SoD
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#SourcePath}\..
OutputBaseFilename=SC-navLink_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
CloseApplications=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#IconFile}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[InstallDelete]
; A self-contained publish names its files after the assembly. Builds up to 5.0.0
; shipped as Nexus_v4.*; on upgrade, remove those so the app folder is left with
; only the renamed files (no stale Nexus_v4.exe to launch by mistake).
Type: files; Name: "{app}\Nexus_v4.*"
Type: files; Name: "{app}\NexusApp.exe"
Type: files; Name: "{app}\nexus.ico"
; The .NET 10 publish no longer bundles the WinForms/designer assemblies or the 8.0
; debugger DAC. Inno only overwrites shipped files, so on upgrade from a .NET 8
; install these stale runtime files must be removed explicitly.
Type: files; Name: "{app}\System.Windows.Forms.dll"
Type: files; Name: "{app}\System.Windows.Forms.Primitives.dll"
Type: files; Name: "{app}\System.Windows.Forms.Design.dll"
Type: files; Name: "{app}\System.Windows.Forms.Design.Editors.dll"
Type: files; Name: "{app}\WindowsFormsIntegration.dll"
Type: files; Name: "{app}\Microsoft.VisualBasic.Forms.dll"
Type: files; Name: "{app}\System.Design.dll"
Type: files; Name: "{app}\System.Drawing.Design.dll"
Type: files; Name: "{app}\mscordaccore_amd64_amd64_8.0.*.dll"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "NexusApp\Assets\sc-navlink.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"; IconFilename: "{app}\{#IconFile}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; IconFilename: "{app}\{#IconFile}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The marker is written in [Code] (not tracked as an installed file), so remove it explicitly.
Type: files; Name: "{app}\install.marker"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Drop a marker so the running app can tell it was installed via Setup (vs the portable zip).
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\install.marker'), 'installer {#AppVersion}', False);
end;
