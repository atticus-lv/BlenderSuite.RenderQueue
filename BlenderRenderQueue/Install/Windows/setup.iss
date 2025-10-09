; 定义应用程序信息
#define MyAppName "BlenderRenderQueue"
#define MyAppVersion "0.5.1"
#define MyAppPublisher "Atticus"
#define MyAppExeName "BlenderRenderQueue.exe"

; 添加UTF-8编码声明
#pragma coding UTF-8

[Setup]
AppId={{a8239aab-c146-434c-85c1-d6d56bc9b77c}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
WizardStyle=modern

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename={#MyAppName}-{#MyAppVersion}.Setup

SetupIconFile=..\..\Assets\logo.ico
Compression=lzma
SolidCompression=yes

PrivilegesRequired=admin

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "..\license_en.txt"
Name: "chinesesimplified"; MessagesFile: ".\Languages\ChineseSimplified.isl"; LicenseFile: "..\license_zh.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}";

[Files]
Source: "..\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "*.pdb,__pycache__"

[Dirs]
Name: "{app}"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Code]
function GetUninstallString(): String;
forward;
function UninstallOldVersion(): Boolean;
forward;

function GetUninstallString(): String;
var
  sUnInstPath: String;
  sUnInstallString: String;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
  if RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    Result := sUnInstallString
  else
    Result := '';
end;

function UninstallOldVersion(): Boolean;
var
  sUnInstallString: String;
  iResultCode: Integer;
begin
  Result := True;
  sUnInstallString := GetUninstallString();
  if sUnInstallString <> '' then begin
    sUnInstallString := RemoveQuotes(sUnInstallString);
    if MsgBox('A previous version of BlenderRenderQueue is already installed. Do you want to uninstall it first?', 
              mbConfirmation, MB_YESNO) = IDYES then begin
      Exec(sUnInstallString, '/SILENT /NORESTART /SUPPRESSMSGBOXES','', SW_HIDE, ewWaitUntilTerminated, iResultCode);
      Result := True;
    end
    else
      Result := False;
  end;
end;

procedure InitializeWizard;
begin
  if not UninstallOldVersion() then begin
    MsgBox('Installation cancelled.', mbInformation, MB_OK);
    Abort();
  end;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent