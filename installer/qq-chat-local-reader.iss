#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\artifacts\0.1.0\portable"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts\0.1.0"
#endif

[Setup]
AppId={{5F033A74-0D44-4E7C-B25D-650CFA5EB3CD}
AppName=QQ 聊天本地读取器
AppVersion={#MyAppVersion}
AppPublisher=leaf0329
AppPublisherURL=https://github.com/leaf0329/qq-chat-local-reader
DefaultDirName={localappdata}\Programs\QQChatLocalReader
DefaultGroupName=QQ 聊天本地读取器
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename=qq-chat-local-reader-{#MyAppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\qq-chat-local-reader.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\QQ 聊天本地读取器"; Filename: "{app}\qq-chat-local-reader.exe"
Name: "{autodesktop}\QQ 聊天本地读取器"; Filename: "{app}\qq-chat-local-reader.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Run]
Filename: "{app}\qq-chat-local-reader.exe"; Parameters: "register-codex"; Description: "注册到 Codex（推荐）"; Flags: postinstall skipifsilent runhidden waituntilterminated
Filename: "{app}\qq-chat-local-reader.exe"; Description: "启动 QQ 聊天本地读取器"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\qq-chat-local-reader.exe"; Parameters: "unregister-codex"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  DeleteLocalDataCheckBox: TNewCheckBox;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteLocalDataCheckBox := TNewCheckBox.Create(UninstallProgressForm);
  DeleteLocalDataCheckBox.Parent := UninstallProgressForm.InnerPage;
  DeleteLocalDataCheckBox.Left := 0;
  DeleteLocalDataCheckBox.Top := UninstallProgressForm.StatusLabel.Top + UninstallProgressForm.StatusLabel.Height + ScaleY(20);
  DeleteLocalDataCheckBox.Width := UninstallProgressForm.InnerPage.ClientWidth;
  DeleteLocalDataCheckBox.Caption := '同时删除本地聊天索引和信任配置';
  DeleteLocalDataCheckBox.Checked := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and DeleteLocalDataCheckBox.Checked then
    DelTree(ExpandConstant('{localappdata}\QQChatLocalReader'), True, True, True);
end;
