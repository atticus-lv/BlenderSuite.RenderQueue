; 定义应用程序信息
#define MyAppName "BlenderRenderQueue"
#define MyAppVersion "0.1.15"
#define MyAppPublisher "Atticus"
#define MyAppExeName "BlenderRenderQueue.exe"

; 添加UTF-8编码声明
#pragma coding UTF-8

[Setup]
; 基本设置
AppId={{a8239aab-c146-434c-85c1-d6d56bc9b77c}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
WizardStyle=modern

; 添加这两行来指定64位应用
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; 目录设置
DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename={#MyAppName}-{#MyAppVersion}.Setup

; 图标和压缩设置
SetupIconFile=..\..\Assets\logo.ico
Compression=lzma
SolidCompression=yes

; 权限和许可设置
PrivilegesRequired=admin
LicenseFile=..\license.txt

; 添加这行来创建注册表项用于卸载
UninstallDisplayName={#MyAppName}
; 添加这行来创建注册表项用于卸载
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
; 使用中文简体界面
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
; 安装任务
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}";

[Files]
; 直接复制发布目录中的所有文件即可
Source: "..\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "*.pdb,__pycache__"

[Dirs]
; 确保主目录
Name: "{app}"

[Icons]
; 快捷方式
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Code]
// 所有函数
function GetUninstallString(): String;
forward;
function UninstallOldVersion(): Boolean;
forward;

// 函数实现
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
    if MsgBox(#$8BE2#$6D4B#$5230#$5DF2#$5B89#$88C5#$7684#$7248#$672C#$FF0C#$662F#$5426#$5148#$5378#$8F7D#$FF1F, 
              mbConfirmation, MB_YESNO) = IDYES then begin
      Exec(sUnInstallString, '/SILENT /NORESTART /SUPPRESSMSGBOXES','', SW_HIDE, ewWaitUntilTerminated, iResultCode);
      Result := True;
    end
    else
      Result := False;
  end;
end;

// 初始化函数
procedure InitializeWizard;
begin
  if not UninstallOldVersion() then begin
    MsgBox('安装已取消。', mbInformation, MB_OK);
    Abort();
  end;
end;

[Run]
; 安装完成后运行
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent